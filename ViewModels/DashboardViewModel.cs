namespace TaskManagementSystem.ViewModels
{
    public class DashboardViewModel
    {
        public int TotalTasks { get; set; }
        public int PendingTasks { get; set; }
        public int InProgressTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int ActiveWorkers { get; set; }
        public int CriticalTasks { get; set; }
        public int OverdueTasks { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public string UserFullName { get; set; } = string.Empty;
        public string UserRole { get; set; } = string.Empty;
    }
}