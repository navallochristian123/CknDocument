using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;

namespace CKNDocument.Services;

/// <summary>
/// Background service that handles the full document retention lifecycle:
/// 1. Sends advance notifications 30 days before retention expires
/// 2. Archives documents when retention expires (starts 30-day grace period)
/// 3. Auto-deletes documents after grace period (unless on legal hold)
/// 4. Generates destruction audit logs
/// 
/// Workflow: Retention Expires → Admin Notification → Grace Period (30 days) → 
///           Admin Hold Check → Auto-Delete (if no hold) → Audit Log & Certificate
/// </summary>
public class RetentionArchiveBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RetentionArchiveBackgroundService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);
    private const int GRACE_PERIOD_DAYS = 30;
    private const int ADVANCE_NOTIFICATION_DAYS = 30;

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
                await ProcessRetentionLifecycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in retention lifecycle processing");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Retention Archive Background Service stopped");
    }

    private async Task ProcessRetentionLifecycleAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LawFirmDMSDbContext>();
        var auditLogService = scope.ServiceProvider.GetRequiredService<AuditLogService>();
        var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();

        var now = DateTime.UtcNow;
        _logger.LogInformation("Running retention lifecycle checks at {Time}", now);

        // Step 1: Send advance notifications (30 days before expiry)
        await SendAdvanceNotificationsAsync(context, notificationService, auditLogService, now, stoppingToken);

        // Step 2: Archive expired retention documents (retention period just ended)
        await ArchiveExpiredRetentionsAsync(context, auditLogService, notificationService, now, stoppingToken);

        // Step 3: Process grace period expirations (auto-delete after 30-day grace)
        await ProcessGracePeriodExpirationsAsync(context, auditLogService, notificationService, now, stoppingToken);
    }

    /// <summary>
    /// Step 1: Send notifications 30 days before retention expires
    /// </summary>
    private async Task SendAdvanceNotificationsAsync(
        LawFirmDMSDbContext context,
        NotificationService notificationService,
        AuditLogService auditLogService,
        DateTime now,
        CancellationToken stoppingToken)
    {
        var notificationDate = now.AddDays(ADVANCE_NOTIFICATION_DAYS);

        // Find documents whose retention expires within next 30 days and haven't been notified yet
        var upcomingExpirations = await context.DocumentRetentions
            .Include(r => r.Document)
            .Where(r => r.IsArchived != true &&
                       r.ExpiryDate <= notificationDate &&
                       r.ExpiryDate > now &&
                       r.Document != null &&
                       (r.Document.Status == "Completed" || r.Document.Status == "Approved"))
            .ToListAsync(stoppingToken);

        foreach (var retention in upcomingExpirations)
        {
            if (stoppingToken.IsCancellationRequested) break;
            if (retention.Document == null || retention.FirmId == null) continue;

            try
            {
                // Check if we already sent notification for this document
                var alreadyNotified = await context.Archives
                    .AnyAsync(a => a.DocumentID == retention.DocumentID &&
                                  a.ExpiryNotificationSent == true, stoppingToken);

                if (alreadyNotified) continue;

                var daysUntilExpiry = (int)(retention.ExpiryDate!.Value - now).TotalDays;

                // Notify all admins of the firm
                await notificationService.NotifyAllAdminAsync(
                    retention.FirmId.Value,
                    "Retention Expiring Soon",
                    $"Document '{retention.Document.Title}' retention expires in {daysUntilExpiry} days (on {retention.ExpiryDate:MMM dd, yyyy}). Review and prepare for disposition.",
                    "RetentionExpiringSoon",
                    retention.DocumentID,
                    "/Retention");

                _logger.LogInformation("Sent advance notification for document {DocumentId}, expires in {Days} days",
                    retention.DocumentID, daysUntilExpiry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send advance notification for document {DocumentId}", retention.DocumentID);
            }
        }
    }

    /// <summary>
    /// Step 2: Archive documents whose retention period has expired
    /// Sets up the grace period for the post-retention workflow
    /// </summary>
    private async Task ArchiveExpiredRetentionsAsync(
        LawFirmDMSDbContext context,
        AuditLogService auditLogService,
        NotificationService notificationService,
        DateTime now,
        CancellationToken stoppingToken)
    {
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
                    retention.IsArchived = true;
                    retention.ModifiedAt = now;
                    await context.SaveChangesAsync(stoppingToken);
                    continue;
                }

                // Create archive with post-retention workflow
                var gracePeriodEnd = now.AddDays(GRACE_PERIOD_DAYS);

                var archive = new Archive
                {
                    DocumentID = retention.DocumentID,
                    FirmId = retention.FirmId,
                    ArchivedDate = now,
                    Reason = $"Auto-archived: Retention period expired on {retention.ExpiryDate:d}",
                    ArchiveType = "AutoExpired",
                    ArchivedBy = null,
                    IsRestored = false,
                    OriginalStatus = retention.Document.Status,
                    OriginalWorkflowStage = retention.Document.WorkflowStage,
                    OriginalFolderId = retention.Document.FolderId,
                    OriginalRetentionDate = retention.ExpiryDate,
                    ScheduledDeleteDate = gracePeriodEnd,
                    RetentionDispositionStatus = "PendingReview",
                    GracePeriodStartDate = now,
                    GracePeriodEndDate = gracePeriodEnd,
                    IsOnHold = false,
                    ExpiryNotificationSent = true,
                    ExpiryNotifiedAt = true,
                    CreatedAt = now
                };

                context.Archives.Add(archive);

                // Update document status
                retention.Document.Status = "Archived";
                retention.Document.WorkflowStage = "Archived";


                // Mark retention as archived
                retention.IsArchived = true;
                retention.ModifiedAt = now;
                retention.ModificationReason = "Auto-archived due to retention expiry";

                await context.SaveChangesAsync(stoppingToken);

                // Notify admins
                if (retention.FirmId.HasValue)
                {
                    await notificationService.NotifyAllAdminAsync(
                        retention.FirmId.Value,
                        "Retention Period Expired",
                        $"Document '{retention.Document.Title}' retention has expired. A {GRACE_PERIOD_DAYS}-day grace period started. Place a legal hold if needed, or the document will be auto-deleted on {gracePeriodEnd:MMM dd, yyyy}.",
                        "RetentionExpired",
                        retention.DocumentID,
                        "/Retention");
                }

                _logger.LogInformation("Auto-archived document {DocumentId}: {Title}, grace period until {GraceEnd}",
                    retention.DocumentID, retention.Document.Title, gracePeriodEnd);

                archivedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error archiving document {DocumentId}", retention.DocumentID);
                errorCount++;
            }
        }

        if (archivedCount > 0 || errorCount > 0)
        {
            try
            {
                await auditLogService.LogAsync(
                    "AutoArchiveScheduled",
                    "System",
                    0,
                    $"Scheduled auto-archive completed: {archivedCount} documents archived with {GRACE_PERIOD_DAYS}-day grace period, {errorCount} errors",
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

    /// <summary>
    /// Step 3: Process grace period expirations - auto-delete documents
    /// Skips documents on legal hold
    /// </summary>
    private async Task ProcessGracePeriodExpirationsAsync(
        LawFirmDMSDbContext context,
        AuditLogService auditLogService,
        NotificationService notificationService,
        DateTime now,
        CancellationToken stoppingToken)
    {
        var readyForDeletion = await context.Archives
            .Include(a => a.Document)
                .ThenInclude(d => d!.Versions)
            .Include(a => a.Document)
                .ThenInclude(d => d!.Uploader)
            .Where(a => a.GracePeriodEndDate <= now &&
                       a.IsOnHold != true &&
                       a.IsDeleted != true &&
                       a.IsRestored != true &&
                       a.RetentionDispositionStatus == "PendingReview" &&
                       (a.ArchiveType == "AutoExpired" || a.ArchiveType == "Retention"))
            .ToListAsync(stoppingToken);

        if (!readyForDeletion.Any())
        {
            _logger.LogInformation("No archives ready for auto-deletion");
            return;
        }

        _logger.LogInformation("Found {Count} archives ready for auto-deletion after grace period", readyForDeletion.Count);

        int deletedCount = 0;
        int errorCount = 0;

        foreach (var archive in readyForDeletion)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                if (archive.Document == null) continue;

                var documentTitle = archive.Document.Title ?? "Unknown";
                var documentType = archive.Document.DocumentType ?? "Unknown";
                var clientName = archive.Document.Uploader?.FullName ?? "Unknown";
                var firmId = archive.FirmId;

                // Delete physical files
                if (archive.Document.Versions != null)
                {
                    foreach (var version in archive.Document.Versions)
                    {
                        if (!string.IsNullOrEmpty(version.FilePath) && System.IO.File.Exists(version.FilePath))
                        {
                            try
                            {
                                System.IO.File.Delete(version.FilePath);
                                _logger.LogInformation("Deleted file: {FilePath}", version.FilePath);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to delete file: {FilePath}", version.FilePath);
                            }
                        }
                    }
                }

                // Mark archive as destroyed (keep metadata for audit trail)
                archive.RetentionDispositionStatus = "Destroyed";
                archive.IsDeleted = true;
                archive.DeletedAt = now;
                archive.DestroyedAt = now;
                archive.HasDestructionCertificate = true;

                // Remove related records with FK constraints first
                var signatures = await context.DocumentSignatures
                    .Where(s => s.DocumentId == archive.DocumentID).ToListAsync(stoppingToken);
                if (signatures.Any())
                    context.DocumentSignatures.RemoveRange(signatures);

                var accesses = await context.DocumentAccesses
                    .Where(a => a.DocumentID == archive.DocumentID).ToListAsync(stoppingToken);
                if (accesses.Any())
                    context.DocumentAccesses.RemoveRange(accesses);

                var notifications = await context.Notifications
                    .Where(n => n.DocumentId == archive.DocumentID).ToListAsync(stoppingToken);
                if (notifications.Any())
                    context.Notifications.RemoveRange(notifications);

                var aiAnalyses = await context.DocumentAIAnalyses
                    .Where(a => a.DocumentId == archive.DocumentID).ToListAsync(stoppingToken);
                if (aiAnalyses.Any())
                    context.DocumentAIAnalyses.RemoveRange(aiAnalyses);

                // Delete document versions from DB
                if (archive.Document.Versions != null)
                {
                    context.DocumentVersions.RemoveRange(archive.Document.Versions);
                }

                // Delete retention records
                var retentionRecords = await context.DocumentRetentions
                    .Where(dr => dr.DocumentID == archive.DocumentID)
                    .ToListAsync(stoppingToken);
                if (retentionRecords.Any())
                    context.DocumentRetentions.RemoveRange(retentionRecords);

                // Delete checklist results BEFORE reviews (FK constraint: ChecklistResult -> Review)
                var reviews = await context.DocumentReviews
                    .Where(r => r.DocumentId == archive.DocumentID)
                    .ToListAsync(stoppingToken);
                if (reviews.Any())
                {
                    var reviewIds = reviews.Select(r => r.ReviewId).ToList();
                    var checklistResults = await context.DocumentChecklistResults
                        .Where(cr => reviewIds.Contains(cr.ReviewId))
                        .ToListAsync(stoppingToken);
                    if (checklistResults.Any())
                        context.DocumentChecklistResults.RemoveRange(checklistResults);
                    context.DocumentReviews.RemoveRange(reviews);
                }

                // Delete second opinion requests referencing this document
                var secondOpinions = await context.SecondOpinionRequests
                    .Where(s => s.DocumentId == archive.DocumentID)
                    .ToListAsync(stoppingToken);
                if (secondOpinions.Any())
                    context.SecondOpinionRequests.RemoveRange(secondOpinions);

                // Update Document status using ExecuteUpdateAsync to avoid UpdatedAt column issue
                if (archive.DocumentID.HasValue)
                {
                    await context.Documents
                        .Where(d => d.DocumentID == archive.DocumentID.Value)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(d => d.Status, "Destroyed")
                            .SetProperty(d => d.WorkflowStage, "Destroyed"), stoppingToken);
                }

                // Detach the Document entity to prevent EF from trying to update it via tracking
                if (archive.Document != null)
                {
                    context.Entry(archive.Document).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                }

                await context.SaveChangesAsync(stoppingToken);

                // Create destruction audit log
                await auditLogService.LogAsync(
                    "DocumentDestroyed",
                    "Document",
                    archive.DocumentID ?? 0,
                    $"Document permanently destroyed after retention period: '{documentTitle}' (Type: {documentType}, Client: {clientName}). " +
                    $"Retention expired: {archive.OriginalRetentionDate:d}, Grace period ended: {archive.GracePeriodEndDate:d}",
                    null,
                    null,
                    "RetentionDestruction");

                // Notify admins
                if (firmId.HasValue)
                {
                    await notificationService.NotifyAllAdminAsync(
                        firmId.Value,
                        "Document Destroyed",
                        $"Document '{documentTitle}' has been permanently destroyed after retention period expiry and {GRACE_PERIOD_DAYS}-day grace period. A destruction certificate is available.",
                        "DocumentDestroyed",
                        archive.DocumentID,
                        "/Retention");
                }

                _logger.LogInformation("Auto-destroyed document {DocumentId}: {Title}", archive.DocumentID, documentTitle);

                deletedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error destroying archive {ArchiveId}", archive.ArchiveID);
                errorCount++;
            }
        }

        if (deletedCount > 0 || errorCount > 0)
        {
            try
            {
                await auditLogService.LogAsync(
                    "AutoDestructionScheduled",
                    "System",
                    0,
                    $"Scheduled auto-destruction completed: {deletedCount} documents destroyed, {errorCount} errors",
                    null,
                    null,
                    "SystemOperation");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to log audit entry for auto-destruction");
            }
        }

        _logger.LogInformation("Auto-destruction completed: {Deleted} destroyed, {Errors} errors", deletedCount, errorCount);
    }
}
