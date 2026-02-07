using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;
using CKNDocument.Services;
using System.Security.Claims;

namespace CKNDocument.Controllers.LawFirm;

/// <summary>
/// Billing & Payment controller for Law Firm Admins
/// Handles subscription payments, invoice viewing, payment history
/// Integrates with PayMongo for online payment processing
/// </summary>
[Authorize(Policy = "AdminOnly")]
public class BillingController : Controller
{
    private readonly LawFirmDMSDbContext _context;
    private readonly PayMongoService _payMongoService;
    private readonly ILogger<BillingController> _logger;

    public BillingController(
        LawFirmDMSDbContext context,
        PayMongoService payMongoService,
        ILogger<BillingController> logger)
    {
        _context = context;
        _payMongoService = payMongoService;
        _logger = logger;
    }

    private int GetCurrentFirmId()
    {
        var firmIdClaim = User.FindFirst("FirmID")?.Value;
        return firmIdClaim != null ? int.Parse(firmIdClaim) : 0;
    }

    /// <summary>
    /// Main billing page - shows current subscription, due date, payment history
    /// </summary>
    public async Task<IActionResult> Index()
    {
        var firmId = GetCurrentFirmId();
        var subscription = await _context.FirmSubscriptions
            .Include(s => s.Firm)
            .Include(s => s.Invoices)
                .ThenInclude(i => i.InvoiceItems)
            .Include(s => s.Payments)
            .Where(s => s.FirmID == firmId && s.Status == "Active")
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

        ViewBag.Subscription = subscription;
        ViewBag.FirmId = firmId;

        // Get payment history
        var payments = await _context.Payments
            .Include(p => p.Invoice)
            .Include(p => p.Subscription)
            .Where(p => p.Subscription != null && p.Subscription.FirmID == firmId)
            .OrderByDescending(p => p.PaymentDate)
            .Take(20)
            .ToListAsync();

        ViewBag.Payments = payments;

        // Get pending/upcoming invoices
        var pendingInvoices = await _context.Invoices
            .Include(i => i.InvoiceItems)
            .Include(i => i.Subscription)
            .Where(i => i.Subscription != null && i.Subscription.FirmID == firmId
                && (i.Status == "Pending" || i.Status == "Overdue"))
            .OrderBy(i => i.DueDate)
            .ToListAsync();

        ViewBag.PendingInvoices = pendingInvoices;

        return View("~/Views/Admin/Billing.cshtml");
    }

    /// <summary>
    /// View invoice details with breakdown
    /// </summary>
    public async Task<IActionResult> Invoice(int id)
    {
        var firmId = GetCurrentFirmId();
        var invoice = await _context.Invoices
            .Include(i => i.InvoiceItems)
            .Include(i => i.Subscription)
                .ThenInclude(s => s!.Firm)
            .Include(i => i.Payments)
            .Where(i => i.InvoiceID == id && i.Subscription != null && i.Subscription.FirmID == firmId)
            .FirstOrDefaultAsync();

        if (invoice == null)
            return NotFound();

        return View("~/Views/Admin/Invoice.cshtml", invoice);
    }

