using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TaskManagementSystem.Models
{
    public enum TaskPriority
    {
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
    }

    public enum TaskStatus
    {
        Pending = 1,
        InProgress = 2,
        Completed = 3,
        Cancelled = 4
    }

    public class TaskItem
    {
        [Key]
        public int TaskId { get; set; }

        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Description { get; set; }

        [Required]
        public int DepartmentId { get; set; }

        [Required]
        public int AssignedToUserId { get; set; }

        [Required]
        public int CreatedByUserId { get; set; }

        [Required]
        public TaskPriority Priority { get; set; } = TaskPriority.Medium;

        [Required]
        public TaskStatus Status { get; set; } = TaskStatus.Pending;

        public DateTime? DueDate { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public DateTime? UpdatedDate { get; set; }

        [StringLength(500)]
        public string? AttachmentFileName { get; set; }

        [StringLength(500)]
        public string? AttachmentPath { get; set; }

        [NotMapped]
        public bool ApproveAsCompleted { get; set; }

        // Navigation Properties
        [ForeignKey("DepartmentId")]
        public virtual Department Department { get; set; } = null!;

        [ForeignKey("AssignedToUserId")]
        public virtual User AssignedToUser { get; set; } = null!;

        [ForeignKey("CreatedByUserId")]
        public virtual User CreatedByUser { get; set; } = null!;

        public virtual ICollection<TaskUpdate> TaskUpdates { get; set; } = new List<TaskUpdate>();
    }
}