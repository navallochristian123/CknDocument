using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
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
            .Where(a => a.Document != null && a.Document.FirmID == firmId);

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
                archivedByName = (string?)null, // Simplified - no navigation for now
                scheduledDeleteDate = a.OriginalRetentionDate, // Use retention date
                versionNumber = a.VersionNumber
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
            .Where(a => a.Document != null && a.Document.FirmID == firmId && a.Document.UploadedBy == userId)
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
    /// Restore an archived document
    /// </summary>
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> RestoreArchive(int id)
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

        // Restore the document
        archive.Document.Status = "Pending";
        archive.Document.WorkflowStage = "ClientUpload";
        archive.Document.UpdatedAt = DateTime.UtcNow;

        // Remove the archive record
        _context.Archives.Remove(archive);
        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "ArchiveRestore",
            "Archive",
            id,
            $"Restored document from archive: {archive.Document.Title}",
            null,
            null,
            "ArchiveOperation");

        return Ok(new { success = true, message = "Document restored successfully", documentId = archive.DocumentID });
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

        var stats = new
        {
            totalArchived = await _context.Archives
                .Where(a => a.Document != null && a.Document.FirmID == firmId && (a.ArchiveType == "Manual" || a.ArchiveType == "Version"))
                .CountAsync(),
            totalRejected = await _context.Archives
                .Where(a => a.Document != null && a.Document.FirmID == firmId && a.ArchiveType == "Rejected")
                .CountAsync(),
            pendingDeletion = await _context.Archives
                .Where(a => a.Document != null && a.Document.FirmID == firmId && a.OriginalRetentionDate != null && a.OriginalRetentionDate <= DateTime.UtcNow.AddDays(30))
                .CountAsync(),
            archivedThisMonth = await _context.Archives
                .Where(a => a.Document != null && a.Document.FirmID == firmId && a.ArchivedDate >= DateTime.UtcNow.AddDays(-30))
                .CountAsync()
        };

        return Ok(new { success = true, stats });
    }
}