    /// <summary>
    /// Initiate payment via PayMongo checkout
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(int invoiceId)
    {
        var firmId = GetCurrentFirmId();
        var invoice = await _context.Invoices
            .Include(i => i.InvoiceItems)
            .Include(i => i.Subscription)
                .ThenInclude(s => s!.Firm)
            .Where(i => i.InvoiceID == invoiceId && i.Subscription != null && i.Subscription.FirmID == firmId)
            .FirstOrDefaultAsync();

        if (invoice == null)
            return NotFound();

        if (invoice.Status == "Paid")
            return RedirectToAction("Invoice", new { id = invoiceId });

        var amountToPay = (invoice.TotalAmount ?? 0) - (invoice.PaidAmount ?? 0);
        if (amountToPay <= 0)
            return RedirectToAction("Invoice", new { id = invoiceId });

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var successUrl = $"{baseUrl}/LawFirm/Billing/PaymentSuccess?invoiceId={invoiceId}&session_id={{CHECKOUT_SESSION_ID}}";
        var cancelUrl = $"{baseUrl}/LawFirm/Billing/PaymentCancelled?invoiceId={invoiceId}";

        var result = await _payMongoService.CreateCheckoutSession(
            amountToPay,
            $"CKN Document Subscription - Invoice #{invoice.InvoiceNumber}",
            invoice.InvoiceNumber ?? $"INV-{invoice.InvoiceID}",
            invoice.Subscription?.Firm?.FirmName ?? "Law Firm",
            successUrl,
            cancelUrl
        );

        if (!result.Success)
        {
            TempData["Error"] = $"Payment initialization failed: {result.ErrorMessage}";
            return RedirectToAction("Invoice", new { id = invoiceId });
        }

        // Create a pending payment record
        var payment = new Payment
        {
            SubscriptionID = invoice.SubscriptionID,
            InvoiceID = invoice.InvoiceID,
            Amount = amountToPay,
            TaxAmount = Math.Round(amountToPay * 0.12m / 1.12m, 2), // 12% VAT inclusive
            NetAmount = Math.Round(amountToPay / 1.12m, 2),
            PaymentMethod = "PayMongo",
            PaymentDate = DateTime.Today,
            Status = "Pending",
            PayMongoCheckoutSessionId = result.CheckoutSessionId,
            PayMongoCheckoutUrl = result.CheckoutUrl,
            PayMongoPaymentIntentId = result.PaymentIntentId,
            PaymentReference = $"PM-{DateTime.Now:yyyyMMddHHmmss}",
            CreatedAt = DateTime.Now
        };

        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();

        // Redirect to PayMongo checkout
        return Redirect(result.CheckoutUrl!);
    }

    /// <summary>
    /// PayMongo success callback - verify payment and update records
    /// </summary>
    public async Task<IActionResult> PaymentSuccess(int invoiceId, string? session_id)
    {
        var firmId = GetCurrentFirmId();

        if (string.IsNullOrEmpty(session_id))
        {
            TempData["Error"] = "Invalid payment session.";
            return RedirectToAction("Invoice", new { id = invoiceId });
        }

        // Verify payment with PayMongo
        var status = await _payMongoService.GetCheckoutSessionStatus(session_id);

        var payment = await _context.Payments
            .Include(p => p.Invoice)
            .Where(p => p.PayMongoCheckoutSessionId == session_id)
            .FirstOrDefaultAsync();

        if (payment == null)
        {
            TempData["Error"] = "Payment record not found.";
            return RedirectToAction("Invoice", new { id = invoiceId });
        }

        if (status.Status == "paid")
        {
            payment.Status = "Completed";
            payment.PayMongoPaymentId = status.PaymentId;
            payment.PayMongoStatus = "paid";
            payment.PaymentMethod = status.PaymentMethod ?? "PayMongo";
            payment.UpdatedAt = DateTime.Now;

            // Update invoice
            var invoice = await _context.Invoices.FindAsync(invoiceId);
            if (invoice != null)
            {
                invoice.PaidAmount = (invoice.PaidAmount ?? 0) + payment.Amount;
                if (invoice.PaidAmount >= invoice.TotalAmount)
                {
                    invoice.Status = "Paid";
                }
                invoice.UpdatedAt = DateTime.Now;
            }

            // Create revenue record
            var revenue = new Revenue
            {
                SubscriptionID = payment.SubscriptionID,
                PaymentID = payment.PaymentID,
                Source = "Subscription",
                GrossAmount = payment.Amount,
                TaxAmount = payment.TaxAmount,
                NetAmount = payment.NetAmount,
                TaxRate = 12.00m,
                Amount = payment.Amount,
                RevenueDate = DateTime.Today,
                Description = $"Payment for Invoice #{invoice?.InvoiceNumber}",
                Category = "Monthly",
                CreatedAt = DateTime.Now
            };

            _context.Revenues.Add(revenue);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Payment completed successfully! Thank you.";
        }
        else
        {
            payment.PayMongoStatus = status.Status;
            payment.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            TempData["Warning"] = "Payment is still being processed. We'll update your records once confirmed.";
        }

        return RedirectToAction("Invoice", new { id = invoiceId });
    }

