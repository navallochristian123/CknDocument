using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;

namespace CKNDocument.Services;

/// <summary>
/// Background service that automatically archives documents when their retention period expires
/// Runs daily to check for expired documents
/// </summary>
public class RetentionArchiveBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RetentionArchiveBackgroundService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24); // Run daily

    public RetentionArchiveBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<RetentionArchiveBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Retention Archive Background Service started");

        // Initial delay to let the application fully start
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredRetentionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing expired retentions");
            }

            // Wait for next check interval
            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Retention Archive Background Service stopped");
    }

    private async Task ProcessExpiredRetentionsAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LawFirmDMSDbContext>();
        var auditLogService = scope.ServiceProvider.GetRequiredService<AuditLogService>();

        var now = DateTime.UtcNow;
        _logger.LogInformation("Checking for expired retention documents at {Time}", now);

        // Find all expired retention documents that are not already archived
        var expiredRetentions = await context.DocumentRetentions
            .Include(r => r.Document)
            .Where(r => r.IsArchived != true &&
                       r.ExpiryDate <= now &&
                       r.Document != null &&
                       (r.Document.Status == "Completed" || r.Document.Status == "Approved"))
            .ToListAsync(stoppingToken);

        if (!expiredRetentions.Any())
        {
            _logger.LogInformation("No expired retention documents found");
            return;
        }

        _logger.LogInformation("Found {Count} expired retention documents", expiredRetentions.Count);

        int archivedCount = 0;
        int errorCount = 0;

        foreach (var retention in expiredRetentions)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                if (retention.Document == null) continue;

                // Check if already archived
                var existingArchive = await context.Archives
                    .FirstOrDefaultAsync(a => a.DocumentID == retention.DocumentID &&
                                             a.IsRestored != true &&
                                             a.IsDeleted != true, stoppingToken);

                if (existingArchive != null)
                {
                    _logger.LogDebug("Document {DocumentId} already archived, skipping", retention.DocumentID);
                    continue;
                }

                var archive = new Archive
                {
                    DocumentID = retention.DocumentID,
                    FirmId = retention.FirmId,
                    ArchivedDate = now,
                    Reason = $"Auto-archived: Retention period expired on {retention.ExpiryDate:d}",
                    ArchiveType = "AutoExpired",
                    ArchivedBy = null, // System action
                    IsRestored = false,
                    OriginalStatus = retention.Document.Status,
                    OriginalWorkflowStage = retention.Document.WorkflowStage,
                    OriginalFolderId = retention.Document.FolderId,
                    OriginalRetentionDate = retention.ExpiryDate,
                    ScheduledDeleteDate = now.AddYears(1), // Schedule for permanent deletion in 1 year
                    CreatedAt = now
                };

                context.Archives.Add(archive);

                // Update document status
                retention.Document.Status = "Archived";
                retention.Document.WorkflowStage = "Archived";
                retention.Document.UpdatedAt = now;

                // Mark retention as archived
                retention.IsArchived = true;
                retention.ModifiedAt = now;
                retention.ModificationReason = "Auto-archived due to retention expiry";

                await context.SaveChangesAsync(stoppingToken);

                _logger.LogInformation("Auto-archived document {DocumentId}: {Title}",
                    retention.DocumentID, retention.Document.Title);

                archivedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error archiving document {DocumentId}", retention.DocumentID);
                errorCount++;
            }
        }

        // Log summary
        if (archivedCount > 0 || errorCount > 0)
        {
            try
            {
                await auditLogService.LogAsync(
                    "AutoArchiveScheduled",
                    "System",
                    0,
                    $"Scheduled auto-archive completed: {archivedCount} documents archived, {errorCount} errors",
                    null,
                    null,
                    "SystemOperation");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to log audit entry for auto-archive");
            }
        }

        _logger.LogInformation("Auto-archive completed: {Archived} archived, {Errors} errors", archivedCount, errorCount);
    }
}
