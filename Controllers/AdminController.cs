using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TaskManagementSystem.Attributes;
using TaskManagementSystem.Data;
using TaskManagementSystem.Models;
using TaskManagementSystem.Services;
using TaskManagementSystem.ViewModels;
using TaskStatus = TaskManagementSystem.Models.TaskStatus;
using TaskPriority = TaskManagementSystem.Models.TaskPriority;
using System.IO;

namespace TaskManagementSystem.Controllers
{
    [Authorize(Roles = "SuperAdmin,ITAdmin,SodeinAdmin,FoodBankAdmin")]
    [DepartmentFilter]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IUserService _userService;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public AdminController(ApplicationDbContext context, IUserService userService, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _userService = userService;
            _webHostEnvironment = webHostEnvironment;
        }

        private async Task<int> GetCurrentDepartmentId()
        {
            var departmentIdClaim = User.FindFirst("DepartmentId")?.Value;
            return int.Parse(departmentIdClaim ?? "0");
        }

        private async Task<int> GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.Parse(userIdClaim ?? "0");
        }

        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            var departmentId = await GetCurrentDepartmentId();
            var userId = await GetCurrentUserId();
            var user = await _userService.GetUserById(userId);
            var departmentName = User.FindFirst("DepartmentName")?.Value ?? "Unknown";
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? "Unknown";

            var tasks = await _context.Tasks
                .Where(t => t.DepartmentId == departmentId)
                .ToListAsync();

            var workers = await _userService.GetWorkersByDepartment(departmentId);

            var viewModel = new DashboardViewModel
            {
                TotalTasks = tasks.Count,
                PendingTasks = tasks.Count(t => t.Status == TaskStatus.Pending),
                InProgressTasks = tasks.Count(t => t.Status == TaskStatus.InProgress),
                CompletedTasks = tasks.Count(t => t.Status == TaskStatus.Completed),
                ActiveWorkers = workers.Count,
                CriticalTasks = tasks.Count(t => t.Priority == TaskPriority.Critical && t.Status != TaskStatus.Completed),
                OverdueTasks = tasks.Count(t => t.DueDate < DateTime.Now && t.Status != TaskStatus.Completed),
                DepartmentName = departmentName,
                UserFullName = user?.FullName ?? "Unknown",
                UserRole = userRole
            };

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> ManageWorkers()
        {
            var departmentId = await GetCurrentDepartmentId();
            var workers = await _userService.GetWorkersByDepartment(departmentId);

            return View(workers);
        }

        [HttpGet]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> ManageAdmins()
        {
            var adminRoles = new[] { "ITAdmin", "FoodBankAdmin", "SodeinAdmin" };

            var admins = await _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Department)
                .Where(u => u.UserRoles.Any(ur => adminRoles.Contains(ur.Role.RoleName)))
                .ToListAsync();

            return View(admins);
        }

        [HttpGet]
        public async Task<IActionResult> CreateTask()
        {
            var departmentId = await GetCurrentDepartmentId();
            var workers = await _userService.GetWorkersByDepartment(departmentId);

            ViewBag.Workers = new SelectList(workers, "UserId", "FullName");
            
            var priorities = Enum.GetValues(typeof(TaskPriority))
                .Cast<TaskPriority>()
                .Select(p => new { Value = (int)p, Text = p.ToString() })
                .ToList();
            
            ViewBag.Priorities = new SelectList(priorities, "Value", "Text");

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateTask(CreateTaskViewModel model)
        {
            if (!ModelState.IsValid)
            {
                await LoadCreateTaskDropdowns();
                return View(model);
            }

            var departmentId = await GetCurrentDepartmentId();
            var userId = await GetCurrentUserId();

            var worker = await _context.UserRoles
                .Where(ur => ur.UserId == model.AssignedToUserId && ur.DepartmentId == departmentId)
                .FirstOrDefaultAsync();

            if (worker == null)
            {
                ModelState.AddModelError(string.Empty, "Selected worker is not in your department.");
                await LoadCreateTaskDropdowns();
                return View(model);
            }

            var task = new TaskItem
            {
                Title = model.Title,
                Description = model.Description,
                DepartmentId = departmentId,
                AssignedToUserId = model.AssignedToUserId,
                CreatedByUserId = userId,
                Priority = model.Priority,
                Status = TaskStatus.Pending,
                DueDate = model.DueDate,
                CreatedDate = DateTime.Now
            };

            // Handle file upload
            if (model.AttachmentFile != null && model.AttachmentFile.Length > 0)
            {
                const long maxFileSize = 10 * 1024 * 1024; // 10MB
                var allowedExtensions = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".gif" };

                if (model.AttachmentFile.Length > maxFileSize)
                {
                    ModelState.AddModelError("AttachmentFile", "File size cannot exceed 10MB.");
                    await LoadCreateTaskDropdowns();
                    return View(model);
                }

                var fileExtension = Path.GetExtension(model.AttachmentFile.FileName).ToLower();
                if (!allowedExtensions.Contains(fileExtension))
                {
                    ModelState.AddModelError("AttachmentFile", "Only PDF, JPG, PNG, and GIF files are allowed.");
                    await LoadCreateTaskDropdowns();
                    return View(model);
                }

                // Generate unique filename
                var fileName = $"{Guid.NewGuid()}_{DateTime.Now:yyyyMMddHHmmss}{fileExtension}";
                var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads");

                // Create uploads directory if it doesn't exist
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                var filePath = Path.Combine(uploadsFolder, fileName);

                try
                {
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await model.AttachmentFile.CopyToAsync(fileStream);
                    }

                    task.AttachmentFileName = model.AttachmentFile.FileName;
                    task.AttachmentPath = $"/uploads/{fileName}";
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("AttachmentFile", $"Error uploading file: {ex.Message}");
                    await LoadCreateTaskDropdowns();
                    return View(model);
                }
            }

            _context.Tasks.Add(task);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Task created successfully!";
            return RedirectToAction("TaskList");
        }

        [HttpGet]
        public async Task<IActionResult> TaskList(TaskFilterViewModel filter)
        {
            var departmentId = await GetCurrentDepartmentId();

            var query = _context.Tasks
                .Include(t => t.AssignedToUser)
                .Include(t => t.CreatedByUser)
                .Include(t => t.Department)
                .Where(t => t.DepartmentId == departmentId);

            if (!string.IsNullOrEmpty(filter.SearchTerm))
            {
                query = query.Where(t => t.Title.Contains(filter.SearchTerm) || 
                                        (t.Description != null && t.Description.Contains(filter.SearchTerm)));
            }

            if (filter.Status.HasValue)
            {
                query = query.Where(t => t.Status == filter.Status.Value);
            }

            if (filter.Priority.HasValue)
            {
                query = query.Where(t => t.Priority == filter.Priority.Value);
            }

            if (filter.AssignedToUserId.HasValue)
            {
                query = query.Where(t => t.AssignedToUserId == filter.AssignedToUserId.Value);
            }

            if (filter.FromDate.HasValue)
            {
                query = query.Where(t => t.CreatedDate >= filter.FromDate.Value);
            }

            if (filter.ToDate.HasValue)
            {
                query = query.Where(t => t.CreatedDate <= filter.ToDate.Value);
            }

            var totalRecords = await query.CountAsync();
            var tasks = await query
                .OrderByDescending(t => t.CreatedDate)
                .Skip((filter.PageNumber - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();

            ViewBag.TotalPages = (int)Math.Ceiling(totalRecords / (double)filter.PageSize);
            ViewBag.CurrentPage = filter.PageNumber;
            ViewBag.Filter = filter;

            var workers = await _userService.GetWorkersByDepartment(departmentId);
            ViewBag.Workers = new SelectList(workers, "UserId", "FullName");
            
            // Get all status and priority enum values
            var statuses = Enum.GetValues(typeof(TaskStatus))
                .Cast<TaskStatus>()
                .Select(s => new { Value = (int)s, Text = s.ToString() })
                .ToList();
            
            var priorities = Enum.GetValues(typeof(TaskPriority))
                .Cast<TaskPriority>()
                .Select(p => new { Value = (int)p, Text = p.ToString() })
                .ToList();
            
            ViewBag.Statuses = new SelectList(statuses, "Value", "Text");
            ViewBag.Priorities = new SelectList(priorities, "Value", "Text");

            return View(tasks);
        }

        [HttpGet]
        public async Task<IActionResult> EditTask(int id)
        {
            var departmentId = await GetCurrentDepartmentId();

            var task = await _context.Tasks
                .Include(t => t.AssignedToUser)
                .Include(t => t.Department)
                .FirstOrDefaultAsync(t => t.TaskId == id && t.DepartmentId == departmentId);

            if (task == null)
            {
                return NotFound();
            }

            var workers = await _userService.GetWorkersByDepartment(departmentId);
            ViewBag.Workers = new SelectList(workers, "UserId", "FullName", task.AssignedToUserId);
            
            var priorities = Enum.GetValues(typeof(TaskPriority))
                .Cast<TaskPriority>()
                .Select(p => new { Value = (int)p, Text = p.ToString() })
                .ToList();
            
            var statuses = Enum.GetValues(typeof(TaskStatus))
                .Cast<TaskStatus>()
                .Select(s => new { Value = (int)s, Text = s.ToString() })
                .ToList();
            
            ViewBag.Priorities = new SelectList(priorities, "Value", "Text", (int)task.Priority);
            ViewBag.Statuses = new SelectList(statuses, "Value", "Text", (int)task.Status);

            return View(task);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTask(int id, TaskItem model)
        {
            var departmentId = await GetCurrentDepartmentId();

            var task = await _context.Tasks
                .FirstOrDefaultAsync(t => t.TaskId == id && t.DepartmentId == departmentId);

            if (task == null)
            {
                return NotFound();
            }

            task.Title = model.Title;
            task.Description = model.Description;
            task.AssignedToUserId = model.AssignedToUserId;
            task.Priority = model.Priority;
            task.Status = model.Status;
            task.DueDate = model.DueDate;
            task.UpdatedDate = DateTime.Now;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Task updated successfully!";
            return RedirectToAction("TaskList");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTask(int id)
        {
            var departmentId = await GetCurrentDepartmentId();

            var task = await _context.Tasks
                .FirstOrDefaultAsync(t => t.TaskId == id && t.DepartmentId == departmentId);

            if (task == null)
            {
                return NotFound();
            }

            _context.Tasks.Remove(task);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Task deleted successfully!";
            return RedirectToAction("TaskList");
        }

        [HttpGet]
        public async Task<IActionResult> ViewTaskDetails(int id)
        {
            var departmentId = await GetCurrentDepartmentId();

            var task = await _context.Tasks
                .Include(t => t.AssignedToUser)
                .Include(t => t.CreatedByUser)
                .Include(t => t.Department)
                .Include(t => t.TaskUpdates)
                    .ThenInclude(tu => tu.UpdatedByUser)
                .FirstOrDefaultAsync(t => t.TaskId == id && t.DepartmentId == departmentId);

            if (task == null)
            {
                return NotFound();
            }

            return View(task);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReviewTaskCompletion(int id, bool approveAsCompleted, string? reviewMessage)
        {
            var departmentId = await GetCurrentDepartmentId();
            var adminUserId = await GetCurrentUserId();

            var task = await _context.Tasks
                .FirstOrDefaultAsync(t => t.TaskId == id && t.DepartmentId == departmentId);

            if (task == null)
            {
                return NotFound();
            }

            task.Status = approveAsCompleted ? TaskStatus.Completed : TaskStatus.InProgress;
            task.UpdatedDate = DateTime.Now;

            var message = string.IsNullOrWhiteSpace(reviewMessage)
                ? (approveAsCompleted
                    ? "Completion approved by admin. Status changed to Completed."
                    : "Completion not approved by admin. Status kept In Progress.")
                : reviewMessage.Trim();

            _context.TaskUpdates.Add(new TaskUpdate
            {
                TaskId = task.TaskId,
                UpdatedByUserId = adminUserId,
                UpdateDescription = message,
                OldStatus = null,
                NewStatus = task.Status,
                UpdatedDate = DateTime.Now
            });

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = approveAsCompleted
                ? "Task approved and marked as completed."
                : "Task kept in progress.";

            return RedirectToAction("ViewTaskDetails", new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendOverdueReminder(int id, string? reminderMessage)
        {
            var departmentId = await GetCurrentDepartmentId();
            var adminUserId = await GetCurrentUserId();

            var task = await _context.Tasks
                .FirstOrDefaultAsync(t => t.TaskId == id && t.DepartmentId == departmentId);

            if (task == null)
            {
                return NotFound();
            }

            if (!task.DueDate.HasValue || task.DueDate.Value >= DateTime.Now)
            {
                TempData["ErrorMessage"] = "This task is not overdue yet.";
                return RedirectToAction("ViewTaskDetails", new { id });
            }

            if (task.Status == TaskStatus.Completed)
            {
                TempData["ErrorMessage"] = "Completed tasks do not need overdue reminders.";
                return RedirectToAction("ViewTaskDetails", new { id });
            }

            var message = string.IsNullOrWhiteSpace(reminderMessage)
                ? $"Reminder: This task is overdue. Due date was {task.DueDate:MMMM dd, yyyy hh:mm tt}. Please update it as soon as possible."
                : reminderMessage.Trim();

            var taskUpdate = new TaskUpdate
            {
                TaskId = task.TaskId,
                UpdatedByUserId = adminUserId,
                UpdateDescription = message,
                UpdatedDate = DateTime.Now
            };

            _context.TaskUpdates.Add(taskUpdate);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Overdue reminder sent to the worker.";
            return RedirectToAction("ViewTaskDetails", new { id });
        }

        [HttpGet]
        public async Task<IActionResult> Reports()
        {
            var departmentId = await GetCurrentDepartmentId();

            var tasks = await _context.Tasks
                .Include(t => t.AssignedToUser)
                .Include(t => t.CreatedByUser)
                .Where(t => t.DepartmentId == departmentId)
                .OrderByDescending(t => t.CreatedDate)
                .ToListAsync();

            return View(tasks);
        }

        [HttpGet]
        public async Task<IActionResult> EditUser(int id)
        {
            var departmentId = await GetCurrentDepartmentId();

            var user = await _context.Users
                .Include(u => u.UserRoles)
                .FirstOrDefaultAsync(u => u.UserId == id);

            if (user == null)
            {
                return NotFound();
            }

            // Check if user is in the same department
            var userDepartment = await _context.UserRoles
                .FirstOrDefaultAsync(ur => ur.UserId == id && ur.DepartmentId == departmentId);

            if (userDepartment == null)
            {
                return Forbid();
            }

            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUser(int id, User model)
        {
            var departmentId = await GetCurrentDepartmentId();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == id);

            if (user == null)
            {
                return NotFound();
            }

            // Check if user is in the same department
            var userDepartment = await _context.UserRoles
                .FirstOrDefaultAsync(ur => ur.UserId == id && ur.DepartmentId == departmentId);

            if (userDepartment == null)
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                return View(user);
            }

            user.FullName = model.FullName;
            user.Email = model.Email;
            user.PhoneNumber = model.PhoneNumber;
            user.IsActive = model.IsActive;

            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "User updated successfully!";
            return RedirectToAction("ManageWorkers");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var departmentId = await GetCurrentDepartmentId();
            var currentUserId = await GetCurrentUserId();

            if (id == currentUserId)
            {
                TempData["ErrorMessage"] = "You cannot delete your own account!";
                return RedirectToAction("ManageWorkers");
            }

            var user = await _context.Users
                .Include(u => u.AssignedTasks)
                .FirstOrDefaultAsync(u => u.UserId == id);

            if (user == null)
            {
                return NotFound();
            }

            // Check if user is in the same department
            var userDepartment = await _context.UserRoles
                .FirstOrDefaultAsync(ur => ur.UserId == id && ur.DepartmentId == departmentId);

            if (userDepartment == null)
            {
                return Forbid();
            }

            // Check if user has any assigned tasks
            if (user.AssignedTasks.Any())
            {
                TempData["ErrorMessage"] = "Cannot delete user with assigned tasks. Please reassign or complete their tasks first.";
                return RedirectToAction("ManageWorkers");
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "User deleted successfully!";
            return RedirectToAction("ManageWorkers");
        }

        [HttpGet]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> EditAdmin(int id)
        {
            var user = await _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Department)
                .FirstOrDefaultAsync(u => u.UserId == id);

            if (user == null)
            {
                return NotFound();
            }

            var adminRoles = new[] { "ITAdmin", "FoodBankAdmin", "SuddenAdmin" };
            var isAdmin = user.UserRoles.Any(ur => adminRoles.Contains(ur.Role.RoleName));

            if (!isAdmin)
            {
                return Forbid();
            }

            var departments = await _context.Departments.ToListAsync();
            ViewBag.Departments = new SelectList(departments, "DepartmentId", "DepartmentName");

            var roles = await _context.Roles
                .Where(r => adminRoles.Contains(r.RoleName))
                .ToListAsync();
            ViewBag.Roles = new SelectList(roles, "RoleId", "RoleName");

            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> EditAdmin(int id, User model, int departmentId, int roleId)
        {
            var user = await _context.Users
                .Include(u => u.UserRoles)
                .FirstOrDefaultAsync(u => u.UserId == id);

            if (user == null)
            {
                return NotFound();
            }

            var adminRoles = new[] { "ITAdmin", "FoodBankAdmin", "SuddenAdmin" };
            var isAdmin = user.UserRoles.Any(ur => adminRoles.Contains(ur.Role.RoleName));

            if (!isAdmin)
            {
                return Forbid();
            }

            user.FullName = model.FullName;
            user.Email = model.Email;
            user.PhoneNumber = model.PhoneNumber;
            user.IsActive = model.IsActive;

            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Admin account updated successfully!";
            return RedirectToAction("ManageAdmins");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> DeleteAdmin(int id)
        {
            var currentUserId = await GetCurrentUserId();

            if (id == currentUserId)
            {
                TempData["ErrorMessage"] = "You cannot delete your own admin account!";
                return RedirectToAction("ManageAdmins");
            }

            var user = await _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .Include(u => u.CreatedTasks)
                .FirstOrDefaultAsync(u => u.UserId == id);

            if (user == null)
            {
                return NotFound();
            }

            var adminRoles = new[] { "ITAdmin", "FoodBankAdmin", "SodeinAdmin" };
            var isAdmin = user.UserRoles.Any(ur => adminRoles.Contains(ur.Role.RoleName));

            if (!isAdmin)
            {
                return Forbid();
            }

            // Check if admin has any created tasks
            if (user.CreatedTasks.Any())
            {
                TempData["ErrorMessage"] = "Cannot delete admin with created tasks. Please reassign or delete their tasks first.";
                return RedirectToAction("ManageAdmins");
            }

            // Remove user roles first
            var userRoles = await _context.UserRoles.Where(ur => ur.UserId == id).ToListAsync();
            _context.UserRoles.RemoveRange(userRoles);

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Admin account deleted successfully!";
            return RedirectToAction("ManageAdmins");
        }

        private async Task LoadCreateTaskDropdowns()
        {
            var departmentId = await GetCurrentDepartmentId();
            var workers = await _userService.GetWorkersByDepartment(departmentId);

            ViewBag.Workers = new SelectList(workers, "UserId", "FullName");
            
            var priorities = Enum.GetValues(typeof(TaskPriority))
                .Cast<TaskPriority>()
                .Select(p => new { Value = (int)p, Text = p.ToString() })
                .ToList();
            
            ViewBag.Priorities = new SelectList(priorities, "Value", "Text");
        }
    }
}