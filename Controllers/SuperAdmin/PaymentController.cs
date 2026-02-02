using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;

namespace CKNDocument.Controllers.SuperAdmin;

/// <summary>
/// Payment management controller for SuperAdmin
/// Tracks payments received from law firms
/// </summary>
[Authorize(Policy = "SuperAdminOnly")]
public class PaymentController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public PaymentController(LawFirmDMSDbContext context)
    {
        _context = context;
    }

    public IActionResult Index()
    {
        return View("~/Views/SuperAdmin/Payments.cshtml");
    }

    public IActionResult Create()
    {
        return View("~/Views/SuperAdmin/Payments.cshtml");
    }

    public IActionResult Edit(int id)
    {
        return View("~/Views/SuperAdmin/Payments.cshtml");
    }

    public IActionResult Details(int id)
    {
        return View("~/Views/SuperAdmin/Payments.cshtml");
    }

    public IActionResult Delete(int id)
    {
        return View("~/Views/SuperAdmin/Payments.cshtml");
    }
}
