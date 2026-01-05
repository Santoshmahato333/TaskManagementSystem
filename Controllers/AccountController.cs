using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TaskManagementSystem.Data;
using TaskManagementSystem.Models;
using TaskManagementSystem.Services;
using TaskManagementSystem.ViewModels;

namespace TaskManagementSystem.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IPasswordService _passwordService;
        private readonly IUserService _userService;

        public AccountController(ApplicationDbContext context, IPasswordService passwordService, IUserService userService)
        {
            _context = context;
            _passwordService = passwordService;
            _userService = userService;
        }

        [HttpGet]
        public IActionResult Login()
        {
            if (User.Identity?.IsAuthenticated ?? false)
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == model.Username && u.IsActive);

            if (user == null || !_passwordService.VerifyPassword(model.Password, user.PasswordHash))
            {
                ModelState.AddModelError(string.Empty, "Invalid username or password.");
                return View(model);
            }

            var userRole = await _context.UserRoles
                .Include(ur => ur.Role)
                .Include(ur => ur.Department)
                .FirstOrDefaultAsync(ur => ur.UserId == user.UserId);

            if (userRole == null)
            {
                ModelState.AddModelError(string.Empty, "User role not assigned.");
                return View(model);
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("FullName", user.FullName),
                new Claim(ClaimTypes.Role, userRole.Role.RoleName),
                new Claim("DepartmentId", userRole.DepartmentId.ToString()),
                new Claim("DepartmentName", userRole.Department.DepartmentName)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            if (userRole.Role.RoleName == "Worker")
            {
                return RedirectToAction("Dashboard", "Worker");
            }
            else
            {
                return RedirectToAction("Dashboard", "Admin");
            }
        }

        [HttpGet]
        [Authorize(Roles = "SuperAdmin,ITAdmin,SuddenAdmin,FoodBankAdmin")]
        public async Task<IActionResult> Register()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var userRole = await _userService.GetUserRole(userId);
            var departmentId = await _userService.GetUserDepartmentId(userId);

            if (userRole == "SuperAdmin")
            {
                ViewBag.Roles = new SelectList(await _context.Roles.ToListAsync(), "RoleId", "RoleName");
                ViewBag.Departments = new SelectList(await _context.Departments.Where(d => d.IsActive).ToListAsync(), "DepartmentId", "DepartmentName");
            }
            else
            {
                ViewBag.Roles = new SelectList(await _context.Roles.Where(r => r.RoleName == "Worker").ToListAsync(), "RoleId", "RoleName");
                ViewBag.Departments = new SelectList(await _context.Departments.Where(d => d.DepartmentId == departmentId).ToListAsync(), "DepartmentId", "DepartmentName");
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "SuperAdmin,ITAdmin,SuddenAdmin,FoodBankAdmin")]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                await LoadRegisterDropdowns(model.DepartmentId);
                return View(model);
            }

            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == model.Username || u.Email == model.Email);

            if (existingUser != null)
            {
                ModelState.AddModelError(string.Empty, "Username or Email already exists.");
                await LoadRegisterDropdowns(model.DepartmentId);
                return View(model);
            }

            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var currentUserRole = await _userService.GetUserRole(userId);
            var currentDepartmentId = await _userService.GetUserDepartmentId(userId);

            if (currentUserRole != "SuperAdmin" && model.DepartmentId != currentDepartmentId)
            {
                ModelState.AddModelError(string.Empty, "You can only create users in your department.");
                await LoadRegisterDropdowns(model.DepartmentId);
                return View(model);
            }

            var user = new User
            {
                Username = model.Username,
                Email = model.Email,
                FullName = model.FullName,
                PhoneNumber = model.PhoneNumber,
                PasswordHash = _passwordService.HashPassword(model.Password),
                IsActive = true,
                CreatedDate = DateTime.Now
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var userRole = new UserRole
            {
                UserId = user.UserId,
                RoleId = model.RoleId,
                DepartmentId = model.DepartmentId,
                AssignedDate = DateTime.Now
            };

            _context.UserRoles.Add(userRole);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "User registered successfully!";
            return RedirectToAction("ManageWorkers", "Admin");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        private async Task LoadRegisterDropdowns(int? selectedDepartmentId = null)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var userRole = await _userService.GetUserRole(userId);
            var departmentId = await _userService.GetUserDepartmentId(userId);

            if (userRole == "SuperAdmin")
            {
                ViewBag.Roles = new SelectList(await _context.Roles.ToListAsync(), "RoleId", "RoleName");
                ViewBag.Departments = new SelectList(await _context.Departments.Where(d => d.IsActive).ToListAsync(), "DepartmentId", "DepartmentName", selectedDepartmentId);
            }
            else
            {
                ViewBag.Roles = new SelectList(await _context.Roles.Where(r => r.RoleName == "Worker").ToListAsync(), "RoleId", "RoleName");
                ViewBag.Departments = new SelectList(await _context.Departments.Where(d => d.DepartmentId == departmentId).ToListAsync(), "DepartmentId", "DepartmentName", selectedDepartmentId);
            }
        }
    }
}