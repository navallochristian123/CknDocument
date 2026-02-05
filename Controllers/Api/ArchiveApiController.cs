using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;
using CKNDocument.Services;
using System.Security.Claims;

namespace CKNDocument.Controllers.Api;

/// <summary>
/// API Controller for Archive operations
/// Handles archived documents, rejected documents, retention queue, and document lifecycle
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "FirmMember")]
public class ArchiveApiController : ControllerBase
{
    private readonly LawFirmDMSDbContext _context;
    private readonly AuditLogService _auditLogService;
    private readonly ILogger<ArchiveApiController> _logger;
    private readonly IWebHostEnvironment _environment;

    public ArchiveApiController(
        LawFirmDMSDbContext context,
        AuditLogService auditLogService,
        ILogger<ArchiveApiController> logger,
        IWebHostEnvironment environment)
    {
        _context = context;
        _auditLogService = auditLogService;
        _logger = logger;
        _environment = environment;
    }

    private int GetCurrentUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    private int GetFirmId() => int.Parse(User.FindFirst("FirmId")?.Value ?? "0");
    private string GetUserRole() => User.FindFirst(ClaimTypes.Role)?.Value ?? "Client";

    /// <summary>
    /// Get archives based on type filter
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetArchives([FromQuery] string? type = null)
    {
        try
        {
            var firmId = GetFirmId();
            var role = GetUserRole();
            var userId = GetCurrentUserId();

            var query = _context.Archives
                .Include(a => a.Document)
                    .ThenInclude(d => d!.Uploader)
                .Include(a => a.Document)
                    .ThenInclude(d => d!.Folder)
                .Include(a => a.ArchivedByUser)
                .Include(a => a.RestoredByUser)
                .Where(a => a.Document != null && 
                           a.Document.FirmID == firmId && 
                           a.IsRestored != true && 
                           a.IsDeleted != true);

            // For clients, only show their own documents
            if (role == "Client")
            {
                query = query.Where(a => a.Document!.UploadedBy == userId);
            }

            // Filter by type
            switch (type?.ToLower())
            {
                case "archived":
                    query = query.Where(a => a.ArchiveType == "Manual" || a.ArchiveType == "Version");
                    break;
                case "rejected":
                    query = query.Where(a => a.ArchiveType == "Rejected");
                    break;
                case "retention":
                    query = query.Where(a => a.ArchiveType == "Retention" || a.ArchiveType == "AutoExpired");
                    break;
                case "version":
                    query = query.Where(a => a.ArchiveType == "Version");
                    break;
                case "all":
                default:
                    // No additional filter
                    break;
            }

            var archives = await query
                .OrderByDescending(a => a.ArchivedDate)
                .Select(a => new
                {
                    archiveId = a.ArchiveID,
                    documentId = a.DocumentID,
                    documentTitle = a.Document != null ? a.Document.Title : "Unknown",
                    documentType = a.Document != null ? a.Document.DocumentType : null,
                    originalFileName = a.Document != null ? a.Document.OriginalFileName : null,
                    fileExtension = a.Document != null ? a.Document.FileExtension : null,
                    fileSize = a.Document != null ? a.Document.TotalFileSize : 0,
                    clientName = a.Document != null && a.Document.Uploader != null ? a.Document.Uploader.FullName : null,
                    clientEmail = a.Document != null && a.Document.Uploader != null ? a.Document.Uploader.Email : null,
                    originalFolderName = a.Document != null && a.Document.Folder != null ? a.Document.Folder.FolderName : null,
                    uploadedAt = a.Document != null ? a.Document.CreatedAt : null,
                    archiveType = a.ArchiveType,
                    archiveReason = a.Reason,
                    archivedAt = a.ArchivedDate,
                    archivedByName = a.ArchivedByUser != null ? a.ArchivedByUser.FullName : null,
                    originalRetentionDate = a.OriginalRetentionDate,
                    scheduledDeleteDate = a.ScheduledDeleteDate,
                    versionNumber = a.VersionNumber,
                    originalStatus = a.OriginalStatus,
                    isRestored = a.IsRestored,
                    restoredAt = a.RestoredAt,
                    restoredByName = a.RestoredByUser != null ? a.RestoredByUser.FullName : null
                })
                .ToListAsync();

            return Ok(new { success = true, archives });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading archives");
            return Ok(new { success = false, message = "Error loading archives: " + ex.Message, archives = new List<object>() });
        }
    }

