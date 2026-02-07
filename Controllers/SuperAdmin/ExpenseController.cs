using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;

namespace CKNDocument.Controllers.SuperAdmin;

/// <summary>
/// Expense management controller for SuperAdmin
/// Tracks platform operating expenses with CRUD operations
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

    [HttpGet]
    public async Task<IActionResult> GetExpenseStats()
    {
        var now = DateTime.Now;
        var startOfMonth = new DateTime(now.Year, now.Month, 1);
        var startOfYear = new DateTime(now.Year, 1, 1);

        var thisMonth = await _context.Expenses.Where(e => e.ExpenseDate >= startOfMonth && e.Status != "Rejected").SumAsync(e => e.Amount ?? 0);
        var thisYear = await _context.Expenses.Where(e => e.ExpenseDate >= startOfYear && e.Status != "Rejected").SumAsync(e => e.Amount ?? 0);
        var monthCount = now.Month;
        var avgMonthly = monthCount > 0 ? thisYear / monthCount : 0;

        return Json(new { thisMonth, thisYear, avgMonthly });
    }

    [HttpGet]
    public async Task<IActionResult> GetExpenses(string period = "month", string sortBy = "date")
    {
        var startDate = period switch
        {
            "week" => DateTime.Today.AddDays(-7),
            "month" => new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1),
            "quarter" => new DateTime(DateTime.Now.Year, ((DateTime.Now.Month - 1) / 3) * 3 + 1, 1),
            "year" => new DateTime(DateTime.Now.Year, 1, 1),
            "all" => DateTime.MinValue,
            _ => new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1)
        };

        var query = _context.Expenses.Where(e => e.ExpenseDate >= startDate);

        query = sortBy switch
        {
            "amount" => query.OrderByDescending(e => e.Amount),
            "category" => query.OrderBy(e => e.Category),
            _ => query.OrderByDescending(e => e.ExpenseDate)
        };

        var expenses = await query.Take(100).Select(e => new
        {
            e.ExpenseID,
            date = e.ExpenseDate,
            category = e.Category ?? "Other",
            description = e.Description ?? "N/A",
            amount = e.Amount ?? 0,
            status = e.Status ?? "Pending",
            notes = e.Notes
        }).ToListAsync();

        return Json(expenses);
    }

    [HttpPost]
    public async Task<IActionResult> AddExpense([FromBody] ExpenseInput input)
    {
        if (string.IsNullOrEmpty(input.Description) || input.Amount <= 0)
            return BadRequest(new { message = "Description and positive amount required." });

        var expense = new Expense
        {
            Description = input.Description,
            Amount = input.Amount,
            Category = input.Category ?? "Other",
            ExpenseDate = input.ExpenseDate ?? DateTime.Today,
            Status = "Approved",
            Notes = input.Notes,
            CreatedAt = DateTime.Now
        };

        _context.Expenses.Add(expense);
        await _context.SaveChangesAsync();
        return Json(new { success = true, id = expense.ExpenseID });
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteExpense(int id)
    {
        var expense = await _context.Expenses.FindAsync(id);
        if (expense == null) return NotFound();
        _context.Expenses.Remove(expense);
        await _context.SaveChangesAsync();
        return Json(new { success = true });
    }

    public class ExpenseInput
    {
        public string? Description { get; set; }
        public decimal Amount { get; set; }
        public string? Category { get; set; }
        public DateTime? ExpenseDate { get; set; }
        public string? Notes { get; set; }
    }
}
