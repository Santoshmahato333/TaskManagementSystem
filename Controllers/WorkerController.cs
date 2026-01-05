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

namespace TaskManagementSystem.Controllers
{
    [Authorize(Roles = "Worker")]
    public class WorkerController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IUserService _userService;

        public WorkerController(ApplicationDbContext context, IUserService userService)
        {
            _context = context;
            _userService = userService;
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.Parse(userIdClaim ?? "0");
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

            var taskUpdate = new TaskUpdate
            {
                TaskId = task.TaskId,
                UpdatedByUserId = userId,
                UpdateDescription = model.UpdateDescription,
                OldStatus = task.Status,
                NewStatus = model.NewStatus,
                UpdatedDate = DateTime.Now
            };

            task.Status = model.NewStatus;
            task.UpdatedDate = DateTime.Now;

            _context.TaskUpdates.Add(taskUpdate);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Task status updated successfully!";
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