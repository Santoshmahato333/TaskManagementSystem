using Microsoft.EntityFrameworkCore;
using TaskManagementSystem.Models;

namespace TaskManagementSystem.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<UserRole> UserRoles { get; set; }
        public DbSet<TaskItem> Tasks { get; set; }
        public DbSet<TaskUpdate> TaskUpdates { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User Configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(e => e.Username).IsUnique();
                entity.HasIndex(e => e.Email).IsUnique();

                entity.HasMany(u => u.AssignedTasks)
                    .WithOne(t => t.AssignedToUser)
                    .HasForeignKey(t => t.AssignedToUserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasMany(u => u.CreatedTasks)
                    .WithOne(t => t.CreatedByUser)
                    .HasForeignKey(t => t.CreatedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // UserRole Configuration
            modelBuilder.Entity<UserRole>(entity =>
            {
                entity.HasOne(ur => ur.User)
                    .WithMany(u => u.UserRoles)
                    .HasForeignKey(ur => ur.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(ur => ur.Role)
                    .WithMany(r => r.UserRoles)
                    .HasForeignKey(ur => ur.RoleId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(ur => ur.Department)
                    .WithMany(d => d.UserRoles)
                    .HasForeignKey(ur => ur.DepartmentId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // TaskItem Configuration
            modelBuilder.Entity<TaskItem>(entity =>
            {
                entity.HasIndex(t => t.DepartmentId);
                entity.HasIndex(t => t.AssignedToUserId);
                entity.HasIndex(t => t.Status);
                entity.HasIndex(t => t.CreatedDate);

                entity.HasOne(t => t.Department)
                    .WithMany(d => d.Tasks)
                    .HasForeignKey(t => t.DepartmentId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // TaskUpdate Configuration
            modelBuilder.Entity<TaskUpdate>(entity =>
            {
                entity.HasOne(tu => tu.Task)
                    .WithMany(t => t.TaskUpdates)
                    .HasForeignKey(tu => tu.TaskId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(tu => tu.UpdatedByUser)
                    .WithMany(u => u.TaskUpdates)
                    .HasForeignKey(tu => tu.UpdatedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Seed Data
            SeedData(modelBuilder);
        }

        private void SeedData(ModelBuilder modelBuilder)
        {
            // Seed Roles
            modelBuilder.Entity<Role>().HasData(
                new Role { RoleId = 1, RoleName = "SuperAdmin", Description = "System Administrator" },
                new Role { RoleId = 2, RoleName = "ITAdmin", Description = "IT Department Administrator" },
                new Role { RoleId = 3, RoleName = "SuddenAdmin", Description = "Sudden Department Administrator" },
                new Role { RoleId = 4, RoleName = "FoodBankAdmin", Description = "Food Bank Administrator" },
                new Role { RoleId = 5, RoleName = "Worker", Description = "Task Worker" }
            );

            // Seed Departments
            modelBuilder.Entity<Department>().HasData(
                new Department { DepartmentId = 1, DepartmentName = "IT", Description = "Information Technology Department", IsActive = true },
                new Department { DepartmentId = 2, DepartmentName = "Sudden", Description = "Sudden Response Department", IsActive = true },
                new Department { DepartmentId = 3, DepartmentName = "FoodBank", Description = "Food Bank Department", IsActive = true }
            );

            // Seed Super Admin User (Password: Admin@123)
            // Note: The hash will be updated by migration, use a temporary placeholder
            modelBuilder.Entity<User>().HasData(
                new User
                {
                    UserId = 1,
                    Username = "superadmin",
                    Email = "admin@taskmanagement.com",
                    PasswordHash = GeneratePasswordHash("Admin@123"), // Admin@123
                    FullName = "Super Administrator",
                    PhoneNumber = "9800000000",
                    IsActive = true,
                    CreatedDate = new DateTime(2026, 1, 5, 8, 0, 0)
                }
            );

            // Assign SuperAdmin Role
            modelBuilder.Entity<UserRole>().HasData(
                new UserRole
                {
                    UserRoleId = 1,
                    UserId = 1,
                    RoleId = 1,
                    DepartmentId = 1,
                    AssignedDate = new DateTime(2026, 1, 5, 8, 0, 0)
                }
            );
        }

        private string GeneratePasswordHash(string password)
        {
            const int SaltSize = 16;
            const int HashSize = 32;
            const int Iterations = 10000;

            byte[] salt = new byte[SaltSize];
            // Use a fixed salt for seeding so it's consistent
            for (int i = 0; i < SaltSize; i++)
                salt[i] = (byte)(i * 7 + 13);

            byte[] hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
                System.Text.Encoding.UTF8.GetBytes(password),
                salt,
                Iterations,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                HashSize
            );

            byte[] hashBytes = new byte[SaltSize + HashSize];
            Array.Copy(salt, 0, hashBytes, 0, SaltSize);
            Array.Copy(hash, 0, hashBytes, SaltSize, HashSize);

            return Convert.ToBase64String(hashBytes);
        }
    }
}