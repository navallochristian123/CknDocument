using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;

namespace CKNDocument.Controllers.SuperAdmin;

/// <summary>
/// Invoice management controller for SuperAdmin
/// Manages billing invoices for law firms with real data
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

    [HttpGet]
    public async Task<IActionResult> GetInvoices(string? status = null)
    {
        var query = _context.Invoices
            .Include(i => i.Subscription).ThenInclude(s => s!.Firm)
            .Include(i => i.Payments)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status) && status != "all")
            query = query.Where(i => i.Status == status);

        var invoices = await query
            .OrderByDescending(i => i.InvoiceDate)
            .Take(100)
            .Select(i => new
            {
                i.InvoiceID,
                invoiceNumber = i.InvoiceNumber ?? $"INV-{i.InvoiceID:D4}",
                firmName = i.Subscription != null && i.Subscription.Firm != null ? i.Subscription.Firm.FirmName : "N/A",
                totalAmount = i.TotalAmount ?? 0,
                paidAmount = i.PaidAmount ?? 0,
                balance = (i.TotalAmount ?? 0) - (i.PaidAmount ?? 0),
                dueDate = i.DueDate,
                invoiceDate = i.InvoiceDate,
                status = i.Status ?? "Pending",
                paymentCount = i.Payments.Count(p => p.Status == "Completed")
            })
            .ToListAsync();

        return Json(invoices);
    }

    [HttpGet]
    public async Task<IActionResult> GetInvoiceStats()
    {
        var total = await _context.Invoices.CountAsync();
        var paid = await _context.Invoices.CountAsync(i => i.Status == "Paid");
        var pending = await _context.Invoices.CountAsync(i => i.Status == "Pending");
        var overdue = await _context.Invoices.CountAsync(i => i.Status == "Overdue");
        var totalAmount = await _context.Invoices.SumAsync(i => i.TotalAmount ?? 0);
        var totalPaid = await _context.Invoices.SumAsync(i => i.PaidAmount ?? 0);

        return Json(new { total, paid, pending, overdue, totalAmount, totalPaid, outstanding = totalAmount - totalPaid });
    }
}
