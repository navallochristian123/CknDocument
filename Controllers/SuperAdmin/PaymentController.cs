using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;

namespace CKNDocument.Controllers.SuperAdmin;

/// <summary>
/// Payment management controller for SuperAdmin
/// Tracks payments received from law firms with real data
/// Supports approving/rejecting manual payments submitted by law firms
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
            paymongoId = p.PayMongoPaymentId,
            notes = p.Notes
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
        var pendingCount = await _context.Payments.CountAsync(p => p.Status == "Pending" || p.Status == "PendingApproval");
        var totalCount = await _context.Payments.CountAsync(p => p.Status == "Completed");
        var pendingApprovalCount = await _context.Payments.CountAsync(p => p.Status == "PendingApproval");

        return Json(new { totalCompleted, thisMonthTotal, pendingCount, totalCount, pendingApprovalCount });
    }

    /// <summary>
    /// Approve a manual payment submitted by a law firm admin
    /// This activates/extends the subscription and marks payment as completed
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ApprovePayment(int paymentId)
    {
        var payment = await _context.Payments
            .Include(p => p.Subscription).ThenInclude(s => s!.Firm)
            .Include(p => p.Invoice)
            .FirstOrDefaultAsync(p => p.PaymentID == paymentId);

        if (payment == null)
            return Json(new { success = false, message = "Payment not found." });

        if (payment.Status != "PendingApproval")
            return Json(new { success = false, message = "Payment is not pending approval." });

        // Mark payment as completed
        payment.Status = "Completed";
        payment.PayMongoStatus = "manual_approved";
        payment.UpdatedAt = DateTime.Now;

        // Update invoice
        if (payment.Invoice != null)
        {
            payment.Invoice.PaidAmount = payment.Invoice.TotalAmount;
            payment.Invoice.Status = "Paid";
            payment.Invoice.UpdatedAt = DateTime.Now;
        }

        // Calculate months from the amount
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

            // Extend or activate subscription
            if (subscription.Status == "Expired" || subscription.Status == "PendingPayment")
            {
                subscription.Status = "Active";
                subscription.StartDate = DateTime.UtcNow;
                subscription.EndDate = DateTime.UtcNow.AddMonths(months);
            }
            else if (subscription.Status == "Active")
            {
                // Extend from current end date
                subscription.EndDate = (subscription.EndDate ?? DateTime.UtcNow).AddMonths(months);
            }
            subscription.UpdatedAt = DateTime.Now;

            // Activate firm if expired
            var firm = subscription.Firm;
            if (firm != null && (firm.Status == "Expired" || firm.Status == "PendingPayment"))
            {
                firm.Status = "Active";
                firm.UpdatedAt = DateTime.Now;
            }

            // Activate any expired users
            var expiredUsers = await _context.Users
                .Where(u => u.FirmID == subscription.FirmID && (u.Status == "Expired" || u.Status == "PendingPayment"))
                .ToListAsync();
            foreach (var user in expiredUsers)
            {
                user.Status = "Active";
                user.UpdatedAt = DateTime.Now;
            }

            // Create revenue record
            var revenue = new Revenue
            {
                SubscriptionID = subscription.SubscriptionID,
                PaymentID = payment.PaymentID,
                Source = "Subscription",
                GrossAmount = payment.Amount,
                TaxAmount = payment.TaxAmount,
                NetAmount = payment.NetAmount,
                TaxRate = 12.00m,
                Amount = payment.Amount,
                RevenueDate = DateTime.Today,
                Description = $"Manual renewal ({months} month{(months > 1 ? "s" : "")}) - {subscription.PlanType} Plan ({firm?.FirmName})",
                Category = "Monthly",
                CreatedAt = DateTime.Now
            };
            _context.Revenues.Add(revenue);

            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = $"Payment approved! {firm?.FirmName}'s {subscription.PlanType} subscription extended by {months} month(s) until {subscription.EndDate?.ToString("MMM dd, yyyy")}."
            });
        }

        await _context.SaveChangesAsync();
        return Json(new { success = true, message = "Payment approved." });
    }

    /// <summary>
    /// Reject a manual payment submitted by a law firm admin
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> RejectPayment(int paymentId, string? reason = null)
    {
        var payment = await _context.Payments
            .Include(p => p.Invoice)
            .FirstOrDefaultAsync(p => p.PaymentID == paymentId);

        if (payment == null)
            return Json(new { success = false, message = "Payment not found." });

        if (payment.Status != "PendingApproval")
            return Json(new { success = false, message = "Payment is not pending approval." });

        payment.Status = "Rejected";
        payment.PayMongoStatus = "manual_rejected";
        payment.Notes = (payment.Notes ?? "") + $" | Rejected: {reason ?? "No reason provided"}";
        payment.UpdatedAt = DateTime.Now;

        // Cancel the associated invoice
        if (payment.Invoice != null)
        {
            payment.Invoice.Status = "Cancelled";
            payment.Invoice.UpdatedAt = DateTime.Now;
        }

        await _context.SaveChangesAsync();

        return Json(new { success = true, message = "Payment rejected." });
    }
}
