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
/// Handles archived documents, rejected documents, and retention queue
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "FirmMember")]
public class ArchiveApiController : ControllerBase
{
    private readonly LawFirmDMSDbContext _context;
    private readonly AuditLogService _auditLogService;
    private readonly ILogger<ArchiveApiController> _logger;

    public ArchiveApiController(
        LawFirmDMSDbContext context,
        AuditLogService auditLogService,
        ILogger<ArchiveApiController> logger)
    {
        _context = context;
        _auditLogService = auditLogService;
        _logger = logger;
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
        var firmId = GetFirmId();
        var role = GetUserRole();

        var query = _context.Archives
            .Include(a => a.Document)
                .ThenInclude(d => d!.Uploader)
            .Include(a => a.Document)
                .ThenInclude(d => d!.Folder)
            .Include(a => a.ArchivedByUser)
            .Include(a => a.RestoredByUser)
            .Where(a => a.Document != null && a.Document.FirmID == firmId && a.IsRestored != true);

        // Filter by type
        switch (type?.ToLower())
        {
            case "archived":
                query = query.Where(a => a.ArchiveType == "Manual" || a.ArchiveType == "Version" || a.ArchiveType == "Retention");
                break;
            case "rejected":
                query = query.Where(a => a.ArchiveType == "Rejected");
                break;
            case "retention":
                query = query.Where(a => a.ArchiveType == "Retention");
                break;
            case "version":
                query = query.Where(a => a.ArchiveType == "Version");
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
                clientName = a.Document != null && a.Document.Uploader != null ? a.Document.Uploader.FullName : null,
                originalFolderName = a.Document != null && a.Document.Folder != null ? a.Document.Folder.FolderName : null,
                archiveType = a.ArchiveType,
                archiveReason = a.Reason,
                archivedAt = a.ArchivedDate,
                archivedByName = a.ArchivedByUser != null ? a.ArchivedByUser.FullName : null,
                scheduledDeleteDate = a.OriginalRetentionDate,
                versionNumber = a.VersionNumber,
                isRestored = a.IsRestored,
                restoredAt = a.RestoredAt,
                restoredByName = a.RestoredByUser != null ? a.RestoredByUser.FullName : null
            })
            .ToListAsync();

