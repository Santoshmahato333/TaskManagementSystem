// using System.Diagnostics;
// using Microsoft.AspNetCore.Mvc;
// using TaskManagementSystem.Models;

// namespace TaskManagementSystem.Controllers;

// public class HomeController : Controller
// {
//     private readonly ILogger<HomeController> _logger;

//     public HomeController(ILogger<HomeController> logger)
//     {
//         _logger = logger;
//     }

//     public IActionResult Index()
//     {
//         return View();
//     }

//     public IActionResult Privacy()
//     {
//         return View();
//     }

//     [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
//     public IActionResult Error()
//     {
//         return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
//     }
// }
using Microsoft.AspNetCore.Mvc;

namespace TaskManagementSystem.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            if (User.Identity?.IsAuthenticated ?? false)
            {
                var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

                if (role == "Worker")
                {
                    return RedirectToAction("Dashboard", "Worker");
                }
                else if (role != null && role.Contains("Admin"))
                {
                    return RedirectToAction("Dashboard", "Admin");
                }
            }

            return RedirectToAction("Login", "Account");
        }

        public IActionResult Error()
        {
            return View();
        }
    }
}