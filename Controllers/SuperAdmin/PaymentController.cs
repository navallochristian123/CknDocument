using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;

namespace CKNDocument.Controllers.SuperAdmin;

/// <summary>
/// Payment management controller for SuperAdmin
/// Tracks payments received from law firms with real data
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

    [HttpGet]
    public async Task<IActionResult> GetPayments(string? status = null, string? sortBy = "date")
    {
        var query = _context.Payments
            .Include(p => p.Subscription).ThenInclude(s => s!.Firm)
            .Include(p => p.Invoice)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status) && status != "all")
            query = query.Where(p => p.Status == status);

        query = sortBy switch
        {
            "amount" => query.OrderByDescending(p => p.Amount),
            "firm" => query.OrderBy(p => p.Subscription != null && p.Subscription.Firm != null ? p.Subscription.Firm.FirmName : ""),
            "status" => query.OrderBy(p => p.Status),
            _ => query.OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.CreatedAt)
        };

        var payments = await query.Take(100).Select(p => new
        {
            p.PaymentID,
            p.PaymentReference,
            firmName = p.Subscription != null && p.Subscription.Firm != null ? p.Subscription.Firm.FirmName : "N/A",
            invoiceNumber = p.Invoice != null ? p.Invoice.InvoiceNumber : "N/A",
            amount = p.Amount ?? 0,
            taxAmount = p.TaxAmount ?? 0,
            netAmount = p.NetAmount ?? 0,
            method = p.PaymentMethod ?? "N/A",
            date = p.PaymentDate,
            status = p.Status ?? "N/A",
            paymongoId = p.PayMongoPaymentId
        }).ToListAsync();

        return Json(payments);
    }

    [HttpGet]
    public async Task<IActionResult> GetPaymentStats()
    {
        var now = DateTime.Now;
        var startOfMonth = new DateTime(now.Year, now.Month, 1);

        var totalCompleted = await _context.Payments.Where(p => p.Status == "Completed").SumAsync(p => p.Amount ?? 0);
        var thisMonthTotal = await _context.Payments.Where(p => p.Status == "Completed" && p.PaymentDate >= startOfMonth).SumAsync(p => p.Amount ?? 0);
        var pendingCount = await _context.Payments.CountAsync(p => p.Status == "Pending");
        var totalCount = await _context.Payments.CountAsync(p => p.Status == "Completed");

        return Json(new { totalCompleted, thisMonthTotal, pendingCount, totalCount });
    }
}
