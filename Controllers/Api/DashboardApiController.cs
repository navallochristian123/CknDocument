using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using System.Security.Claims;

namespace CKNDocument.Controllers.Api;

/// <summary>
/// API Controller for Dashboard data
/// Provides real-time statistics and recent activity for all roles
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "FirmMember")]
public class DashboardApiController : ControllerBase
{
    private readonly LawFirmDMSDbContext _context;
    private readonly ILogger<DashboardApiController> _logger;

    public DashboardApiController(
        LawFirmDMSDbContext context,
        ILogger<DashboardApiController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private int GetCurrentUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    private int GetFirmId() => int.Parse(User.FindFirst("FirmId")?.Value ?? "0");
    private string GetUserRole() => User.FindFirst(ClaimTypes.Role)?.Value ?? "Client";

    /// <summary>
    /// Get dashboard statistics for Client
    /// </summary>
    [HttpGet("client-stats")]
    [Authorize(Policy = "ClientOnly")]
    public async Task<IActionResult> GetClientStats()
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        // Get archived document IDs to exclude
        var archivedDocIds = await _context.Archives
            .Where(a => a.IsRestored != true)
            .Select(a => a.DocumentID)
            .ToListAsync();

        var myDocuments = await _context.Documents
            .Where(d => d.UploadedBy == userId && d.FirmID == firmId && !archivedDocIds.Contains(d.DocumentID))
            .CountAsync();

        var approved = await _context.Documents
            .Where(d => d.UploadedBy == userId && d.FirmID == firmId && d.Status == "Completed")
            .CountAsync();

        var pending = await _context.Documents
            .Where(d => d.UploadedBy == userId && d.FirmID == firmId && 
                   d.Status != "Completed" && d.Status != "Rejected" && 
                   !archivedDocIds.Contains(d.DocumentID))
            .CountAsync();

        var rejected = await _context.Documents
            .Where(d => d.UploadedBy == userId && d.FirmID == firmId && d.Status == "Rejected")
            .CountAsync();

        return Ok(new
        {
            success = true,
            stats = new { myDocuments, approved, pending, rejected }
        });
    }

    /// <summary>
    /// Get recent documents for Client
    /// </summary>
    [HttpGet("client-recent")]
    [Authorize(Policy = "ClientOnly")]
    public async Task<IActionResult> GetClientRecentDocuments([FromQuery] int take = 10)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        // Get archived document IDs to exclude
        var archivedDocIds = await _context.Archives
            .Where(a => a.IsRestored != true)
            .Select(a => a.DocumentID)
            .ToListAsync();

        var documents = await _context.Documents
            .Where(d => d.UploadedBy == userId && d.FirmID == firmId && !archivedDocIds.Contains(d.DocumentID))
            .OrderByDescending(d => d.CreatedAt)
            .Take(take)
            .Select(d => new
            {
                id = d.DocumentID,
                title = d.Title,
                originalFileName = d.OriginalFileName,
                fileExtension = d.FileExtension,
                documentType = d.DocumentType,
                category = d.Category,
                status = d.Status,
                workflowStage = d.WorkflowStage,
                createdAt = d.CreatedAt,
                updatedAt = d.UpdatedAt
            })
            .ToListAsync();

