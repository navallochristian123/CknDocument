using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;

namespace CKNDocument.Controllers.SuperAdmin;

/// <summary>
/// Revenue management controller for SuperAdmin
/// Tracks platform revenue from all sources with tax separation
/// </summary>
[Authorize(Policy = "SuperAdminOnly")]
public class RevenueController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public RevenueController(LawFirmDMSDbContext context)
    {
        _context = context;
    }

    public IActionResult Index()
    {
        return View("~/Views/SuperAdmin/Revenue.cshtml");
    }

    [HttpGet]
    public async Task<IActionResult> GetRevenueStats()
    {
        var now = DateTime.Now;
        var startOfMonth = new DateTime(now.Year, now.Month, 1);
        var startOfYear = new DateTime(now.Year, 1, 1);
        var startOfLastMonth = startOfMonth.AddMonths(-1);

        var thisMonthGross = await _context.Revenues.Where(r => r.RevenueDate >= startOfMonth).SumAsync(r => r.GrossAmount ?? r.Amount ?? 0);
        var thisMonthTax = await _context.Revenues.Where(r => r.RevenueDate >= startOfMonth).SumAsync(r => r.TaxAmount ?? 0);
        var thisYearGross = await _context.Revenues.Where(r => r.RevenueDate >= startOfYear).SumAsync(r => r.GrossAmount ?? r.Amount ?? 0);
        var thisYearTax = await _context.Revenues.Where(r => r.RevenueDate >= startOfYear).SumAsync(r => r.TaxAmount ?? 0);
        var lastMonthGross = await _context.Revenues.Where(r => r.RevenueDate >= startOfLastMonth && r.RevenueDate < startOfMonth).SumAsync(r => r.GrossAmount ?? r.Amount ?? 0);
        var monthCount = now.Month;
        var avgMonthly = monthCount > 0 ? thisYearGross / monthCount : 0;

        return Json(new
        {
            thisMonth = new { gross = thisMonthGross, tax = thisMonthTax, net = thisMonthGross - thisMonthTax },
            thisYear = new { gross = thisYearGross, tax = thisYearTax, net = thisYearGross - thisYearTax },
            lastMonth = new { gross = lastMonthGross },
            avgMonthly
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetRevenueList(string period = "month", string sortBy = "date")
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

        var query = _context.Revenues
            .Include(r => r.Subscription).ThenInclude(s => s!.Firm)
            .Where(r => r.RevenueDate >= startDate);

        query = sortBy switch
        {
            "amount" => query.OrderByDescending(r => r.GrossAmount ?? r.Amount ?? 0),
            "firm" => query.OrderBy(r => r.Subscription != null && r.Subscription.Firm != null ? r.Subscription.Firm.FirmName : ""),
            _ => query.OrderByDescending(r => r.RevenueDate)
        };

        var revenues = await query.Take(100).Select(r => new
        {
            r.RevenueID,
            date = r.RevenueDate,
            source = r.Source ?? "N/A",
            category = r.Category ?? "N/A",
            firmName = r.Subscription != null && r.Subscription.Firm != null ? r.Subscription.Firm.FirmName : "N/A",
            grossAmount = r.GrossAmount ?? r.Amount ?? 0,
            taxAmount = r.TaxAmount ?? 0,
            netAmount = (r.GrossAmount ?? r.Amount ?? 0) - (r.TaxAmount ?? 0),
            description = r.Description
        }).ToListAsync();

        return Json(revenues);
    }
}
