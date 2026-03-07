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
    public async Task<IActionResult> GetInvoices(string? status = null, string? period = null)
    {
        var query = _context.Invoices
            .Include(i => i.Subscription).ThenInclude(s => s!.Firm)
            .Include(i => i.Payments)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status) && status != "all")
            query = query.Where(i => i.Status == status);

        // Period filtering
        if (!string.IsNullOrEmpty(period) && period != "all")
        {
            var now = DateTime.Now;
            query = period switch
            {
                "week" => query.Where(i => i.InvoiceDate >= now.AddDays(-7)),
                "month" => query.Where(i => i.InvoiceDate >= new DateTime(now.Year, now.Month, 1)),
                "year" => query.Where(i => i.InvoiceDate >= new DateTime(now.Year, 1, 1)),
                _ => query
            };
        }

        var invoices = await query
            .OrderByDescending(i => i.InvoiceDate)
            .Take(100)
            .Select(i => new
            {
                i.InvoiceID,
                invoiceNumber = i.InvoiceNumber ?? $"INV-{i.InvoiceID:D4}",
                firmName = i.Subscription != null && i.Subscription.Firm != null ? i.Subscription.Firm.FirmName : "N/A",
                firmAddress = i.Subscription != null && i.Subscription.Firm != null ? i.Subscription.Firm.Address : "",
                firmEmail = i.Subscription != null && i.Subscription.Firm != null ? i.Subscription.Firm.ContactEmail : "",
                subscriptionPlan = i.Subscription != null ? i.Subscription.PlanType : "N/A",
                totalAmount = i.TotalAmount ?? 0,
                paidAmount = i.PaidAmount ?? 0,
                balance = (i.TotalAmount ?? 0) - (i.PaidAmount ?? 0),
                dueDate = i.DueDate,
                invoiceDate = i.InvoiceDate,
                status = i.Status ?? "Pending",
                paymentCount = i.Payments.Count(p => p.Status == "Completed"),
                notes = i.Notes
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

    /// <summary>
    /// Get detailed receipt data for a single invoice
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetInvoiceReceipt(int id)
    {
        var invoice = await _context.Invoices
            .Include(i => i.Subscription).ThenInclude(s => s!.Firm)
            .Include(i => i.InvoiceItems)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.InvoiceID == id);

        if (invoice == null)
            return Json(new { success = false, message = "Invoice not found" });

        var firm = invoice.Subscription?.Firm;
        var payments = invoice.Payments
            .Where(p => p.Status == "Completed")
            .Select(p => new
            {
                p.PaymentReference,
                amount = p.Amount ?? 0,
                taxAmount = p.TaxAmount ?? 0,
                netAmount = p.NetAmount ?? 0,
                method = p.PaymentMethod ?? "N/A",
                date = p.PaymentDate
            }).ToList();

        var items = invoice.InvoiceItems.Select(ii => new
        {
            ii.Description,
            ii.Quantity,
            unitPrice = ii.UnitPrice ?? 0,
            subTotal = ii.SubTotal ?? 0
        }).ToList();

        return Json(new
        {
            success = true,
            invoiceNumber = invoice.InvoiceNumber ?? $"INV-{invoice.InvoiceID:D4}",
            firmName = firm?.FirmName ?? "N/A",
            firmAddress = firm?.Address ?? "",
            firmEmail = firm?.ContactEmail ?? "",
            subscriptionPlan = invoice.Subscription?.PlanType ?? "N/A",
            invoiceDate = invoice.InvoiceDate,
            dueDate = invoice.DueDate,
            totalAmount = invoice.TotalAmount ?? 0,
            paidAmount = invoice.PaidAmount ?? 0,
            balance = (invoice.TotalAmount ?? 0) - (invoice.PaidAmount ?? 0),
            status = invoice.Status ?? "Pending",
            notes = invoice.Notes,
            items,
            payments
        });
    }
}
