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

namespace TaskManagementSystem.Controllers
{
    [Authorize(Roles = "SuperAdmin,ITAdmin,SuddenAdmin,FoodBankAdmin")]
    [DepartmentFilter]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IUserService _userService;

        public AdminController(ApplicationDbContext context, IUserService userService)
        {
            _context = context;
            _userService = userService;
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
        public async Task<IActionResult> CreateTask()
        {
            var departmentId = await GetCurrentDepartmentId();
            var workers = await _userService.GetWorkersByDepartment(departmentId);

            ViewBag.Workers = new SelectList(workers, "UserId", "FullName");
            ViewBag.Priorities = new SelectList(Enum.GetValues(typeof(TaskPriority)));

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
            ViewBag.Statuses = new SelectList(Enum.GetValues(typeof(TaskStatus)));
            ViewBag.Priorities = new SelectList(Enum.GetValues(typeof(TaskPriority)));

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
            ViewBag.Priorities = new SelectList(Enum.GetValues(typeof(TaskPriority)), task.Priority);
            ViewBag.Statuses = new SelectList(Enum.GetValues(typeof(TaskStatus)), task.Status);

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

        private async Task LoadCreateTaskDropdowns()
        {
            var departmentId = await GetCurrentDepartmentId();
            var workers = await _userService.GetWorkersByDepartment(departmentId);

            ViewBag.Workers = new SelectList(workers, "UserId", "FullName");
            ViewBag.Priorities = new SelectList(Enum.GetValues(typeof(TaskPriority)));
        }
    }
}