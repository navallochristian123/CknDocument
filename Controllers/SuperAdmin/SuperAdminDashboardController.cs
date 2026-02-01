using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;

namespace CKNDocument.Controllers.SuperAdmin;

/// <summary>
/// Dashboard controller for SuperAdmin
/// Displays platform analytics, law firms overview, revenue/expenses summary
/// </summary>
[Authorize(Policy = "SuperAdminOnly")]
public class SuperAdminDashboardController : Controller
{
    private readonly OwnerERPDbContext _context;

    public SuperAdminDashboardController(OwnerERPDbContext context)
    {
        _context = context;
    }

    public IActionResult Index()
    {
        return View("~/Views/SuperAdmin/Dashboard.cshtml");
    }
}
