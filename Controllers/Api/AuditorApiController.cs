using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Services;
using System.Security.Claims;

namespace CKNDocument.Controllers.Api;

/// <summary>
/// API Controller for Auditor-specific operations
/// Provides read-only access to all firm data for compliance and audit purposes
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AuditorOnly")]
public class AuditorApiController : ControllerBase
{
    private readonly LawFirmDMSDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<AuditorApiController> _logger;

    public AuditorApiController(
        LawFirmDMSDbContext context,
        IWebHostEnvironment environment,
        ILogger<AuditorApiController> logger)
    {
        _context = context;
        _environment = environment;
        _logger = logger;
    }

    private int GetCurrentUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    private int GetFirmId() => int.Parse(User.FindFirst("FirmId")?.Value ?? "0");

    /// <summary>
    /// Get firm ID with fallback to user's DB record if claim is missing
    /// </summary>
    private async Task<int> GetFirmIdAsync()
    {
        var firmId = GetFirmId();
        if (firmId > 0) return firmId;

        // Fallback: look up firm from user record
        var userId = GetCurrentUserId();
        if (userId > 0)
        {
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserID == userId);
            if (user != null && user.FirmID > 0) return user.FirmID;
        }
        return 0;
    }

    /// <summary>
    /// Build the firm-scoped audit log query
    /// </summary>
    private async Task<(IQueryable<CKNDocument.Models.LawFirmDMS.AuditLog> query, List<int> firmUserIds)> BuildFirmAuditQuery(int firmId)
    {
        var firmUserIds = firmId > 0
            ? await _context.Users.Where(u => u.FirmID == firmId).Select(u => u.UserID).ToListAsync()
            : new List<int>();

        var query = _context.AuditLogs
            .Include(a => a.User)
            .AsNoTracking()
            .AsQueryable();

        if (firmId > 0)
        {
            query = query.Where(a => a.FirmID == firmId 
                || (a.FirmID == null && a.UserID != null && firmUserIds.Contains(a.UserID.Value)));
        }
        else
        {
            // No firm context — return nothing (safety guard)
            query = query.Where(a => false);
        }

        return (query, firmUserIds);
    }

    // ===== DASHBOARD =====

    /// <summary>
    /// Get real-time dashboard statistics for the auditor
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        try
        {
            var firmId = await GetFirmIdAsync();
            _logger.LogInformation("Auditor stats requested. FirmId={FirmId}, UserId={UserId}", firmId, GetCurrentUserId());

            var totalDocs = await _context.Documents
                .CountAsync(d => d.FirmID == firmId);

            var approvedDocs = await _context.Documents
                .CountAsync(d => d.FirmID == firmId && d.Status == "Completed");

            var pendingDocs = await _context.Documents
                .CountAsync(d => d.FirmID == firmId && d.Status == "Pending");

            var rejectedDocs = await _context.Documents
                .CountAsync(d => d.FirmID == firmId && d.Status == "Rejected");

            var archivedDocs = await _context.Archives
                .CountAsync(a => a.FirmId == firmId && a.IsRestored != true);

            var totalVersions = await _context.DocumentVersions
                .CountAsync(v => v.Document != null && v.Document.FirmID == firmId);

            // Use shared query builder for audit logs
            var (auditQuery, firmUserIds) = await BuildFirmAuditQuery(firmId);

            var auditLogCount = await auditQuery.CountAsync();

            // Recent activity (last 7 days)
            var since = DateTime.UtcNow.AddDays(-7);
            var recentActivity = await auditQuery
                .Where(a => a.Timestamp >= since)
                .CountAsync();

            // Unauthorized/security events
            var unauthorizedCount = await auditQuery
                .Where(a => a.ActionCategory == "Security" || a.Action.Contains("Failed") || a.Action.Contains("Unauthorized") || a.Action.Contains("Locked"))
                .CountAsync();

            return Ok(new
            {
                success = true,
                stats = new
                {
                    totalDocuments = totalDocs,
                    approvedDocuments = approvedDocs,
                    pendingDocuments = pendingDocs,
                    rejectedDocuments = rejectedDocs,
                    archivedDocuments = archivedDocs,
                    totalVersions,
                    auditLogCount,
                    recentActivity,
                    unauthorizedEvents = unauthorizedCount
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting auditor stats");
            return StatusCode(500, new { success = false, message = "Error loading stats" });
        }
    }

    // ===== DOCUMENTS =====

    /// <summary>
    /// Get all firm documents (all statuses) - read only for auditor
    /// </summary>
    [HttpGet("documents")]
    public async Task<IActionResult> GetAllDocuments(
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? documentType = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            var firmId = await GetFirmIdAsync();

            var query = _context.Documents
                .Include(d => d.Uploader)
                .Include(d => d.Folder)
                .Include(d => d.Versions)
                .Where(d => d.FirmID == firmId)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.ToLower();
                query = query.Where(d =>
                    (d.Title != null && d.Title.ToLower().Contains(s)) ||
                    (d.OriginalFileName != null && d.OriginalFileName.ToLower().Contains(s)) ||
                    (d.DocumentType != null && d.DocumentType.ToLower().Contains(s)) ||
                    (d.Uploader != null && d.Uploader.FirstName != null && d.Uploader.FirstName.ToLower().Contains(s)) ||
                    (d.Uploader != null && d.Uploader.LastName != null && d.Uploader.LastName.ToLower().Contains(s)) ||
                    (d.Uploader != null && d.Uploader.Email != null && d.Uploader.Email.ToLower().Contains(s)));
            }

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(d => d.Status == status);

            if (!string.IsNullOrWhiteSpace(documentType))
                query = query.Where(d => d.DocumentType == documentType);

            var totalCount = await query.CountAsync();

            var documents = await query
                .OrderByDescending(d => d.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(d => new
                {
                    documentId = d.DocumentID,
                    title = d.Title,
                    originalFileName = d.OriginalFileName,
                    fileExtension = d.FileExtension,
                    documentType = d.DocumentType,
                    status = d.Status,
                    workflowStage = d.WorkflowStage,
                    clientName = d.Uploader != null ? (d.Uploader.FirstName ?? "") + " " + (d.Uploader.LastName ?? "") : "Unknown",
                    uploadedBy = d.Uploader != null ? (d.Uploader.FirstName ?? "") + " " + (d.Uploader.LastName ?? "") : "Unknown",
                    uploaderEmail = d.Uploader != null ? d.Uploader.Email : null,
                    folderName = d.Folder != null ? d.Folder.FolderName : null,
                    totalFileSize = d.TotalFileSize,
                    currentVersion = d.CurrentVersion,
                    versionCount = d.Versions.Count,
                    uploadedAt = d.CreatedAt,
                    createdAt = d.CreatedAt,
                    approvedAt = d.ApprovedAt,
                    isHighRisk = d.IsHighRisk,
                    isDuplicate = d.IsDuplicate,
                    createdBy = d.CreatedBy
                })
                .ToListAsync();

            return Ok(new { success = true, documents, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)pageSize), currentPage = page });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting auditor documents");
            return StatusCode(500, new { success = false, message = "Error loading documents" });
        }
    }

    // ===== COMPLIANCE =====

    /// <summary>
    /// Get full version timeline for all documents
    /// </summary>
    [HttpGet("compliance/version-timeline")]
    public async Task<IActionResult> GetVersionTimeline(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            var firmId = await GetFirmIdAsync();

            var query = _context.DocumentVersions
                .Include(v => v.Document)
                    .ThenInclude(d => d!.Uploader)
                .Include(v => v.Uploader)
                .Where(v => v.Document != null && v.Document.FirmID == firmId)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.ToLower();
                query = query.Where(v =>
                    (v.Document != null && v.Document.Title != null && v.Document.Title.ToLower().Contains(s)) ||
                    (v.OriginalFileName != null && v.OriginalFileName.ToLower().Contains(s)));
            }

            var totalCount = await query.CountAsync();

            var versions = await query
                .OrderByDescending(v => v.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(v => new
                {
                    versionId = v.VersionId,
                    documentId = v.DocumentId,
                    documentTitle = v.Document != null ? v.Document.Title : "Unknown",
                    documentStatus = v.Document != null ? v.Document.Status : null,
                    versionNumber = v.VersionNumber,
                    originalFileName = v.OriginalFileName,
                    fileExtension = v.FileExtension,
                    fileSize = v.FileSize,
                    changeDescription = v.ChangeDescription,
                    changedBy = v.ChangedBy,
                    uploaderName = v.Uploader != null ? (v.Uploader.FirstName ?? "") + " " + (v.Uploader.LastName ?? "") : v.ChangedBy,
                    isCurrentVersion = v.IsCurrentVersion,
                    fileHash = v.FileHash,
                    createdAt = v.CreatedAt
                })
                .ToListAsync();

            return Ok(new { success = true, versions, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)pageSize), currentPage = page });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting version timeline");
            return StatusCode(500, new { success = false, message = "Error loading version timeline" });
        }
    }

    /// <summary>
    /// Get unauthorized/suspicious actions from audit log
    /// </summary>
    [HttpGet("compliance/unauthorized-actions")]
    public async Task<IActionResult> GetUnauthorizedActions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            var firmId = await GetFirmIdAsync();

            var suspiciousActions = new[] {
                "LoginFailed", "AccountLocked", "UnauthorizedAccess",
                "PasswordResetFailed", "InvalidToken", "SessionExpired",
                "PermissionDenied", "ForbiddenAccess"
            };

            var (baseQuery, firmUserIds) = await BuildFirmAuditQuery(firmId);

            var query = baseQuery
                .Where(a =>
                    a.ActionCategory == "Security" ||
                     a.Action.Contains("Failed") ||
                     a.Action.Contains("Unauthorized") ||
                     a.Action.Contains("Locked") ||
                     a.Action.Contains("Invalid") ||
                     a.Action.Contains("Rejected") ||
                     suspiciousActions.Contains(a.Action));

            var totalCount = await query.CountAsync();

            var logs = await query
                .OrderByDescending(a => a.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new
                {
                    auditId = a.AuditID,
                    action = a.Action,
                    description = a.Description,
                    userName = a.User != null ? (a.User.FirstName ?? "") + " " + (a.User.LastName ?? "") : "Unknown",
                    userEmail = a.User != null ? a.User.Email : null,
                    ipAddress = a.IPAddress,
                    actionCategory = a.ActionCategory,
                    entityType = a.EntityType,
                    entityId = a.EntityID,
                    timestamp = a.Timestamp
                })
                .ToListAsync();

            return Ok(new { success = true, logs, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)pageSize), currentPage = page });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unauthorized actions");
            return StatusCode(500, new { success = false, message = "Error loading unauthorized actions" });
        }
    }

    /// <summary>
    /// Get archived/deleted documents (retention-expired, archived by admin, etc.)
    /// </summary>
    [HttpGet("compliance/archived-deleted")]
    public async Task<IActionResult> GetArchivedDeleted(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            var firmId = await GetFirmIdAsync();

            var query = _context.Archives
                .Include(a => a.Document)
                    .ThenInclude(d => d!.Uploader)
                .Include(a => a.ArchivedByUser)
                .Include(a => a.RestoredByUser)
                .Where(a => a.FirmId == firmId)
                .AsNoTracking();

            var totalCount = await query.CountAsync();

            var archives = await query
                .OrderByDescending(a => a.ArchivedDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new
                {
                    archiveId = a.ArchiveID,
                    documentId = a.DocumentID,
                    documentTitle = a.Document != null ? a.Document.Title : "Unknown",
                    originalFileName = a.Document != null ? a.Document.OriginalFileName : null,
                    fileExtension = a.Document != null ? a.Document.FileExtension : null,
                    documentType = a.Document != null ? a.Document.DocumentType : null,
                    uploaderName = a.Document != null && a.Document.Uploader != null
                        ? (a.Document.Uploader.FirstName ?? "") + " " + (a.Document.Uploader.LastName ?? "")
                        : "Unknown",
                    archiveType = a.ArchiveType,
                    archiveReason = a.Reason,
                    originalStatus = a.OriginalStatus,
                    archivedAt = a.ArchivedDate,
                    archivedByName = a.ArchivedByUser != null
                        ? (a.ArchivedByUser.FirstName ?? "") + " " + (a.ArchivedByUser.LastName ?? "")
                        : "System",
                    isRestored = a.IsRestored,
                    restoredAt = a.RestoredAt,
                    restoredByName = a.RestoredByUser != null
                        ? (a.RestoredByUser.FirstName ?? "") + " " + (a.RestoredByUser.LastName ?? "")
                        : null,
                    scheduledDeleteDate = a.ScheduledDeleteDate
                })
                .ToListAsync();

            return Ok(new { success = true, archives, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)pageSize), currentPage = page });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting archived/deleted docs");
            return StatusCode(500, new { success = false, message = "Error loading archived documents" });
        }
    }

    /// <summary>
    /// Get documents with missing physical files on disk
    /// </summary>
    [HttpGet("compliance/missing-files")]
    public async Task<IActionResult> GetMissingFiles()
    {
        try
        {
            var firmId = await GetFirmIdAsync();
            var uploadsRoot = Path.Combine(_environment.ContentRootPath, "Uploads");

            var versions = await _context.DocumentVersions
                .Include(v => v.Document)
                    .ThenInclude(d => d!.Uploader)
                .Where(v => v.Document != null && v.Document.FirmID == firmId && v.FilePath != null)
                .AsNoTracking()
                .Select(v => new
                {
                    versionId = v.VersionId,
                    documentId = v.DocumentId,
                    documentTitle = v.Document != null ? v.Document.Title : "Unknown",
                    documentStatus = v.Document != null ? v.Document.Status : null,
                    versionNumber = v.VersionNumber,
                    originalFileName = v.OriginalFileName,
                    filePath = v.FilePath,
                    uploaderName = v.Document != null && v.Document.Uploader != null
                        ? (v.Document.Uploader.FirstName ?? "") + " " + (v.Document.Uploader.LastName ?? "")
                        : "Unknown",
                    createdAt = v.CreatedAt
                })
                .ToListAsync();

            // Check which ones are missing on disk
            var missing = new List<object>();
            foreach (var v in versions)
            {
                if (string.IsNullOrEmpty(v.filePath)) continue;
                bool exists = System.IO.File.Exists(v.filePath);
                if (!exists)
                {
                    // Try content-root remap
                    var fileName = Path.GetFileName(v.filePath);
                    var uploadsIdx = v.filePath.Replace('\\', '/').IndexOf("/Uploads/", StringComparison.OrdinalIgnoreCase);
                    if (uploadsIdx >= 0)
                    {
                        var relPath = v.filePath.Substring(uploadsIdx + 1).Replace('/', Path.DirectorySeparatorChar);
                        var remapped = Path.Combine(_environment.ContentRootPath, relPath);
                        exists = System.IO.File.Exists(remapped);
                    }
                    if (!exists && !string.IsNullOrEmpty(fileName))
                    {
                        var found = Directory.Exists(uploadsRoot)
                            ? Directory.GetFiles(uploadsRoot, fileName, SearchOption.AllDirectories).FirstOrDefault()
                            : null;
                        exists = found != null;
                    }
                    if (!exists)
                    {
                        missing.Add(new
                        {
                            v.versionId,
                            v.documentId,
                            v.documentTitle,
                            v.documentStatus,
                            v.versionNumber,
                            v.originalFileName,
                            v.uploaderName,
                            storedPath = v.filePath,
                            v.createdAt
                        });
                    }
                }
            }

            return Ok(new { success = true, missingFiles = missing, totalMissing = missing.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking missing files");
            return StatusCode(500, new { success = false, message = "Error checking missing files" });
        }
    }

    /// <summary>
    /// Get all audit logs for the firm (for auditor)
    /// </summary>
    [HttpGet("audit-logs")]
    public async Task<IActionResult> GetAuditLogs(
        [FromQuery] string? search = null,
        [FromQuery] string? action = null,
        [FromQuery] string? category = null,
        [FromQuery] int? userId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            var firmId = await GetFirmIdAsync();
            _logger.LogInformation("Auditor audit-logs requested. FirmId={FirmId}, UserId={UserId}, Page={Page}", firmId, GetCurrentUserId(), page);

            var (query, firmUserIds) = await BuildFirmAuditQuery(firmId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.ToLower();
                query = query.Where(a =>
                    (a.Description != null && a.Description.ToLower().Contains(s)) ||
                    (a.Action != null && a.Action.ToLower().Contains(s)));
            }

            if (!string.IsNullOrWhiteSpace(action))
                query = query.Where(a => a.Action == action);

            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(a => a.ActionCategory == category);

            if (userId.HasValue)
                query = query.Where(a => a.UserID == userId);

            if (startDate.HasValue)
                query = query.Where(a => a.Timestamp >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(a => a.Timestamp <= endDate.Value.AddDays(1));

            var totalCount = await query.CountAsync();

            var logs = await query
                .OrderByDescending(a => a.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new
                {
                    auditId = a.AuditID,
                    action = a.Action,
                    description = a.Description,
                    entityType = a.EntityType,
                    entityId = a.EntityID,
                    userName = a.User != null ? (a.User.FirstName ?? "") + " " + (a.User.LastName ?? "") : "System",
                    userEmail = a.User != null ? a.User.Email : null,
                    ipAddress = a.IPAddress,
                    actionCategory = a.ActionCategory,
                    timestamp = a.Timestamp
                })
                .ToListAsync();

            return Ok(new { success = true, logs, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)pageSize), currentPage = page });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting audit logs for auditor");
            return StatusCode(500, new { success = false, message = "Error loading audit logs" });
        }
    }
}
