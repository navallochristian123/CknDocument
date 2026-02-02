using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;

namespace CKNDocument.Controllers.SuperAdmin;

/// <summary>
/// Subscription management controller for SuperAdmin
/// Manages subscription plans and law firm subscriptions
/// </summary>
[Authorize(Policy = "SuperAdminOnly")]
public class SubscriptionController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public SubscriptionController(LawFirmDMSDbContext context)
    {
        _context = context;
    }

    public IActionResult Index()
    {
        return View("~/Views/SuperAdmin/Subscriptions.cshtml");
    }

    public IActionResult Plans()
    {
        return View("~/Views/SuperAdmin/Subscriptions.cshtml");
    }

    public IActionResult Create()
    {
        return View("~/Views/SuperAdmin/Subscriptions.cshtml");
    }

    public IActionResult Edit(int id)
    {
        return View("~/Views/SuperAdmin/Subscriptions.cshtml");
    }

    public IActionResult Details(int id)
    {
        return View("~/Views/SuperAdmin/Subscriptions.cshtml");
    }
}
