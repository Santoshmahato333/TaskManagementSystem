using TaskManagementSystem.Models;
using TaskStatus = TaskManagementSystem.Models.TaskStatus;
using TaskPriority = TaskManagementSystem.Models.TaskPriority;

namespace TaskManagementSystem.ViewModels
{
    public class TaskFilterViewModel
    {
        public string? SearchTerm { get; set; }
        public TaskStatus? Status { get; set; }
        public TaskPriority? Priority { get; set; }
        public int? AssignedToUserId { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}