        return Ok(new { success = true, archives });
    }

    /// <summary>
    /// Get client's own archived documents
    /// </summary>
    [HttpGet("my-archives")]
    [Authorize(Policy = "ClientOnly")]
    public async Task<IActionResult> GetMyArchives()
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        var archives = await _context.Archives
            .Include(a => a.Document)
                .ThenInclude(d => d!.Folder)
            .Where(a => a.Document != null && a.Document.FirmID == firmId && a.Document.UploadedBy == userId && a.IsRestored != true)
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

    /// <summary>
    /// Get archive details
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetArchiveDetails(int id)
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
            .FirstOrDefaultAsync(a => a.ArchiveID == id && a.Document != null && a.Document.FirmID == firmId);

        if (archive == null)
            return NotFound(new { success = false, message = "Archive not found" });

        // Check permissions for clients
        if (role == "Client" && archive.Document?.UploadedBy != userId)
            return Forbid();

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
                archiveType = archive.ArchiveType,
                archiveReason = archive.Reason,
                archivedAt = archive.ArchivedDate,
                archivedByName = archive.ArchivedByUser?.FullName,
                originalRetentionDate = archive.OriginalRetentionDate,
                isRestored = archive.IsRestored,
                restoredAt = archive.RestoredAt,
                versions = archive.Document?.Versions.Select(v => new
                {
                    versionId = v.VersionId,
                    versionNumber = v.VersionNumber,
                    originalFileName = v.OriginalFileName,
                    fileSize = v.FileSize,
                    createdAt = v.CreatedAt
                })
            }
        });
    }

    /// <summary>
    /// Restore an archived document
    /// </summary>
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> RestoreArchive(int id, [FromBody] RestoreArchiveDto? dto = null)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();
        var role = GetUserRole();

        var archive = await _context.Archives
            .Include(a => a.Document)
            .FirstOrDefaultAsync(a => a.ArchiveID == id && a.Document != null && a.Document.FirmID == firmId);

        if (archive == null)
            return NotFound(new { success = false, message = "Archive not found" });

        // Check permissions
        if (role == "Client" && archive.Document?.UploadedBy != userId)
            return Forbid();

        if (archive.Document == null)
            return BadRequest(new { success = false, message = "Associated document not found" });

        // Restore the document to Approved status
        archive.Document.Status = "Completed";
        archive.Document.WorkflowStage = "Completed";
        archive.Document.UpdatedAt = DateTime.UtcNow;

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
                    .AddYears(existingRetention.RetentionYears ?? 0)
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

        return Ok(new { 
            success = true, 
            message = "Document restored successfully", 
            documentId = archive.DocumentID,
            newRetentionExpiry = archive.Document.RetentionExpiryDate
        });
    }

    /// <summary>
    /// Archive a rejected document
    /// </summary>
    [HttpPost("archive-rejected/{documentId}")]
    [Authorize(Policy = "AdminOrStaff")]
    public async Task<IActionResult> ArchiveRejectedDocument(int documentId, [FromBody] ArchiveReasonDto? dto = null)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.FirmID == firmId);

        if (document == null)
            return NotFound(new { success = false, message = "Document not found" });

        if (document.Status != "Rejected")
            return BadRequest(new { success = false, message = "Only rejected documents can be archived this way" });

        // Check if already archived
        var existingArchive = await _context.Archives
            .FirstOrDefaultAsync(a => a.DocumentID == documentId && a.IsRestored != true);

        if (existingArchive != null)
            return BadRequest(new { success = false, message = "Document is already archived" });

        var archive = new Archive
        {
            DocumentID = documentId,
            ArchivedDate = DateTime.UtcNow,
            Reason = dto?.Reason ?? "Rejected document archived",
            ArchiveType = "Rejected",
            ArchivedBy = userId,
            IsRestored = false
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

    /// <summary>
    /// Permanently delete an archived document (Admin only)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteArchive(int id)
    {
        var firmId = GetFirmId();

        var archive = await _context.Archives
            .Include(a => a.Document)
                .ThenInclude(d => d!.Versions)
            .FirstOrDefaultAsync(a => a.ArchiveID == id && a.Document != null && a.Document.FirmID == firmId);

        if (archive == null)
            return NotFound(new { success = false, message = "Archive not found" });

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

        // Delete from database (cascade should handle related records)
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

    /// <summary>
    /// Get archive statistics
    /// </summary>
    [HttpGet("stats")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetStats()
    {
        var firmId = GetFirmId();
        var now = DateTime.UtcNow;

        var stats = new
        {
            totalArchived = await _context.Archives
                .Where(a => a.Document != null && a.Document.FirmID == firmId && 
                    (a.ArchiveType == "Manual" || a.ArchiveType == "Version" || a.ArchiveType == "Retention") &&
                    a.IsRestored != true)
                .CountAsync(),
            totalRejected = await _context.Archives
                .Where(a => a.Document != null && a.Document.FirmID == firmId && 
                    a.ArchiveType == "Rejected" && a.IsRestored != true)
                .CountAsync(),
            totalRestored = await _context.Archives
                .Where(a => a.Document != null && a.Document.FirmID == firmId && a.IsRestored == true)
                .CountAsync(),
            pendingDeletion = await _context.Archives
                .Where(a => a.Document != null && a.Document.FirmID == firmId && 
                    a.OriginalRetentionDate != null && a.OriginalRetentionDate <= now.AddDays(30) &&
                    a.IsRestored != true)
                .CountAsync(),
            archivedThisMonth = await _context.Archives
                .Where(a => a.Document != null && a.Document.FirmID == firmId && 
                    a.ArchivedDate >= now.AddDays(-30) && a.IsRestored != true)
                .CountAsync()
        };

        return Ok(new { success = true, stats });
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
