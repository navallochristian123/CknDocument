using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;

namespace CKNDocument.Controllers.SuperAdmin;

/// <summary>
/// Expense management controller for SuperAdmin
/// Tracks platform operating expenses
/// </summary>
[Authorize(Policy = "SuperAdminOnly")]
public class ExpenseController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public ExpenseController(LawFirmDMSDbContext context)
    {
        _context = context;
    }

    public IActionResult Index()
    {
        return View("~/Views/SuperAdmin/Expenses.cshtml");
    }

    public IActionResult Create()
    {
        return View("~/Views/SuperAdmin/Expenses.cshtml");
    }

    public IActionResult Edit(int id)
    {
        return View("~/Views/SuperAdmin/Expenses.cshtml");
    }

    public IActionResult Details(int id)
    {
        return View("~/Views/SuperAdmin/Expenses.cshtml");
    }

    public IActionResult Delete(int id)
    {
        return View("~/Views/SuperAdmin/Expenses.cshtml");
    }
}
