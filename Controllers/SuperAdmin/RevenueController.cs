using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;

namespace CKNDocument.Controllers.SuperAdmin;

/// <summary>
/// Revenue management controller for SuperAdmin
/// Tracks platform revenue from all sources
/// </summary>
[Authorize(Policy = "SuperAdminOnly")]
public class RevenueController : Controller
{
    private readonly OwnerERPDbContext _context;

    public RevenueController(OwnerERPDbContext context)
    {
        _context = context;
    }

    public IActionResult Index()
    {
        return View("~/Views/SuperAdmin/Revenue.cshtml");
    }

    public IActionResult Create()
    {
        return View("~/Views/SuperAdmin/Revenue.cshtml");
    }

    public IActionResult Edit(int id)
    {
        return View("~/Views/SuperAdmin/Revenue.cshtml");
    }

    public IActionResult Details(int id)
    {
        return View("~/Views/SuperAdmin/Revenue.cshtml");
    }

    public IActionResult Delete(int id)
    {
        return View("~/Views/SuperAdmin/Revenue.cshtml");
    }
}