    /// <summary>
    /// Get client's own archived documents
    /// </summary>
    [HttpGet("my-archives")]
    public async Task<IActionResult> GetMyArchives()
    {
        try
        {
            var userId = GetCurrentUserId();
            var firmId = GetFirmId();

            var archives = await _context.Archives
                .Include(a => a.Document)
                    .ThenInclude(d => d!.Folder)
                .Where(a => a.Document != null && 
                           a.Document.FirmID == firmId && 
                           a.Document.UploadedBy == userId && 
                           a.IsRestored != true &&
                           a.IsDeleted != true)
                .OrderByDescending(a => a.ArchivedDate)
                .Select(a => new
                {
                    archiveId = a.ArchiveID,
                    documentId = a.DocumentID,
                    documentTitle = a.Document != null ? a.Document.Title : "Unknown",
                    documentType = a.Document != null ? a.Document.DocumentType : null,
                    originalFileName = a.Document != null ? a.Document.OriginalFileName : null,
                    fileExtension = a.Document != null ? a.Document.FileExtension : null,
                    originalFolderName = a.Document != null && a.Document.Folder != null ? a.Document.Folder.FolderName : null,
                    archiveType = a.ArchiveType,
                    archiveReason = a.Reason,
                    archivedAt = a.ArchivedDate
                })
                .ToListAsync();

            return Ok(new { success = true, archives });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading my archives");
            return Ok(new { success = false, message = "Error loading archives", archives = new List<object>() });
        }
    }

