using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TaskManagementSystem.Data;
using TaskManagementSystem.Models;
using TaskManagementSystem.Services;
using TaskManagementSystem.ViewModels;
using TaskStatus = TaskManagementSystem.Models.TaskStatus;
using TaskPriority = TaskManagementSystem.Models.TaskPriority;
using System.IO;

namespace TaskManagementSystem.Controllers
{
    [Authorize(Roles = "Worker")]
    public class WorkerController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IUserService _userService;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public WorkerController(ApplicationDbContext context, IUserService userService, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _userService = userService;
            _webHostEnvironment = webHostEnvironment;
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.Parse(userIdClaim ?? "0");
        }

        private int GetCurrentDepartmentId()
        {
            var departmentIdClaim = User.FindFirst("DepartmentId")?.Value;
            return int.Parse(departmentIdClaim ?? "0");
        }

        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            var userId = GetCurrentUserId();
            var user = await _userService.GetUserById(userId);
            var departmentName = User.FindFirst("DepartmentName")?.Value ?? "Unknown";

            var tasks = await _context.Tasks
                .Where(t => t.AssignedToUserId == userId)
                .ToListAsync();

            var viewModel = new DashboardViewModel
            {
                TotalTasks = tasks.Count,
                PendingTasks = tasks.Count(t => t.Status == TaskStatus.Pending),
                InProgressTasks = tasks.Count(t => t.Status == TaskStatus.InProgress),
                CompletedTasks = tasks.Count(t => t.Status == TaskStatus.Completed),
                OverdueTasks = tasks.Count(t => t.DueDate < DateTime.Now && t.Status != TaskStatus.Completed),
                DepartmentName = departmentName,
                UserFullName = user?.FullName ?? "Unknown",
                UserRole = "Worker"
            };

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> MyTasks(TaskFilterViewModel filter)
        {
            var userId = GetCurrentUserId();

            var query = _context.Tasks
                .Include(t => t.CreatedByUser)
                .Include(t => t.Department)
                .Where(t => t.AssignedToUserId == userId);

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

            var totalRecords = await query.CountAsync();
            var tasks = await query
                .OrderByDescending(t => t.CreatedDate)
                .Skip((filter.PageNumber - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();

            ViewBag.TotalPages = (int)Math.Ceiling(totalRecords / (double)filter.PageSize);
            ViewBag.CurrentPage = filter.PageNumber;
            ViewBag.Filter = filter;
            ViewBag.Statuses = Enum.GetValues(typeof(TaskStatus));
            ViewBag.Priorities = Enum.GetValues(typeof(TaskPriority));

            return View(tasks);
        }

        [HttpGet]
        public IActionResult CreateTask()
        {
            var model = new CreateTaskViewModel
            {
                AssignedToUserId = GetCurrentUserId(),
                Priority = TaskPriority.Medium
            };

            ViewBag.Priorities = Enum.GetValues(typeof(TaskPriority));
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateTask(CreateTaskViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Priorities = Enum.GetValues(typeof(TaskPriority));
                return View(model);
            }

            var userId = GetCurrentUserId();
            var departmentId = GetCurrentDepartmentId();

            var task = new TaskItem
            {
                Title = model.Title,
                Description = model.Description,
                DepartmentId = departmentId,
                AssignedToUserId = userId,
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
                    ViewBag.Priorities = Enum.GetValues(typeof(TaskPriority));
                    return View(model);
                }

                var fileExtension = Path.GetExtension(model.AttachmentFile.FileName).ToLower();
                if (!allowedExtensions.Contains(fileExtension))
                {
                    ModelState.AddModelError("AttachmentFile", "Only PDF, JPG, PNG, and GIF files are allowed.");
                    ViewBag.Priorities = Enum.GetValues(typeof(TaskPriority));
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
                    ViewBag.Priorities = Enum.GetValues(typeof(TaskPriority));
                    return View(model);
                }
            }

            _context.Tasks.Add(task);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Task created successfully!";
            return RedirectToAction("MyTasks");
        }

        [HttpGet]
        public async Task<IActionResult> ViewTaskDetails(int id)
        {
            var userId = GetCurrentUserId();

            var task = await _context.Tasks
                .Include(t => t.CreatedByUser)
                .Include(t => t.Department)
                .Include(t => t.TaskUpdates)
                    .ThenInclude(tu => tu.UpdatedByUser)
                .FirstOrDefaultAsync(t => t.TaskId == id && t.AssignedToUserId == userId);

            if (task == null)
            {
                return NotFound();
            }

            return View(task);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateTaskStatus(TaskUpdateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Invalid data provided.";
                return RedirectToAction("ViewTaskDetails", new { id = model.TaskId });
            }

            var userId = GetCurrentUserId();

            var task = await _context.Tasks
                .FirstOrDefaultAsync(t => t.TaskId == model.TaskId && t.AssignedToUserId == userId);

            if (task == null)
            {
                return NotFound();
            }

            var isCompletionRequest = model.NewStatus == TaskStatus.Completed;
            var previousStatus = task.Status;
            var effectiveStatus = isCompletionRequest ? TaskStatus.InProgress : model.NewStatus;

            var updateDescription = model.UpdateDescription;
            if (isCompletionRequest)
            {
                updateDescription = string.IsNullOrWhiteSpace(updateDescription)
                    ? "Worker marked the task as completed and sent it for admin approval."
                    : $"{updateDescription.Trim()} (Worker marked the task as completed and sent it for admin approval.)";
            }

            var taskUpdate = new TaskUpdate
            {
                TaskId = task.TaskId,
                UpdatedByUserId = userId,
                UpdateDescription = updateDescription,
                OldStatus = previousStatus,
                NewStatus = effectiveStatus,
                UpdatedDate = DateTime.Now
            };

            if (isCompletionRequest)
            {
                taskUpdate.UpdateDescription = string.IsNullOrWhiteSpace(model.UpdateDescription)
                    ? "Worker submitted the task for admin approval."
                    : $"{model.UpdateDescription.Trim()} (Worker submitted the task for admin approval.)";
            }

            task.Status = effectiveStatus;
            task.UpdatedDate = DateTime.Now;

            _context.TaskUpdates.Add(taskUpdate);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = isCompletionRequest
                ? "Task submitted for admin approval."
                : "Task status updated successfully!";
            return RedirectToAction("ViewTaskDetails", new { id = model.TaskId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddTaskUpdate(int taskId, string updateDescription)
        {
            if (string.IsNullOrWhiteSpace(updateDescription))
            {
                TempData["ErrorMessage"] = "Update description is required.";
                return RedirectToAction("ViewTaskDetails", new { id = taskId });
            }

            var userId = GetCurrentUserId();

            var task = await _context.Tasks
                .FirstOrDefaultAsync(t => t.TaskId == taskId && t.AssignedToUserId == userId);

            if (task == null)
            {
                return NotFound();
            }

            var taskUpdate = new TaskUpdate
            {
                TaskId = taskId,
                UpdatedByUserId = userId,
                UpdateDescription = updateDescription,
                UpdatedDate = DateTime.Now
            };

            _context.TaskUpdates.Add(taskUpdate);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Update added successfully!";
            return RedirectToAction("ViewTaskDetails", new { id = taskId });
        }
    }
}