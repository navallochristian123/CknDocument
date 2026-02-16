using System.Diagnostics;
using CKNDocument.Models;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CKNDocument.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            // If user is already authenticated, redirect to their dashboard
            if (User.Identity?.IsAuthenticated == true)
            {
                var role = User.FindFirst(ClaimTypes.Role)?.Value;
                return role switch
                {
                    "SuperAdmin" => RedirectToAction("Index", "SuperAdminDashboard"),
                    "Admin" => RedirectToAction("Index", "Dashboard"),
                    "Lawyer" => RedirectToAction("Index", "Dashboard"),
                    "Staff" => RedirectToAction("Index", "Dashboard"),
                    "Client" => RedirectToAction("Index", "Dashboard"),
                    "Auditor" => RedirectToAction("Index", "Dashboard"),
                    _ => View()
                };
            }
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