    /// <summary>
    /// Get archive details
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetArchiveDetails(int id)
    {
        try
        {
            var firmId = GetFirmId();
            var role = GetUserRole();
            var userId = GetCurrentUserId();

            var archive = await _context.Archives
                .Include(a => a.Document)
                    .ThenInclude(d => d!.Uploader)
                .Include(a => a.Document)
                    .ThenInclude(d => d!.Folder)
                .Include(a => a.Document)
                    .ThenInclude(d => d!.Versions.OrderByDescending(v => v.VersionNumber))
                .Include(a => a.ArchivedByUser)
                .Include(a => a.RestoredByUser)
                .FirstOrDefaultAsync(a => a.ArchiveID == id && 
                                         a.Document != null && 
                                         a.Document.FirmID == firmId &&
                                         a.IsDeleted != true);

            if (archive == null)
                return NotFound(new { success = false, message = "Archive not found" });

            // Check permissions for clients
            if (role == "Client" && archive.Document?.UploadedBy != userId)
                return Forbid();

            // Get retention info if exists
            var retention = await _context.DocumentRetentions
                .Include(r => r.Policy)
                .FirstOrDefaultAsync(r => r.DocumentID == archive.DocumentID);

            return Ok(new
            {
                success = true,
                archive = new
                {
                    archiveId = archive.ArchiveID,
                    documentId = archive.DocumentID,
                    documentTitle = archive.Document?.Title,
                    documentDescription = archive.Document?.Description,
                    documentType = archive.Document?.DocumentType,
                    originalFileName = archive.Document?.OriginalFileName,
                    fileExtension = archive.Document?.FileExtension,
                    totalFileSize = archive.Document?.TotalFileSize,
                    clientName = archive.Document?.Uploader?.FullName,
                    clientEmail = archive.Document?.Uploader?.Email,
                    folderName = archive.Document?.Folder?.FolderName,
                    uploadedAt = archive.Document?.CreatedAt,
                    archiveType = archive.ArchiveType,
                    archiveReason = archive.Reason,
                    archivedAt = archive.ArchivedDate,
                    archivedByName = archive.ArchivedByUser?.FullName,
                    originalRetentionDate = archive.OriginalRetentionDate,
                    originalStatus = archive.OriginalStatus,
                    originalWorkflowStage = archive.OriginalWorkflowStage,
                    scheduledDeleteDate = archive.ScheduledDeleteDate,
                    isRestored = archive.IsRestored,
                    restoredAt = archive.RestoredAt,
                    restoredByName = archive.RestoredByUser?.FullName,
                    retentionPolicy = retention != null ? new
                    {
                        policyName = retention.Policy?.PolicyName,
                        retentionYears = retention.RetentionYears,
                        retentionMonths = retention.RetentionMonths,
                        retentionDays = retention.RetentionDays,
                        expiryDate = retention.ExpiryDate
                    } : null,
                    versions = archive.Document?.Versions.Select(v => new
                    {
                        versionId = v.VersionId,
                        versionNumber = v.VersionNumber,
                        originalFileName = v.OriginalFileName,
                        fileSize = v.FileSize,
                        createdAt = v.CreatedAt,
                        isCurrentVersion = v.IsCurrentVersion
                    })
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading archive details");
            return Ok(new { success = false, message = "Error loading archive details" });
        }
    }

    /// <summary>
    /// Manually archive a completed/approved document (Admin only)
    /// </summary>
    [HttpPost("archive-document/{documentId}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ArchiveDocument(int documentId, [FromBody] ArchiveDocumentRequest dto)
    {
        try
        {
            var userId = GetCurrentUserId();
            var firmId = GetFirmId();

            if (string.IsNullOrWhiteSpace(dto?.Reason))
                return BadRequest(new { success = false, message = "Archive reason is required" });

            var document = await _context.Documents
                .Include(d => d.Folder)
                .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.FirmID == firmId);

            if (document == null)
                return NotFound(new { success = false, message = "Document not found" });

            // Can only archive completed/approved documents manually
            if (document.Status != "Completed" && document.Status != "Approved")
                return BadRequest(new { success = false, message = "Only completed or approved documents can be manually archived" });

            // Check if already archived
            var existingArchive = await _context.Archives
                .FirstOrDefaultAsync(a => a.DocumentID == documentId && a.IsRestored != true && a.IsDeleted != true);

            if (existingArchive != null)
                return BadRequest(new { success = false, message = "Document is already archived" });

            // Get retention info
            var retention = await _context.DocumentRetentions
                .FirstOrDefaultAsync(r => r.DocumentID == documentId);

            var archive = new Archive
            {
                DocumentID = documentId,
                FirmId = firmId,
                ArchivedDate = DateTime.UtcNow,
                Reason = dto.Reason,
                ArchiveType = "Manual",
                ArchivedBy = userId,
                IsRestored = false,
                OriginalStatus = document.Status,
                OriginalWorkflowStage = document.WorkflowStage,
                OriginalFolderId = document.FolderId,
                OriginalRetentionDate = retention?.ExpiryDate,
                CreatedAt = DateTime.UtcNow
            };

            _context.Archives.Add(archive);

            // Update document to archived status - remove from normal document list
            document.Status = "Archived";
            document.WorkflowStage = "Archived";
            document.UpdatedAt = DateTime.UtcNow;

            // Update retention as archived
            if (retention != null)
            {
                retention.IsArchived = true;
                retention.ModifiedBy = userId;
                retention.ModifiedAt = DateTime.UtcNow;
                retention.ModificationReason = "Document manually archived";
            }

            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                "ManualArchive",
                "Archive",
                archive.ArchiveID,
                $"Manually archived document: {document.Title}. Reason: {dto.Reason}",
                null,
                null,
                "ArchiveOperation");

            return Ok(new { success = true, message = "Document archived successfully", archiveId = archive.ArchiveID });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error archiving document");
            return Ok(new { success = false, message = "Error archiving document: " + ex.Message });
        }
    }

    /// <summary>
    /// Archive a rejected document
    /// </summary>
    [HttpPost("archive-rejected/{documentId}")]
    [Authorize(Policy = "AdminOrStaff")]
    public async Task<IActionResult> ArchiveRejectedDocument(int documentId, [FromBody] ArchiveReasonDto? dto = null)
    {
        try
        {
            var userId = GetCurrentUserId();
            var firmId = GetFirmId();

            var document = await _context.Documents
                .Include(d => d.Folder)
                .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.FirmID == firmId);

            if (document == null)
                return NotFound(new { success = false, message = "Document not found" });

            if (document.Status != "Rejected")
                return BadRequest(new { success = false, message = "Only rejected documents can be archived this way" });

            // Check if already archived
            var existingArchive = await _context.Archives
                .FirstOrDefaultAsync(a => a.DocumentID == documentId && a.IsRestored != true && a.IsDeleted != true);

            if (existingArchive != null)
                return BadRequest(new { success = false, message = "Document is already archived" });

            var archive = new Archive
            {
                DocumentID = documentId,
                FirmId = firmId,
                ArchivedDate = DateTime.UtcNow,
                Reason = dto?.Reason ?? "Rejected document archived",
                ArchiveType = "Rejected",
                ArchivedBy = userId,
                IsRestored = false,
                OriginalStatus = document.Status,
                OriginalWorkflowStage = document.WorkflowStage,
                OriginalFolderId = document.FolderId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Archives.Add(archive);

            // Update document status
            document.WorkflowStage = "Archived";
            document.Status = "Archived";
            document.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                "ArchiveRejected",
                "Archive",
                archive.ArchiveID,
                $"Archived rejected document: {document.Title}",
                null,
                null,
                "ArchiveOperation");

            return Ok(new { success = true, message = "Rejected document archived", archiveId = archive.ArchiveID });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error archiving rejected document");
            return Ok(new { success = false, message = "Error archiving document: " + ex.Message });
        }
    }

    /// <summary>
    /// Restore an archived document
    /// </summary>
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> RestoreArchive(int id, [FromBody] RestoreArchiveDto? dto = null)
    {
        try
        {
            var userId = GetCurrentUserId();
            var firmId = GetFirmId();
            var role = GetUserRole();

            var archive = await _context.Archives
                .Include(a => a.Document)
                .FirstOrDefaultAsync(a => a.ArchiveID == id && 
                                         a.Document != null && 
                                         a.Document.FirmID == firmId &&
                                         a.IsDeleted != true);

            if (archive == null)
                return NotFound(new { success = false, message = "Archive not found" });

            // Check permissions - only admin can restore, or client for their own rejected docs
            if (role == "Client")
            {
                if (archive.Document?.UploadedBy != userId)
                    return Forbid();
                if (archive.ArchiveType != "Rejected")
                    return BadRequest(new { success = false, message = "Clients can only restore their rejected documents" });
            }
            else if (role != "Admin")
            {
                return Forbid();
            }

            if (archive.Document == null)
                return BadRequest(new { success = false, message = "Associated document not found" });

            // Restore the document to original or default status
            var restoreStatus = archive.OriginalStatus ?? "Completed";
            var restoreStage = archive.OriginalWorkflowStage ?? "Completed";

            // If was rejected, restore to pending for re-review
            if (archive.ArchiveType == "Rejected")
            {
                restoreStatus = "Pending";
                restoreStage = "PendingStaffReview";
            }

            archive.Document.Status = restoreStatus;
            archive.Document.WorkflowStage = restoreStage;
            archive.Document.UpdatedAt = DateTime.UtcNow;

            // Restore to original folder if specified
            if (archive.OriginalFolderId.HasValue)
            {
                archive.Document.FolderId = archive.OriginalFolderId;
            }

            // Mark archive as restored (keep record for audit)
            archive.IsRestored = true;
            archive.RestoredAt = DateTime.UtcNow;
            archive.RestoredBy = userId;

            // Reset retention if requested or by default
            if (dto?.ResetRetention != false)
            {
                var existingRetention = await _context.DocumentRetentions
                    .FirstOrDefaultAsync(dr => dr.DocumentID == archive.DocumentID);

                if (existingRetention != null)
                {
                    // Reset retention with same policy
                    var startDate = DateTime.UtcNow;
                    existingRetention.RetentionStartDate = startDate;
                    existingRetention.ExpiryDate = startDate
                        .AddYears(existingRetention.RetentionYears ?? 7)
                        .AddMonths(existingRetention.RetentionMonths ?? 0)
                        .AddDays(existingRetention.RetentionDays ?? 0);
                    existingRetention.IsArchived = false;
                    existingRetention.IsModified = true;
                    existingRetention.ModificationReason = "Reset on document restore";
                    existingRetention.ModifiedBy = userId;
                    existingRetention.ModifiedAt = DateTime.UtcNow;

                    archive.Document.RetentionExpiryDate = existingRetention.ExpiryDate;
                }
            }

            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                "ArchiveRestore",
                "Archive",
                id,
                $"Restored document from archive: {archive.Document.Title}",
                null,
                null,
                "ArchiveOperation");

            return Ok(new
            {
                success = true,
                message = "Document restored successfully",
                documentId = archive.DocumentID,
                newStatus = archive.Document.Status,
                newRetentionExpiry = archive.Document.RetentionExpiryDate
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring archive");
            return Ok(new { success = false, message = "Error restoring document: " + ex.Message });
        }
    }

    /// <summary>
    /// Download archived document
    /// </summary>
    [HttpGet("{id}/download")]
    public async Task<IActionResult> DownloadArchivedDocument(int id, [FromQuery] int? versionId = null)
    {
        try
        {
            var firmId = GetFirmId();
            var role = GetUserRole();
            var userId = GetCurrentUserId();

            var archive = await _context.Archives
                .Include(a => a.Document)
                    .ThenInclude(d => d!.Versions)
                .FirstOrDefaultAsync(a => a.ArchiveID == id && 
                                         a.Document != null && 
                                         a.Document.FirmID == firmId &&
                                         a.IsDeleted != true);

            if (archive == null || archive.Document == null)
                return NotFound(new { success = false, message = "Archive not found" });

            // Check permissions for clients
            if (role == "Client" && archive.Document.UploadedBy != userId)
                return Forbid();

            // Get the file to download
            DocumentVersion? version;
            if (versionId.HasValue)
            {
                version = archive.Document.Versions.FirstOrDefault(v => v.VersionId == versionId);
            }
            else
            {
                version = archive.Document.Versions
                    .OrderByDescending(v => v.VersionNumber)
                    .FirstOrDefault(v => v.IsCurrentVersion == true) 
                    ?? archive.Document.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            }

            if (version == null || string.IsNullOrEmpty(version.FilePath))
                return NotFound(new { success = false, message = "File not found" });

            if (!System.IO.File.Exists(version.FilePath))
                return NotFound(new { success = false, message = "Physical file not found" });

            var fileBytes = await System.IO.File.ReadAllBytesAsync(version.FilePath);
            var contentType = version.MimeType ?? "application/octet-stream";
            var fileName = version.OriginalFileName ?? archive.Document.OriginalFileName ?? "document";

            await _auditLogService.LogAsync(
                "ArchiveDownload",
                "Archive",
                id,
                $"Downloaded archived document: {archive.Document.Title}",
                null,
                null,
                "ArchiveOperation");

            return File(fileBytes, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading archived document");
            return BadRequest(new { success = false, message = "Error downloading file" });
        }
    }

    /// <summary>
    /// Permanently delete an archived document (Admin only, retention documents only)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteArchive(int id, [FromQuery] bool force = false)
    {
        try
        {
            var firmId = GetFirmId();
            var userId = GetCurrentUserId();

            var archive = await _context.Archives
                .Include(a => a.Document)
                    .ThenInclude(d => d!.Versions)
                .FirstOrDefaultAsync(a => a.ArchiveID == id && 
                                         a.Document != null && 
                                         a.Document.FirmID == firmId &&
                                         a.IsDeleted != true);

            if (archive == null)
                return NotFound(new { success = false, message = "Archive not found" });

            // Only allow permanent delete for retention documents unless force is specified
            if (!force && archive.ArchiveType != "Retention" && archive.ArchiveType != "AutoExpired")
                return BadRequest(new { success = false, message = "Only retention-expired documents can be permanently deleted. Use force=true to override." });

            var documentTitle = archive.Document?.Title ?? "Unknown";

            // Delete physical files
            if (archive.Document?.Versions != null)
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

            // Delete related retention records
            var retentionRecords = await _context.DocumentRetentions
                .Where(dr => dr.DocumentID == archive.DocumentID)
                .ToListAsync();
            _context.DocumentRetentions.RemoveRange(retentionRecords);

            // Delete related reviews
            var reviews = await _context.DocumentReviews
                .Where(r => r.DocumentId == archive.DocumentID)
                .ToListAsync();
            _context.DocumentReviews.RemoveRange(reviews);

            // Delete document versions
            if (archive.Document?.Versions != null)
            {
                _context.DocumentVersions.RemoveRange(archive.Document.Versions);
            }

            // Mark archive as deleted (soft delete for audit trail)
            archive.IsDeleted = true;
            archive.DeletedAt = DateTime.UtcNow;
            archive.DeletedBy = userId;

            // Delete the document
            if (archive.Document != null)
            {
                _context.Documents.Remove(archive.Document);
            }

            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                "ArchiveDelete",
                "Archive",
                id,
                $"Permanently deleted document: {documentTitle}",
                null,
                null,
                "ArchiveOperation");

            return Ok(new { success = true, message = "Document permanently deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting archive");
            return Ok(new { success = false, message = "Error deleting document: " + ex.Message });
        }
    }

    /// <summary>
    /// Get archive statistics
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        try
        {
            var firmId = GetFirmId();
            var role = GetUserRole();
            var userId = GetCurrentUserId();
            var now = DateTime.UtcNow;

            var baseQuery = _context.Archives
                .Where(a => a.Document != null && a.Document.FirmID == firmId && a.IsDeleted != true);

            // For clients, only show their own stats
            if (role == "Client")
            {
                baseQuery = baseQuery.Where(a => a.Document!.UploadedBy == userId);
            }

            var stats = new
            {
                totalArchived = await baseQuery
                    .Where(a => (a.ArchiveType == "Manual" || a.ArchiveType == "Version") && a.IsRestored != true)
                    .CountAsync(),
                totalRejected = await baseQuery
                    .Where(a => a.ArchiveType == "Rejected" && a.IsRestored != true)
                    .CountAsync(),
                totalRetention = await baseQuery
                    .Where(a => (a.ArchiveType == "Retention" || a.ArchiveType == "AutoExpired") && a.IsRestored != true)
                    .CountAsync(),
                totalRestored = await baseQuery
                    .Where(a => a.IsRestored == true)
                    .CountAsync(),
                pendingDeletion = await baseQuery
                    .Where(a => a.ScheduledDeleteDate != null && 
                               a.ScheduledDeleteDate <= now.AddDays(30) &&
                               a.IsRestored != true)
                    .CountAsync(),
                archivedThisMonth = await baseQuery
                    .Where(a => a.ArchivedDate >= now.AddDays(-30) && a.IsRestored != true)
                    .CountAsync()
            };

            return Ok(new { success = true, stats });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading stats");
            return Ok(new { success = true, stats = new { totalArchived = 0, totalRejected = 0, totalRetention = 0, totalRestored = 0, pendingDeletion = 0, archivedThisMonth = 0 } });
        }
    }

    /// <summary>
    /// Get documents pending archive (completed but nearing retention expiry)
    /// </summary>
    [HttpGet("pending-archive")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetPendingArchive([FromQuery] int daysUntilExpiry = 30)
    {
        try
        {
            var firmId = GetFirmId();
            var now = DateTime.UtcNow;
            var expiryThreshold = now.AddDays(daysUntilExpiry);

            var pendingDocs = await _context.DocumentRetentions
                .Include(r => r.Document)
                    .ThenInclude(d => d!.Uploader)
                .Include(r => r.Policy)
                .Where(r => r.FirmId == firmId &&
                           r.IsArchived != true &&
                           r.ExpiryDate <= expiryThreshold &&
                           r.Document != null &&
                           r.Document.Status == "Completed")
                .OrderBy(r => r.ExpiryDate)
                .Select(r => new
                {
                    retentionId = r.RetentionID,
                    documentId = r.DocumentID,
                    documentTitle = r.Document != null ? r.Document.Title : "Unknown",
                    documentType = r.Document != null ? r.Document.DocumentType : null,
                    clientName = r.Document != null && r.Document.Uploader != null ? r.Document.Uploader.FullName : null,
                    policyName = r.Policy != null ? r.Policy.PolicyName : null,
                    expiryDate = r.ExpiryDate,
                    daysRemaining = r.ExpiryDate.HasValue ? (int)(r.ExpiryDate.Value - now).TotalDays : 0
                })
                .ToListAsync();

            return Ok(new { success = true, documents = pendingDocs });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading pending archive");
            return Ok(new { success = false, documents = new List<object>() });
        }
    }

    /// <summary>
    /// Auto-archive expired retention documents
    /// </summary>
    [HttpPost("auto-archive-expired")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AutoArchiveExpired()
    {
        try
        {
            var firmId = GetFirmId();
            var userId = GetCurrentUserId();
            var now = DateTime.UtcNow;

            // Find all expired retention documents that are not already archived
            var expiredRetentions = await _context.DocumentRetentions
                .Include(r => r.Document)
                .Where(r => r.FirmId == firmId &&
                           r.IsArchived != true &&
                           r.ExpiryDate <= now &&
                           r.Document != null &&
                           r.Document.Status == "Completed")
                .ToListAsync();

            int archivedCount = 0;

            foreach (var retention in expiredRetentions)
            {
                if (retention.Document == null) continue;

                // Check if already archived
                var existingArchive = await _context.Archives
                    .FirstOrDefaultAsync(a => a.DocumentID == retention.DocumentID && 
                                             a.IsRestored != true && 
                                             a.IsDeleted != true);

                if (existingArchive != null) continue;

                var archive = new Archive
                {
                    DocumentID = retention.DocumentID,
                    FirmId = firmId,
                    ArchivedDate = now,
                    Reason = $"Auto-archived: Retention period expired on {retention.ExpiryDate:d}",
                    ArchiveType = "AutoExpired",
                    ArchivedBy = userId,
                    IsRestored = false,
                    OriginalStatus = retention.Document.Status,
                    OriginalWorkflowStage = retention.Document.WorkflowStage,
                    OriginalFolderId = retention.Document.FolderId,
                    OriginalRetentionDate = retention.ExpiryDate,
                    ScheduledDeleteDate = now.AddYears(1), // Schedule for permanent deletion in 1 year
                    CreatedAt = now
                };

                _context.Archives.Add(archive);

                // Update document
                retention.Document.Status = "Archived";
                retention.Document.WorkflowStage = "Archived";
                retention.Document.UpdatedAt = now;

                // Mark retention as archived
                retention.IsArchived = true;
                retention.ModifiedBy = userId;
                retention.ModifiedAt = now;
                retention.ModificationReason = "Auto-archived due to expiry";

                archivedCount++;
            }

            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                "AutoArchiveExpired",
                "Archive",
                0,
                $"Auto-archived {archivedCount} expired retention documents",
                null,
                null,
                "ArchiveOperation");

            return Ok(new { success = true, message = $"Auto-archived {archivedCount} documents", count = archivedCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error auto-archiving expired documents");
            return Ok(new { success = false, message = "Error: " + ex.Message });
        }
    }
}

// DTOs
public class RestoreArchiveDto
{
    public bool? ResetRetention { get; set; } = true;
}

public class ArchiveReasonDto
{
    public string? Reason { get; set; }
}

public class ArchiveDocumentRequest
{
    public string Reason { get; set; } = string.Empty;
}
