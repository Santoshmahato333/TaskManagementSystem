using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TaskManagementSystem.Models
{
    public class TaskUpdate
    {
        [Key]
        public int UpdateId { get; set; }

        [Required]
        public int TaskId { get; set; }

        [Required]
        public int UpdatedByUserId { get; set; }

        [StringLength(1000)]
        public string? UpdateDescription { get; set; }

        public TaskStatus? OldStatus { get; set; }

        public TaskStatus? NewStatus { get; set; }

        public DateTime UpdatedDate { get; set; } = DateTime.Now;

        // Navigation Properties
        [ForeignKey("TaskId")]
        public virtual TaskItem Task { get; set; } = null!;

        [ForeignKey("UpdatedByUserId")]
        public virtual User UpdatedByUser { get; set; } = null!;
    }
}