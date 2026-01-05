using System.ComponentModel.DataAnnotations;
using TaskManagementSystem.Models;
using TaskStatus = TaskManagementSystem.Models.TaskStatus;

namespace TaskManagementSystem.ViewModels
{
    public class TaskUpdateViewModel
    {
        public int TaskId { get; set; }

        [Required]
        [Display(Name = "New Status")]
        public TaskStatus NewStatus { get; set; }

        [StringLength(1000)]
        [Display(Name = "Update Description")]
        public string? UpdateDescription { get; set; }
    }
}