        return Ok(new { success = true, documents });
    }

    /// <summary>
    /// Get recent activity for Client
    /// </summary>
    [HttpGet("client-activity")]
    [Authorize(Policy = "ClientOnly")]
    public async Task<IActionResult> GetClientActivity([FromQuery] int take = 10)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        // Get recent audit logs for this user's documents
        var docIds = await _context.Documents
            .Where(d => d.UploadedBy == userId && d.FirmID == firmId)
            .Select(d => d.DocumentID)
            .ToListAsync();

        var activities = await _context.AuditLogs
            .Where(a => a.FirmID == firmId && 
                       (a.UserID == userId || (a.EntityType == "Document" && a.EntityID != null && docIds.Contains(a.EntityID.Value))))
            .OrderByDescending(a => a.Timestamp)
            .Take(take)
            .Select(a => new
            {
                id = a.AuditID,
                action = a.Action,
                entityType = a.EntityType,
                description = a.Description,
                timestamp = a.Timestamp
            })
            .ToListAsync();

        return Ok(new { success = true, activities });
    }

    /// <summary>
    /// Get dashboard statistics for Staff
    /// </summary>
    [HttpGet("staff-stats")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> GetStaffStats()
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        // Get archived document IDs to exclude
        var archivedDocIds = await _context.Archives
            .Where(a => a.IsRestored != true)
            .Select(a => a.DocumentID)
            .ToListAsync();

        var totalDocuments = await _context.Documents
            .Where(d => d.FirmID == firmId && !archivedDocIds.Contains(d.DocumentID))
            .CountAsync();

        var assignedToMe = await _context.Documents
            .Where(d => d.FirmID == firmId && d.AssignedStaffId == userId && !archivedDocIds.Contains(d.DocumentID))
            .CountAsync();

        var pendingReviews = await _context.Documents
            .Where(d => d.FirmID == firmId && d.WorkflowStage == "StaffReview" && !archivedDocIds.Contains(d.DocumentID))
            .CountAsync();

        var completedToday = await _context.DocumentReviews
            .Where(r => r.Document != null && r.Document.FirmID == firmId && 
                       r.ReviewedBy == userId && 
                       r.ReviewedAt != null && r.ReviewedAt.Value.Date == DateTime.UtcNow.Date)
            .CountAsync();

        return Ok(new
        {
            success = true,
            stats = new { totalDocuments, assignedToMe, pendingReviews, completedToday }
        });
    }

    /// <summary>
    /// Get recent documents for Staff
    /// </summary>
    [HttpGet("staff-recent")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> GetStaffRecentDocuments([FromQuery] int take = 10)
    {
        var firmId = GetFirmId();

        // Get archived document IDs to exclude
        var archivedDocIds = await _context.Archives
            .Where(a => a.IsRestored != true)
            .Select(a => a.DocumentID)
            .ToListAsync();

        var documents = await _context.Documents
            .Include(d => d.Uploader)
            .Include(d => d.Folder)
            .Where(d => d.FirmID == firmId && !archivedDocIds.Contains(d.DocumentID))
            .OrderByDescending(d => d.CreatedAt)
            .Take(take)
            .Select(d => new
            {
                id = d.DocumentID,
                title = d.Title,
                originalFileName = d.OriginalFileName,
                fileExtension = d.FileExtension,
                documentType = d.DocumentType,
                clientName = d.Uploader != null ? d.Uploader.FullName : "Unknown",
                folderId = d.FolderId,
                folderName = d.Folder != null ? d.Folder.FolderName : null,
                status = d.Status,
                workflowStage = d.WorkflowStage,
                createdAt = d.CreatedAt
            })
            .ToListAsync();

        return Ok(new { success = true, documents });
    }

    /// <summary>
    /// Get staff's completed reviews (Task/Completed)
    /// </summary>
    [HttpGet("staff-completed")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> GetStaffCompletedReviews([FromQuery] int take = 50)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        var completed = await _context.DocumentReviews
            .Include(r => r.Document)
                .ThenInclude(d => d!.Uploader)
            .Where(r => r.ReviewedBy == userId && 
                       r.Document != null && r.Document.FirmID == firmId &&
                       r.ReviewedAt != null)
            .OrderByDescending(r => r.ReviewedAt)
            .Take(take)
            .Select(r => new
            {
                reviewId = r.ReviewId,
                documentId = r.DocumentId,
                documentTitle = r.Document != null ? r.Document.Title : "Unknown",
                originalFileName = r.Document != null ? r.Document.OriginalFileName : null,
                clientName = r.Document != null && r.Document.Uploader != null ? r.Document.Uploader.FullName : "Unknown",
                reviewStatus = r.ReviewStatus,
                reviewedAt = r.ReviewedAt,
                remarks = r.Remarks
            })
            .ToListAsync();

        return Ok(new { success = true, completed });
    }

    /// <summary>
    /// Get dashboard statistics for Admin
    /// </summary>
    [HttpGet("admin-stats")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetAdminStats()
    {
        var firmId = GetFirmId();

        // Get archived document IDs to exclude
        var archivedDocIds = await _context.Archives
            .Where(a => a.IsRestored != true)
            .Select(a => a.DocumentID)
            .ToListAsync();

        var totalDocuments = await _context.Documents
            .Where(d => d.FirmID == firmId && !archivedDocIds.Contains(d.DocumentID))
            .CountAsync();

        var activeUsers = await _context.Users
            .Where(u => u.FirmID == firmId && u.Status == "Active")
            .CountAsync();

        var pendingReviews = await _context.Documents
            .Where(d => d.FirmID == firmId && d.WorkflowStage == "AdminReview" && !archivedDocIds.Contains(d.DocumentID))
            .CountAsync();

        // Check retention table for expiring documents
        var expiringSoon = await _context.DocumentRetentions
            .Where(dr => dr.Document != null && dr.Document.FirmID == firmId && 
                        dr.ExpiryDate != null && dr.ExpiryDate <= DateTime.UtcNow.AddDays(30) && 
                        dr.IsArchived != true)
            .CountAsync();

        return Ok(new
        {
            success = true,
            stats = new { totalDocuments, activeUsers, pendingReviews, expiringSoon }
        });
    }

    /// <summary>
    /// Get recent documents for Admin
    /// </summary>
    [HttpGet("admin-recent")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetAdminRecentDocuments([FromQuery] int take = 10)
    {
        var firmId = GetFirmId();

        // Get archived document IDs to exclude
        var archivedDocIds = await _context.Archives
            .Where(a => a.IsRestored != true)
            .Select(a => a.DocumentID)
            .ToListAsync();

        var documents = await _context.Documents
            .Include(d => d.Uploader)
            .Include(d => d.Folder)
            .Where(d => d.FirmID == firmId && !archivedDocIds.Contains(d.DocumentID))
            .OrderByDescending(d => d.CreatedAt)
            .Take(take)
            .Select(d => new
            {
                id = d.DocumentID,
                title = d.Title,
                originalFileName = d.OriginalFileName,
                fileExtension = d.FileExtension,
                documentType = d.DocumentType,
                uploadedBy = d.Uploader != null ? d.Uploader.FullName : "Unknown",
                folderId = d.FolderId,
                folderName = d.Folder != null ? d.Folder.FolderName : null,
                status = d.Status,
                workflowStage = d.WorkflowStage,
                createdAt = d.CreatedAt
            })
            .ToListAsync();

        return Ok(new { success = true, documents });
    }

    /// <summary>
    /// Get admin's completed reviews (Task/Completed)
    /// </summary>
    [HttpGet("admin-completed")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetAdminCompletedReviews([FromQuery] int take = 50)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        var completed = await _context.DocumentReviews
            .Include(r => r.Document)
                .ThenInclude(d => d!.Uploader)
            .Include(r => r.Reviewer)
            .Where(r => r.Document != null && r.Document.FirmID == firmId &&
                       r.ReviewedAt != null && r.ReviewerRole == "Admin")
            .OrderByDescending(r => r.ReviewedAt)
            .Take(take)
            .Select(r => new
            {
                reviewId = r.ReviewId,
                documentId = r.DocumentId,
                documentTitle = r.Document != null ? r.Document.Title : "Unknown",
                originalFileName = r.Document != null ? r.Document.OriginalFileName : null,
                clientName = r.Document != null && r.Document.Uploader != null ? r.Document.Uploader.FullName : "Unknown",
                reviewerName = r.Reviewer != null ? r.Reviewer.FullName : "Unknown",
                reviewStatus = r.ReviewStatus,
                reviewedAt = r.ReviewedAt,
                remarks = r.Remarks
            })
            .ToListAsync();

        return Ok(new { success = true, completed });
    }

    /// <summary>
    /// Get recent activity for Admin
    /// </summary>
    [HttpGet("admin-activity")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetAdminActivity([FromQuery] int take = 10)
    {
        var firmId = GetFirmId();

        var activities = await _context.AuditLogs
            .Include(a => a.User)
            .Where(a => a.FirmID == firmId)
            .OrderByDescending(a => a.Timestamp)
            .Take(take)
            .Select(a => new
            {
                id = a.AuditID,
                action = a.Action,
                entityType = a.EntityType,
                description = a.Description,
                userName = a.User != null ? a.User.FullName : "System",
                timestamp = a.Timestamp
            })
            .ToListAsync();

        return Ok(new { success = true, activity = activities });
    }

    // ===========================================
    // LAWYER DASHBOARD ENDPOINTS
    // ===========================================

    /// <summary>
    /// Get dashboard statistics for Lawyer
    /// </summary>
    [HttpGet("lawyer-stats")]
    [Authorize(Policy = "LawyerOnly")]
    public async Task<IActionResult> GetLawyerStats()
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        // Get archived document IDs to exclude
        var archivedDocIds = await _context.Archives
            .Where(a => a.IsRestored != true)
            .Select(a => a.DocumentID)
            .ToListAsync();

        var totalDocuments = await _context.Documents
            .Where(d => d.FirmID == firmId && !archivedDocIds.Contains(d.DocumentID))
            .CountAsync();

        var assignedToMe = await _context.Documents
            .Where(d => d.FirmID == firmId && 
                       d.AssignedLawyerId == userId && 
                       !archivedDocIds.Contains(d.DocumentID))
            .CountAsync();

        var pendingReviews = await _context.Documents
            .Where(d => d.FirmID == firmId && 
                       (d.WorkflowStage == "PendingLawyerReview" || d.WorkflowStage == "LawyerReview") &&
                       !archivedDocIds.Contains(d.DocumentID))
            .CountAsync();

        var completedToday = await _context.DocumentReviews
            .Where(r => r.Document != null && r.Document.FirmID == firmId && 
                       r.ReviewedBy == userId && 
                       r.ReviewerRole == "Lawyer" &&
                       r.ReviewedAt != null && r.ReviewedAt.Value.Date == DateTime.UtcNow.Date)
            .CountAsync();

        return Ok(new
        {
            success = true,
            stats = new { totalDocuments, assignedToMe, pendingReviews, completedToday }
        });
    }

    /// <summary>
    /// Get recent documents for Lawyer review
    /// </summary>
    [HttpGet("lawyer-recent")]
    [Authorize(Policy = "LawyerOnly")]
    public async Task<IActionResult> GetLawyerRecentDocuments([FromQuery] int take = 10)
    {
        var firmId = GetFirmId();
        var userId = GetCurrentUserId();

        // Get archived document IDs to exclude
        var archivedDocIds = await _context.Archives
            .Where(a => a.IsRestored != true)
            .Select(a => a.DocumentID)
            .ToListAsync();

        var documents = await _context.Documents
            .Include(d => d.Uploader)
            .Include(d => d.AssignedLawyer)
            .Where(d => d.FirmID == firmId && 
                       !archivedDocIds.Contains(d.DocumentID) &&
                       (d.WorkflowStage == "PendingLawyerReview" || 
                        d.WorkflowStage == "LawyerReview" ||
                        d.AssignedLawyerId == userId))
            .OrderByDescending(d => d.CreatedAt)
            .Take(take)
            .Select(d => new
            {
                id = d.DocumentID,
                title = d.Title,
                originalFileName = d.OriginalFileName,
                fileExtension = d.FileExtension,
                documentType = d.DocumentType,
                status = d.Status,
                workflowStage = d.WorkflowStage,
                clientName = d.Uploader != null ? d.Uploader.FullName : null,
                createdAt = d.CreatedAt,
                assignedLawyerId = d.AssignedLawyerId,
                isAssignedToMe = d.AssignedLawyerId == userId,
                folderId = d.FolderId,
                folderName = d.Folder != null ? d.Folder.FolderName : null
            })
            .ToListAsync();

        return Ok(new { success = true, documents, currentUserId = userId });
    }

    /// <summary>
    /// Get completed reviews by Lawyer
    /// </summary>
    [HttpGet("lawyer-completed")]
    [Authorize(Policy = "LawyerOnly")]
    public async Task<IActionResult> GetLawyerCompletedReviews([FromQuery] int take = 20)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        var completed = await _context.DocumentReviews
            .Include(r => r.Document)
            .ThenInclude(d => d!.Uploader)
            .Where(r => r.ReviewedBy == userId && 
                       r.ReviewerRole == "Lawyer" &&
                       r.Document != null && r.Document.FirmID == firmId)
            .OrderByDescending(r => r.ReviewedAt)
            .Take(take)
            .Select(r => new
            {
                documentId = r.DocumentId,
                documentTitle = r.Document != null ? r.Document.Title : null,
                originalFileName = r.Document != null ? r.Document.OriginalFileName : null,
                clientName = r.Document != null && r.Document.Uploader != null ? r.Document.Uploader.FullName : null,
                reviewStatus = r.ReviewStatus,
                reviewedAt = r.ReviewedAt,
                remarks = r.Remarks
            })
            .ToListAsync();

        return Ok(new { success = true, completed });
    }

    // ===========================================
    // UNIVERSAL AUDIT LOG ENDPOINT (ALL ROLES)
    // ===========================================

    /// <summary>
    /// Get audit logs for any authenticated firm member.
    /// Admin/Auditor see all firm logs. Other roles see only their own logs.
    /// Supports filtering, pagination, and real-time refresh.
    /// </summary>
    [HttpGet("audit-logs")]
    public async Task<IActionResult> GetAuditLogs(
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
            var firmId = GetFirmId();
            var role = GetUserRole();
            var currentUserId = GetCurrentUserId();

            var query = _context.AuditLogs
                .Include(a => a.User)
                .AsNoTracking()
                .AsQueryable();

            // Firm filter
            if (firmId > 0)
            {
                query = query.Where(a => a.FirmID == firmId || a.FirmID == null);
            }

            // Role-based access: Admin/Auditor see all firm logs, others see only their own
            if (role != "Admin" && role != "Auditor")
            {
                query = query.Where(a => a.UserID == currentUserId);
            }
            else if (userId.HasValue)
            {
                query = query.Where(a => a.UserID == userId);
            }

            // Apply filters
            if (!string.IsNullOrEmpty(action))
                query = query.Where(a => a.Action == action);
            if (!string.IsNullOrEmpty(category))
                query = query.Where(a => a.ActionCategory == category);
            if (startDate.HasValue)
                query = query.Where(a => a.Timestamp >= startDate.Value);
            if (endDate.HasValue)
                query = query.Where(a => a.Timestamp <= endDate.Value.AddDays(1));

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            var logs = await query
                .OrderByDescending(a => a.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new
                {
                    id = a.AuditID,
                    timestamp = a.Timestamp,
                    action = a.Action,
                    category = a.ActionCategory ?? "General",
                    entityType = a.EntityType,
                    entityId = a.EntityID,
                    description = a.Description,
                    ipAddress = a.IPAddress,
                    userName = a.User != null ? a.User.FullName : "System"
                })
                .ToListAsync();

            // Get filter options
            var actions = await _context.AuditLogs
                .Where(a => a.FirmID == firmId || a.FirmID == null)
                .Select(a => a.Action)
                .Distinct()
                .OrderBy(a => a)
                .ToListAsync();

            var categories = await _context.AuditLogs
                .Where(a => a.FirmID == firmId || a.FirmID == null)
                .Select(a => a.ActionCategory)
                .Where(c => c != null)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

            // For Admin/Auditor, include user list for filter
            object? users = null;
            if (role == "Admin" || role == "Auditor")
            {
                users = await _context.Users
                    .Where(u => u.FirmID == firmId)
                    .Select(u => new { id = u.UserID, name = u.FullName })
                    .OrderBy(u => u.name)
                    .ToListAsync();
            }

            return Ok(new
            {
                success = true,
                logs,
                totalCount,
                currentPage = page,
                pageSize,
                totalPages,
                filters = new { actions, categories, users },
                debug = new { firmId, role, currentUserId }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetAuditLogs API");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }
}
