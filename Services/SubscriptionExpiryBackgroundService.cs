using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;

namespace CKNDocument.Services;

/// <summary>
/// Background service that checks for expired subscriptions daily
/// When a subscription expires, the firm and its users are deactivated
/// Generates renewal invoices before expiry
/// </summary>
public class SubscriptionExpiryBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SubscriptionExpiryBackgroundService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(12);

    public SubscriptionExpiryBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<SubscriptionExpiryBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Subscription Expiry Background Service started");
        await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckExpiredSubscriptionsAsync(stoppingToken);
                await GenerateRenewalInvoicesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking subscription expiry");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    /// <summary>
    /// Deactivate firms whose subscriptions have expired
    /// </summary>
    private async Task CheckExpiredSubscriptionsAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LawFirmDMSDbContext>();

        var expiredSubs = await context.FirmSubscriptions
            .Include(s => s.Firm)
            .Where(s => s.Status == "Active" && s.EndDate != null && s.EndDate < DateTime.UtcNow)
            .ToListAsync(stoppingToken);

        foreach (var sub in expiredSubs)
        {
            sub.Status = "Expired";
            sub.UpdatedAt = DateTime.Now;

            if (sub.Firm != null)
            {
                sub.Firm.Status = "Expired";
                sub.Firm.UpdatedAt = DateTime.Now;
                _logger.LogWarning("Subscription expired for firm {FirmName} (ID: {FirmId})", sub.Firm.FirmName, sub.FirmID);
            }

            // Deactivate all users of this firm
            var firmUsers = await context.Users
                .Where(u => u.FirmID == sub.FirmID && u.Status == "Active")
                .ToListAsync(stoppingToken);

            foreach (var user in firmUsers)
            {
                user.Status = "Expired";
                user.UpdatedAt = DateTime.Now;
            }
        }

        if (expiredSubs.Any())
        {
            await context.SaveChangesAsync(stoppingToken);
            _logger.LogInformation("Deactivated {Count} expired subscriptions", expiredSubs.Count);
        }
    }

    /// <summary>
    /// Generate renewal invoices 7 days before subscription expires
    /// </summary>
    private async Task GenerateRenewalInvoicesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LawFirmDMSDbContext>();

        var sevenDaysFromNow = DateTime.UtcNow.AddDays(7);
        var subsExpiringSoon = await context.FirmSubscriptions
            .Include(s => s.Firm)
            .Include(s => s.Invoices)
            .Where(s => s.Status == "Active" && s.EndDate != null
                && s.EndDate <= sevenDaysFromNow
                && s.EndDate > DateTime.UtcNow)
            .ToListAsync(stoppingToken);

        foreach (var sub in subsExpiringSoon)
        {
            // Check if a renewal invoice already exists for this period
            var hasRenewalInvoice = sub.Invoices.Any(i =>
                i.Status != "Cancelled"
                && i.Notes != null && i.Notes.Contains("Renewal")
                && i.DueDate >= sub.EndDate?.AddDays(-7));

            if (hasRenewalInvoice) continue;

            var monthlyPrice = sub.PlanType switch
            {
                "Starter" => 1499m,
                "Professional" => 3499m,
                "Enterprise" => 7999m,
                _ => 1499m
            };

            var invoice = new Models.LawFirmDMS.Invoice
            {
                SubscriptionID = sub.SubscriptionID,
                InvoiceNumber = $"INV-{DateTime.Now:yyyyMMdd}-{sub.SubscriptionID:D4}-R",
                InvoiceDate = DateTime.Today,
                DueDate = sub.EndDate?.Date ?? DateTime.Today.AddDays(7),
                TotalAmount = monthlyPrice,
                PaidAmount = 0,
                Status = "Pending",
                Notes = $"Renewal payment for {sub.PlanType} plan - {sub.Firm?.FirmName}",
                CreatedAt = DateTime.Now
            };

            context.Invoices.Add(invoice);
            _logger.LogInformation("Created renewal invoice for firm {FirmName}, due {DueDate}",
                sub.Firm?.FirmName, invoice.DueDate?.ToString("MMM dd, yyyy"));
        }

        await context.SaveChangesAsync(stoppingToken);
    }
}
