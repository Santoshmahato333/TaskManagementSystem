using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TaskManagementSystem.Data;
using TaskManagementSystem.Models;

namespace TaskManagementSystem.Services
{
    public interface IUserService
    {
        Task<int?> GetUserDepartmentId(int userId);
        Task<string?> GetUserRole(int userId);
        Task<User?> GetUserById(int userId);
        Task<bool> IsAdmin(int userId);
        Task<List<User>> GetWorkersByDepartment(int departmentId);
    }

    public class UserService : IUserService
    {
        private readonly ApplicationDbContext _context;

        public UserService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<int?> GetUserDepartmentId(int userId)
        {
            var userRole = await _context.UserRoles
                .Where(ur => ur.UserId == userId)
                .FirstOrDefaultAsync();

            return userRole?.DepartmentId;
        }

        public async Task<string?> GetUserRole(int userId)
        {
            var userRole = await _context.UserRoles
                .Include(ur => ur.Role)
                .Where(ur => ur.UserId == userId)
                .FirstOrDefaultAsync();

            return userRole?.Role.RoleName;
        }

        public async Task<User?> GetUserById(int userId)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == userId);
        }

        public async Task<bool> IsAdmin(int userId)
        {
            var role = await GetUserRole(userId);
            return role != null && (role.Contains("Admin"));
        }

        public async Task<List<User>> GetWorkersByDepartment(int departmentId)
        {
            var workerRoleId = await _context.Roles
                .Where(r => r.RoleName == "Worker")
                .Select(r => r.RoleId)
                .FirstOrDefaultAsync();

            var workers = await _context.UserRoles
                .Where(ur => ur.DepartmentId == departmentId && ur.RoleId == workerRoleId)
                .Include(ur => ur.User)
                .Select(ur => ur.User)
                .Where(u => u.IsActive)
                .ToListAsync();

            return workers;
        }
    }
}