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
/// Handles subscription payments via PayMongo Sources API (GCash, GrabPay)
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
    /// Main billing page
    /// </summary>
    public async Task<IActionResult> Index()
    {
        ViewBag.PayMongoConfigured = _payMongoService.IsConfigured;
        var firmId = GetCurrentFirmId();
        var subscription = await _context.FirmSubscriptions
            .Include(s => s.Firm)
            .Include(s => s.Invoices).ThenInclude(i => i.InvoiceItems)
            .Include(s => s.Payments)
            .Where(s => s.FirmID == firmId && s.Status == "Active")
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

        ViewBag.Subscription = subscription;
        ViewBag.FirmId = firmId;

        var payments = await _context.Payments
            .Include(p => p.Invoice)
            .Include(p => p.Subscription)
            .Where(p => p.Subscription != null && p.Subscription.FirmID == firmId)
            .OrderByDescending(p => p.PaymentDate)
            .Take(20)
            .ToListAsync();

        ViewBag.Payments = payments;

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
    /// View invoice details with payment method selection
    /// </summary>
    public async Task<IActionResult> Invoice(int id)
    {
        var firmId = GetCurrentFirmId();
        var invoice = await _context.Invoices
            .Include(i => i.InvoiceItems)
            .Include(i => i.Subscription).ThenInclude(s => s!.Firm)
            .Include(i => i.Payments)
            .Where(i => i.InvoiceID == id && i.Subscription != null && i.Subscription.FirmID == firmId)
            .FirstOrDefaultAsync();

        if (invoice == null) return NotFound();

        ViewBag.PayMongoConfigured = _payMongoService.IsConfigured;
        ViewBag.PaymentMethods = PayMongoService.SupportedMethods;
        return View("~/Views/Admin/Invoice.cshtml", invoice);
    }

    /// <summary>
    /// Initiate payment via PayMongo with selected payment method
    /// Uses Source API for GCash/GrabPay, Intent API for Card/Maya/BPI/UnionBank
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(int invoiceId, string paymentMethod = "gcash",
        string? cardNumber = null, int? expMonth = null, int? expYear = null, string? cvc = null)
    {
        var firmId = GetCurrentFirmId();
        var invoice = await _context.Invoices
            .Include(i => i.InvoiceItems)
            .Include(i => i.Subscription).ThenInclude(s => s!.Firm)
            .Where(i => i.InvoiceID == invoiceId && i.Subscription != null && i.Subscription.FirmID == firmId)
            .FirstOrDefaultAsync();

        if (invoice == null) return NotFound();

        if (invoice.Status == "Paid")
            return RedirectToAction("Invoice", new { id = invoiceId });

        if (!_payMongoService.IsConfigured)
        {
            TempData["Error"] = "Online payment is currently unavailable. Please contact the administrator.";
            return RedirectToAction("Invoice", new { id = invoiceId });
        }

        var amountToPay = (invoice.TotalAmount ?? 0) - (invoice.PaidAmount ?? 0);
        if (amountToPay <= 0)
            return RedirectToAction("Invoice", new { id = invoiceId });

        // Validate payment method
        if (!PayMongoService.SupportedMethods.ContainsKey(paymentMethod))
        {
            TempData["Error"] = $"Invalid payment method. Please select a valid option.";
            return RedirectToAction("Invoice", new { id = invoiceId });
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var successUrl = $"{baseUrl}/Billing/PaymentSuccess?invoiceId={invoiceId}";
        var failedUrl = $"{baseUrl}/Billing/PaymentFailed?invoiceId={invoiceId}";

        var description = $"CKN Subscription - Invoice #{invoice.InvoiceNumber ?? $"INV-{invoice.InvoiceID}"}";

        PayMongoSourceResult result;
        bool isIntentBased = PayMongoService.IntentBasedMethods.Contains(paymentMethod);

        if (isIntentBased)
        {
            CardDetails? cardDetails = null;
            if (paymentMethod == "card" && !string.IsNullOrEmpty(cardNumber))
            {
                cardDetails = new CardDetails
                {
                    CardNumber = cardNumber.Replace(" ", "").Replace("-", ""),
                    ExpMonth = expMonth ?? 12,
                    ExpYear = expYear ?? 2026,
                    Cvc = cvc ?? "123"
                };
            }
            result = await _payMongoService.CreatePaymentViaIntent(amountToPay, paymentMethod, successUrl, description, cardDetails);
        }
        else
        {
            result = await _payMongoService.CreateSource(amountToPay, paymentMethod, successUrl, failedUrl, description);
        }

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
            TaxAmount = Math.Round(amountToPay * 0.12m / 1.12m, 2),
            NetAmount = Math.Round(amountToPay / 1.12m, 2),
            PaymentMethod = paymentMethod,
            PaymentDate = DateTime.Today,
            Status = "Pending",
            PayMongoCheckoutSessionId = isIntentBased ? null : result.SourceId,
            PayMongoPaymentIntentId = isIntentBased ? result.SourceId : null,
            PayMongoCheckoutUrl = result.CheckoutUrl,
            PayMongoStatus = result.Status,
            PaymentReference = $"PM-{DateTime.Now:yyyyMMddHHmmss}",
            CreatedAt = DateTime.Now
        };

        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();

        // If payment already succeeded (e.g. no 3DS required), go to success handler
        if (result.Status == "succeeded" && string.IsNullOrEmpty(result.CheckoutUrl))
        {
            return RedirectToAction("PaymentSuccess", new { invoiceId });
        }

        // Redirect to PayMongo authorization page
        if (string.IsNullOrEmpty(result.CheckoutUrl))
        {
            TempData["Error"] = "Payment initialization failed: No redirect URL returned.";
            return RedirectToAction("Invoice", new { id = invoiceId });
        }

        return Redirect(result.CheckoutUrl);
    }

    /// <summary>
    /// PayMongo success callback — verify source/intent &amp; capture payment, extend subscription
    /// </summary>
    public async Task<IActionResult> PaymentSuccess(int invoiceId)
    {
        var firmId = GetCurrentFirmId();

        // Find the pending payment for this invoice (source-based or intent-based)
        var payment = await _context.Payments
            .Include(p => p.Invoice)
            .Include(p => p.Subscription).ThenInclude(s => s!.Firm)
            .Where(p => p.InvoiceID == invoiceId && p.Status == "Pending"
                && (p.PayMongoCheckoutSessionId != null || p.PayMongoPaymentIntentId != null))
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        if (payment == null)
        {
            TempData["Error"] = "Payment record not found.";
            return RedirectToAction("Invoice", new { id = invoiceId });
        }

        // Determine flow type
        bool isIntentBased = !string.IsNullOrEmpty(payment.PayMongoPaymentIntentId);
        bool paymentSucceeded = false;
        string? payMongoPaymentId = null;

        if (isIntentBased)
        {
            // Payment Intent flow — check intent status
            var intentStatus = await _payMongoService.GetPaymentIntentStatus(payment.PayMongoPaymentIntentId!);
            _logger.LogInformation("Billing Intent {Id} status: {Status}", payment.PayMongoPaymentIntentId, intentStatus.Status);

            if (intentStatus.Status == "succeeded")
            {
                paymentSucceeded = true;
                payMongoPaymentId = intentStatus.Type; // PaymentId stored in Type field
            }
            else if (intentStatus.Status == "processing" || intentStatus.Status == "awaiting_next_action")
            {
                payment.PayMongoStatus = intentStatus.Status;
                payment.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();

                TempData["Warning"] = $"Payment is still being processed (status: {intentStatus.Status}). Please wait a moment and refresh.";
                return RedirectToAction("Invoice", new { id = invoiceId });
            }
            else
            {
                payment.PayMongoStatus = intentStatus.Status;
                payment.Status = "Failed";
                payment.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();

                TempData["Error"] = $"Payment failed (status: {intentStatus.Status}). Please try again.";
                return RedirectToAction("Invoice", new { id = invoiceId });
            }
        }
        else
        {
            // Source-based flow — check source status
            var sourceStatus = await _payMongoService.GetSourceStatus(payment.PayMongoCheckoutSessionId!);

            if (sourceStatus.Status == "chargeable")
            {
                var payResult = await _payMongoService.CreatePayment(
                    payment.PayMongoCheckoutSessionId!,
                    payment.Amount ?? 0,
                    $"CKN Subscription - Invoice #{payment.Invoice?.InvoiceNumber}"
                );

                if (payResult.Success)
                {
                    paymentSucceeded = true;
                    payMongoPaymentId = payResult.PaymentId;
                    payment.PaymentMethod = payResult.PaymentMethod ?? payment.PaymentMethod;
                }
                else
                {
                    payment.PayMongoStatus = "charge_failed";
                    payment.Status = "Failed";
                    payment.Notes = payResult.ErrorMessage;
                    payment.UpdatedAt = DateTime.Now;
                    await _context.SaveChangesAsync();

                    TempData["Error"] = $"Payment capture failed: {payResult.ErrorMessage}";
                    return RedirectToAction("Invoice", new { id = invoiceId });
                }
            }
            else if (sourceStatus.Status == "paid")
            {
                paymentSucceeded = true;
            }
            else
            {
                payment.PayMongoStatus = sourceStatus.Status;
                payment.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();

                TempData["Warning"] = $"Payment is still being processed (status: {sourceStatus.Status}). We'll update your records once confirmed.";
                return RedirectToAction("Invoice", new { id = invoiceId });
            }
        }

        if (paymentSucceeded)
        {
            payment.Status = "Completed";
            payment.PayMongoPaymentId = payMongoPaymentId;
            payment.PayMongoStatus = "paid";
            payment.UpdatedAt = DateTime.Now;

            // Update invoice
            var invoice = await _context.Invoices.FindAsync(invoiceId);
            if (invoice != null)
            {
                invoice.PaidAmount = (invoice.PaidAmount ?? 0) + payment.Amount;
                if (invoice.PaidAmount >= invoice.TotalAmount)
                    invoice.Status = "Paid";
                invoice.UpdatedAt = DateTime.Now;
            }

            // Extend subscription
            var subscription = payment.Subscription;
            if (subscription != null)
            {
                var monthlyPrice = subscription.PlanType switch
                {
                    "Starter" => 1499m,
                    "Professional" => 3499m,
                    "Enterprise" => 7999m,
                    _ => 1499m
                };
                var months = (int)Math.Max(1, Math.Round((payment.Amount ?? 0) / monthlyPrice));

                if (subscription.Status == "Expired")
                {
                    subscription.Status = "Active";
                    subscription.StartDate = DateTime.UtcNow;
                    subscription.EndDate = DateTime.UtcNow.AddMonths(months);
                }
                else if (subscription.Status == "Active")
                {
                    subscription.EndDate = (subscription.EndDate ?? DateTime.UtcNow).AddMonths(months);
                }
                subscription.UpdatedAt = DateTime.Now;

                // Reactivate firm if expired
                var firm = subscription.Firm;
                if (firm != null && firm.Status == "Expired")
                {
                    firm.Status = "Active";
                    firm.UpdatedAt = DateTime.Now;
                }

                // Reactivate expired users
                var expiredUsers = await _context.Users
                    .Where(u => u.FirmID == subscription.FirmID && u.Status == "Expired")
                    .ToListAsync();
                foreach (var user in expiredUsers)
                {
                    user.Status = "Active";
                    user.UpdatedAt = DateTime.Now;
                }
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

            // Redirect to receipt page
            return RedirectToAction("Receipt", new { paymentId = payment.PaymentID });
        }

        TempData["Error"] = "Payment could not be completed. Please try again.";
        return RedirectToAction("Invoice", new { id = invoiceId });
    }

    /// <summary>
    /// PayMongo failed callback
    /// </summary>
    public async Task<IActionResult> PaymentFailed(int invoiceId)
    {
        var payment = await _context.Payments
            .Where(p => p.InvoiceID == invoiceId && p.Status == "Pending")
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        if (payment != null)
        {
            payment.Status = "Failed";
            payment.PayMongoStatus = "failed";
            payment.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();
        }

        TempData["Error"] = "Payment failed or was cancelled. You can try again anytime.";
        return RedirectToAction("Invoice", new { id = invoiceId });
    }

    /// <summary>
    /// Payment receipt page — shown after successful payment
    /// </summary>
    public async Task<IActionResult> Receipt(int paymentId)
    {
        var firmId = GetCurrentFirmId();
        var payment = await _context.Payments
            .Include(p => p.Invoice).ThenInclude(i => i!.InvoiceItems)
            .Include(p => p.Invoice).ThenInclude(i => i!.Subscription).ThenInclude(s => s!.Firm)
            .Include(p => p.Subscription).ThenInclude(s => s!.Firm)
            .Where(p => p.PaymentID == paymentId && p.Subscription != null && p.Subscription.FirmID == firmId)
            .FirstOrDefaultAsync();

        if (payment == null) return NotFound();

        return View("~/Views/Admin/Receipt.cshtml", payment);
    }

    /// <summary>
    /// Payment history page
    /// </summary>
    public async Task<IActionResult> History()
    {
        var firmId = GetCurrentFirmId();
        var payments = await _context.Payments
            .Include(p => p.Invoice)
            .Include(p => p.Subscription).ThenInclude(s => s!.Firm)
            .Where(p => p.Subscription != null && p.Subscription.FirmID == firmId)
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync();

        return View("~/Views/Admin/PaymentHistory.cshtml", payments);
    }

    /// <summary>
    /// API: Check payment status
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> CheckPaymentStatus(string sourceId)
    {
        if (string.IsNullOrEmpty(sourceId))
            return Json(new { status = "error", message = "No source ID" });

        var status = await _payMongoService.GetSourceStatus(sourceId);
        return Json(new { status = status.Status });
    }

    /// <summary>
    /// API: Get billing summary data
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
            .Include(i => i.Subscription).ThenInclude(s => s!.Firm)
            .Include(i => i.Payments)
            .Where(i => i.InvoiceID == id && i.Subscription != null && i.Subscription.FirmID == firmId)
            .FirstOrDefaultAsync();

        if (invoice == null) return NotFound();

        return View("~/Views/Admin/InvoicePrint.cshtml", invoice);
    }

    /// <summary>
    /// Submit a manual/advance renewal payment for SuperAdmin approval
    /// Used when the law firm pays via bank transfer, cash, or other offline methods
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitRenewalPayment(string paymentMethod, string? referenceNumber, string? notes, int months = 1)
    {
        var firmId = GetCurrentFirmId();
        var subscription = await _context.FirmSubscriptions
            .Include(s => s.Firm)
            .Where(s => s.FirmID == firmId && (s.Status == "Active" || s.Status == "Expired"))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

        if (subscription == null)
        {
            TempData["Error"] = "No subscription found.";
            return RedirectToAction("Index");
        }

        if (months < 1 || months > 12)
            months = 1;

        var monthlyPrice = subscription.PlanType switch
        {
            "Starter" => 1499m,
            "Professional" => 3499m,
            "Enterprise" => 7999m,
            _ => 1499m
        };

        var totalAmount = monthlyPrice * months;

        // Create a pending invoice
        var invoice = new Invoice
        {
            SubscriptionID = subscription.SubscriptionID,
            InvoiceNumber = $"INV-{DateTime.Now:yyyyMMdd}-{subscription.SubscriptionID:D4}-M",
            InvoiceDate = DateTime.Today,
            DueDate = DateTime.Today,
            TotalAmount = totalAmount,
            PaidAmount = 0,
            Status = "Pending",
            Notes = $"Manual renewal payment ({months} month{(months > 1 ? "s" : "")}) for {subscription.PlanType} plan - Awaiting SuperAdmin confirmation",
            CreatedAt = DateTime.Now
        };
        _context.Invoices.Add(invoice);
        await _context.SaveChangesAsync();

        // Add invoice line item
        var invoiceItem = new InvoiceItem
        {
            InvoiceID = invoice.InvoiceID,
            Description = $"{subscription.PlanType} Plan - {months} Month{(months > 1 ? "s" : "")} Renewal",
            Quantity = months,
            UnitPrice = monthlyPrice,
            SubTotal = totalAmount
        };
        _context.InvoiceItems.Add(invoiceItem);
        await _context.SaveChangesAsync();

        // Create the payment record with "PendingApproval" status
        var payment = new Payment
        {
            SubscriptionID = subscription.SubscriptionID,
            InvoiceID = invoice.InvoiceID,
            Amount = totalAmount,
            TaxAmount = Math.Round(totalAmount * 0.12m / 1.12m, 2),
            NetAmount = Math.Round(totalAmount / 1.12m, 2),
            PaymentMethod = paymentMethod,
            PaymentDate = DateTime.Today,
            Status = "PendingApproval",
            PaymentReference = !string.IsNullOrEmpty(referenceNumber) ? referenceNumber : $"MAN-{DateTime.Now:yyyyMMddHHmmss}",
            Notes = $"Manual {months}-month renewal. {(string.IsNullOrEmpty(notes) ? "" : "Note: " + notes)}",
            CreatedAt = DateTime.Now
        };

        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();

        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

        TempData["Success"] = $"Renewal payment of ₱{totalAmount:N2} submitted for {months} month(s). Awaiting SuperAdmin confirmation.";
        return RedirectToAction("Index");
    }
}