    /// <summary>
    /// PayMongo cancel callback
    /// </summary>
    public async Task<IActionResult> PaymentCancelled(int invoiceId)
    {
        var payment = await _context.Payments
            .Where(p => p.InvoiceID == invoiceId && p.Status == "Pending")
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        if (payment != null)
        {
            payment.Status = "Cancelled";
            payment.PayMongoStatus = "cancelled";
            payment.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();
        }

        TempData["Warning"] = "Payment was cancelled. You can try again anytime.";
        return RedirectToAction("Invoice", new { id = invoiceId });
    }

    /// <summary>
    /// Payment history page
    /// </summary>
    public async Task<IActionResult> History()
    {
        var firmId = GetCurrentFirmId();
        var payments = await _context.Payments
            .Include(p => p.Invoice)
            .Include(p => p.Subscription)
                .ThenInclude(s => s!.Firm)
            .Where(p => p.Subscription != null && p.Subscription.FirmID == firmId)
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync();

        return View("~/Views/Admin/PaymentHistory.cshtml", payments);
    }

    /// <summary>
    /// API: Check payment status (called via AJAX from frontend)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> CheckPaymentStatus(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return Json(new { status = "error", message = "No session ID" });

        var status = await _payMongoService.GetCheckoutSessionStatus(sessionId);
        return Json(new { status = status.Status, paymentId = status.PaymentId });
    }

    /// <summary>
    /// API: Get billing summary data for dashboard widgets
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetBillingSummary()
    {
        var firmId = GetCurrentFirmId();

        var subscription = await _context.FirmSubscriptions
            .Include(s => s.Firm)
            .Where(s => s.FirmID == firmId && s.Status == "Active")
            .FirstOrDefaultAsync();

        var totalPaid = await _context.Payments
            .Where(p => p.Subscription != null && p.Subscription.FirmID == firmId && p.Status == "Completed")
            .SumAsync(p => p.Amount ?? 0);

        var pendingAmount = await _context.Invoices
            .Where(i => i.Subscription != null && i.Subscription.FirmID == firmId
                && (i.Status == "Pending" || i.Status == "Overdue"))
            .SumAsync(i => (i.TotalAmount ?? 0) - (i.PaidAmount ?? 0));

        var nextDueDate = await _context.Invoices
            .Where(i => i.Subscription != null && i.Subscription.FirmID == firmId
                && (i.Status == "Pending" || i.Status == "Overdue"))
            .OrderBy(i => i.DueDate)
            .Select(i => i.DueDate)
            .FirstOrDefaultAsync();

        return Json(new
        {
            planType = subscription?.PlanType ?? "N/A",
            status = subscription?.Status ?? "N/A",
            totalPaid,
            pendingAmount,
            nextDueDate = nextDueDate?.ToString("MMM dd, yyyy") ?? "No pending dues"
        });
    }

    /// <summary>
    /// Download invoice as printable HTML
    /// </summary>
    public async Task<IActionResult> DownloadInvoice(int id)
    {
        var firmId = GetCurrentFirmId();
        var invoice = await _context.Invoices
            .Include(i => i.InvoiceItems)
            .Include(i => i.Subscription)
                .ThenInclude(s => s!.Firm)
            .Include(i => i.Payments)
            .Where(i => i.InvoiceID == id && i.Subscription != null && i.Subscription.FirmID == firmId)
            .FirstOrDefaultAsync();

        if (invoice == null) return NotFound();

        return View("~/Views/Admin/InvoicePrint.cshtml", invoice);
    }
}
