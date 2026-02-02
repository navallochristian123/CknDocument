using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;

namespace CKNDocument.Controllers.SuperAdmin;

/// <summary>
/// Invoice management controller for SuperAdmin
/// Manages billing invoices for law firms
/// </summary>
[Authorize(Policy = "SuperAdminOnly")]
public class InvoiceController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public InvoiceController(LawFirmDMSDbContext context)
    {
        _context = context;
    }

    public IActionResult Index()
    {
        return View("~/Views/SuperAdmin/Invoices.cshtml");
    }

    public IActionResult Create()
    {
        return View("~/Views/SuperAdmin/Invoices.cshtml");
    }

    public IActionResult Edit(int id)
    {
        return View("~/Views/SuperAdmin/Invoices.cshtml");
    }

    public IActionResult Details(int id)
    {
        return View("~/Views/SuperAdmin/Invoices.cshtml");
    }

    public IActionResult Delete(int id)
    {
        return View("~/Views/SuperAdmin/Invoices.cshtml");
    }
}
