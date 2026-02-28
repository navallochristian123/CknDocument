using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;
using CKNDocument.Services;
using System.Security.Claims;

namespace CKNDocument.Controllers.Api;

/// <summary>
/// API Controller for Retention operations
/// Handles retention policies and document retention management
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class RetentionApiController : ControllerBase
{
    private readonly LawFirmDMSDbContext _context;
    private readonly AuditLogService _auditLogService;
    private readonly ILogger<RetentionApiController> _logger;

    public RetentionApiController(
        LawFirmDMSDbContext context,
        AuditLogService auditLogService,
        ILogger<RetentionApiController> logger)
    {
        _context = context;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    private int GetCurrentUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    private int GetFirmId() => int.Parse(User.FindFirst("FirmId")?.Value ?? "0");

    #region Retention Policies CRUD

    /// <summary>
    /// Get all retention policies for the firm
    /// </summary>
    [HttpGet("policies")]
    public async Task<IActionResult> GetPolicies()
    {
        var firmId = GetFirmId();

        var policies = await _context.RetentionPolicies
            .Include(p => p.Creator)
            .Where(p => p.FirmId == firmId && p.IsActive == true)
            .OrderBy(p => p.DocumentType)
            .ThenBy(p => p.PolicyName)
            .Select(p => new
            {
                policyId = p.PolicyID,
                policyName = p.PolicyName,
                documentType = p.DocumentType,
                retentionYears = p.RetentionYears ?? 0,
                retentionMonths = p.RetentionMonths ?? 0,
                retentionDays = p.RetentionDays ?? 0,
                totalMonths = (p.RetentionYears ?? 0) * 12 + (p.RetentionMonths ?? 0),
                description = p.Description,
                isDefault = p.IsDefault,
                createdBy = p.Creator != null ? (p.Creator.FirstName ?? "") + " " + (p.Creator.LastName ?? "") : null,
                createdAt = p.CreatedAt,
                documentCount = _context.DocumentRetentions.Count(dr => dr.PolicyID == p.PolicyID)
            })
            .ToListAsync();

        return Ok(new { success = true, policies });
    }

    /// <summary>
    /// Get a single retention policy
    /// </summary>
    [HttpGet("policies/{id}")]
    public async Task<IActionResult> GetPolicy(int id)
    {
        var firmId = GetFirmId();

        var policy = await _context.RetentionPolicies
            .Include(p => p.Creator)
            .FirstOrDefaultAsync(p => p.PolicyID == id && p.FirmId == firmId);

        if (policy == null)
            return NotFound(new { success = false, message = "Policy not found" });

        return Ok(new
        {
            success = true,
            policy = new
            {
                policyId = policy.PolicyID,
                policyName = policy.PolicyName,
                documentType = policy.DocumentType,
                retentionYears = policy.RetentionYears,
                retentionMonths = policy.RetentionMonths,
                retentionDays = policy.RetentionDays,
                description = policy.Description,
                isDefault = policy.IsDefault,
                createdBy = policy.Creator?.FullName,
                createdAt = policy.CreatedAt
            }
        });
    }

    /// <summary>
    /// Create a new retention policy
    /// </summary>
    [HttpPost("policies")]
    public async Task<IActionResult> CreatePolicy([FromBody] RetentionPolicyDto dto)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        if (string.IsNullOrWhiteSpace(dto.PolicyName))
            return BadRequest(new { success = false, message = "Policy name is required" });

        // Check for duplicate policy name
        var exists = await _context.RetentionPolicies
            .AnyAsync(p => p.FirmId == firmId && p.PolicyName == dto.PolicyName && p.IsActive == true);

        if (exists)
            return BadRequest(new { success = false, message = "A policy with this name already exists" });

        var policy = new RetentionPolicy
        {
            FirmId = firmId,
            PolicyName = dto.PolicyName,
            DocumentType = dto.DocumentType,
            RetentionYears = dto.RetentionYears ?? 0,
            RetentionMonths = dto.RetentionMonths ?? 0,
            RetentionDays = dto.RetentionDays ?? 0,
            Description = dto.Description,
            IsDefault = dto.IsDefault ?? false,
            IsActive = true,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow
        };

        // If setting as default, unset other defaults for this document type
        if (policy.IsDefault == true && !string.IsNullOrEmpty(dto.DocumentType))
        {
            var existingDefaults = await _context.RetentionPolicies
                .Where(p => p.FirmId == firmId && p.DocumentType == dto.DocumentType && p.IsDefault == true)
                .ToListAsync();

            foreach (var existing in existingDefaults)
            {
                existing.IsDefault = false;
            }
        }

        _context.RetentionPolicies.Add(policy);
        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "CreateRetentionPolicy",
            "RetentionPolicy",
            policy.PolicyID,
            $"Created retention policy: {policy.PolicyName}",
            null,
            null,
            "RetentionManagement");

        return Ok(new { success = true, message = "Policy created successfully", policyId = policy.PolicyID });
    }

    /// <summary>
    /// Update a retention policy
    /// </summary>
    [HttpPut("policies/{id}")]
    public async Task<IActionResult> UpdatePolicy(int id, [FromBody] RetentionPolicyDto dto)
    {
        var firmId = GetFirmId();

        var policy = await _context.RetentionPolicies
            .FirstOrDefaultAsync(p => p.PolicyID == id && p.FirmId == firmId);

        if (policy == null)
            return NotFound(new { success = false, message = "Policy not found" });

        if (!string.IsNullOrWhiteSpace(dto.PolicyName))
            policy.PolicyName = dto.PolicyName;

        if (!string.IsNullOrWhiteSpace(dto.DocumentType))
            policy.DocumentType = dto.DocumentType;

        if (dto.RetentionYears.HasValue)
            policy.RetentionYears = dto.RetentionYears;

        if (dto.RetentionMonths.HasValue)
            policy.RetentionMonths = dto.RetentionMonths;

        if (dto.RetentionDays.HasValue)
            policy.RetentionDays = dto.RetentionDays;

        if (!string.IsNullOrWhiteSpace(dto.Description))
            policy.Description = dto.Description;

        if (dto.IsDefault.HasValue)
        {
            // If setting as default, unset other defaults for this document type
            if (dto.IsDefault == true && !string.IsNullOrEmpty(policy.DocumentType))
            {
                var existingDefaults = await _context.RetentionPolicies
                    .Where(p => p.FirmId == firmId && p.DocumentType == policy.DocumentType && p.IsDefault == true && p.PolicyID != id)
                    .ToListAsync();

                foreach (var existing in existingDefaults)
                {
                    existing.IsDefault = false;
                }
            }
            policy.IsDefault = dto.IsDefault;
        }

        policy.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "UpdateRetentionPolicy",
            "RetentionPolicy",
            id,
            $"Updated retention policy: {policy.PolicyName}",
            null,
            null,
            "RetentionManagement");

        return Ok(new { success = true, message = "Policy updated successfully" });
    }

    /// <summary>
    /// Delete a retention policy (soft delete)
    /// </summary>
    [HttpDelete("policies/{id}")]
    public async Task<IActionResult> DeletePolicy(int id)
    {
        var firmId = GetFirmId();

        var policy = await _context.RetentionPolicies
            .FirstOrDefaultAsync(p => p.PolicyID == id && p.FirmId == firmId);

        if (policy == null)
            return NotFound(new { success = false, message = "Policy not found" });

        // Check if policy is in use
        var inUse = await _context.DocumentRetentions.AnyAsync(dr => dr.PolicyID == id);
        if (inUse)
        {
            // Soft delete
            policy.IsActive = false;
            policy.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            // Hard delete
            _context.RetentionPolicies.Remove(policy);
        }

        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "DeleteRetentionPolicy",
            "RetentionPolicy",
            id,
            $"Deleted retention policy: {policy.PolicyName}",
            null,
            null,
            "RetentionManagement");

        return Ok(new { success = true, message = "Policy deleted successfully" });
    }

    #endregion

    #region Document Retention Management

    /// <summary>
    /// Get documents under retention with time categorization
    /// </summary>
    [HttpGet("documents")]
    public async Task<IActionResult> GetRetentionDocuments([FromQuery] string? category = null)
    {
        var firmId = GetFirmId();
        var now = DateTime.UtcNow;

        var query = _context.DocumentRetentions
            .Include(dr => dr.Document)
                .ThenInclude(d => d!.Uploader)
            .Include(dr => dr.Document)
                .ThenInclude(d => d!.Folder)
            .Include(dr => dr.Policy)
            .Where(dr => dr.FirmId == firmId && dr.IsArchived != true && dr.Document != null);

        // Filter by category
        if (!string.IsNullOrEmpty(category))
        {
            switch (category.ToLower())
            {
                case "years":
                    // More than 1 year remaining
                    query = query.Where(dr => dr.ExpiryDate > now.AddYears(1));
                    break;
                case "months":
                    // Between 1 month and 1 year remaining
                    query = query.Where(dr => dr.ExpiryDate > now.AddDays(30) && dr.ExpiryDate <= now.AddYears(1));
                    break;
                case "days":
                    // Less than 365 days remaining
                    query = query.Where(dr => dr.ExpiryDate <= now.AddDays(365));
                    break;
            }
        }

        var retentions = await query
            .OrderBy(dr => dr.ExpiryDate)
            .Select(dr => new
            {
                retentionId = dr.RetentionID,
                documentId = dr.DocumentID,
                documentTitle = dr.Document != null ? dr.Document.Title : "Unknown",
                documentType = dr.Document != null ? dr.Document.DocumentType : null,
                originalFileName = dr.Document != null ? dr.Document.OriginalFileName : null,
                fileExtension = dr.Document != null ? dr.Document.FileExtension : null,
                clientName = dr.Document != null && dr.Document.Uploader != null ? (dr.Document.Uploader.FirstName ?? "") + " " + (dr.Document.Uploader.LastName ?? "") : null,
                folderName = dr.Document != null && dr.Document.Folder != null ? dr.Document.Folder.FolderName : null,
                policyName = dr.Policy != null ? dr.Policy.PolicyName : "Custom",
                retentionStartDate = dr.RetentionStartDate,
                expiryDate = dr.ExpiryDate,
                retentionYears = dr.RetentionYears,
                retentionMonths = dr.RetentionMonths,
                retentionDays = dr.RetentionDays,
                daysRemaining = dr.ExpiryDate.HasValue ? (int)(dr.ExpiryDate.Value - now).TotalDays : 0,
                isModified = dr.IsModified
            })
            .ToListAsync();

        return Ok(new { success = true, retentions });
    }

    /// <summary>
    /// Get retention statistics
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var firmId = GetFirmId();
        var now = DateTime.UtcNow;

        var stats = new
        {
            totalPolicies = await _context.RetentionPolicies
                .CountAsync(p => p.FirmId == firmId && p.IsActive == true),

            totalDocumentsUnderRetention = await _context.DocumentRetentions
                .CountAsync(dr => dr.FirmId == firmId && dr.IsArchived != true),

            yearsRemaining = await _context.DocumentRetentions
                .CountAsync(dr => dr.FirmId == firmId && dr.IsArchived != true && dr.ExpiryDate > now.AddYears(1)),

            monthsRemaining = await _context.DocumentRetentions
                .CountAsync(dr => dr.FirmId == firmId && dr.IsArchived != true && 
                    dr.ExpiryDate > now.AddDays(30) && dr.ExpiryDate <= now.AddYears(1)),

            daysRemaining = await _context.DocumentRetentions
                .CountAsync(dr => dr.FirmId == firmId && dr.IsArchived != true && dr.ExpiryDate <= now.AddDays(365)),

            expiringThisMonth = await _context.DocumentRetentions
                .CountAsync(dr => dr.FirmId == firmId && dr.IsArchived != true && 
                    dr.ExpiryDate <= now.AddDays(30)),

            expiredCount = await _context.DocumentRetentions
                .CountAsync(dr => dr.FirmId == firmId && dr.IsArchived != true && dr.ExpiryDate <= now)
        };

        return Ok(new { success = true, stats });
    }

    /// <summary>
    /// Get retention details for a specific document
    /// </summary>
    [HttpGet("document/{documentId}")]
    public async Task<IActionResult> GetDocumentRetention(int documentId)
    {
        var firmId = GetFirmId();

        var retention = await _context.DocumentRetentions
            .Include(dr => dr.Document)
                .ThenInclude(d => d!.Uploader)
            .Include(dr => dr.Document)
                .ThenInclude(d => d!.Folder)
            .Include(dr => dr.Policy)
            .Include(dr => dr.ModifiedByUser)
            .FirstOrDefaultAsync(dr => dr.DocumentID == documentId && dr.FirmId == firmId);

        if (retention == null)
            return NotFound(new { success = false, message = "Retention record not found" });

        var now = DateTime.UtcNow;
        var daysRemaining = retention.ExpiryDate.HasValue ? (int)(retention.ExpiryDate.Value - now).TotalDays : 0;
        var yearsRemaining = daysRemaining / 365;
        var monthsRemaining = (daysRemaining % 365) / 30;
        var daysLeft = daysRemaining % 30;

        return Ok(new
        {
            success = true,
            retention = new
            {
                retentionId = retention.RetentionID,
                documentId = retention.DocumentID,
                documentTitle = retention.Document?.Title,
                documentType = retention.Document?.DocumentType,
                originalFileName = retention.Document?.OriginalFileName,
                fileExtension = retention.Document?.FileExtension,
                clientName = retention.Document?.Uploader?.FullName,
                folderName = retention.Document?.Folder?.FolderName,
                policyId = retention.PolicyID,
                policyName = retention.Policy?.PolicyName ?? "Custom",
                retentionStartDate = retention.RetentionStartDate,
                expiryDate = retention.ExpiryDate,
                retentionYears = retention.RetentionYears,
                retentionMonths = retention.RetentionMonths,
                retentionDays = retention.RetentionDays,
                totalDaysRemaining = daysRemaining,
                timeRemaining = new
                {
                    years = Math.Max(0, yearsRemaining),
                    months = Math.Max(0, monthsRemaining),
                    days = Math.Max(0, daysLeft)
                },
                isArchived = retention.IsArchived,
                isModified = retention.IsModified,
                modificationReason = retention.ModificationReason,
                modifiedBy = retention.ModifiedByUser?.FullName,
                modifiedAt = retention.ModifiedAt
            }
        });
    }

    /// <summary>
    /// Modify retention period for a document
    /// </summary>
    [HttpPut("document/{documentId}")]
    public async Task<IActionResult> ModifyDocumentRetention(int documentId, [FromBody] ModifyRetentionDto dto)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        var retention = await _context.DocumentRetentions
            .Include(dr => dr.Document)
            .FirstOrDefaultAsync(dr => dr.DocumentID == documentId && dr.FirmId == firmId);

        if (retention == null)
            return NotFound(new { success = false, message = "Retention record not found" });

        var oldExpiryDate = retention.ExpiryDate;

        // Update retention period
        if (dto.RetentionYears.HasValue)
            retention.RetentionYears = dto.RetentionYears;
        if (dto.RetentionMonths.HasValue)
            retention.RetentionMonths = dto.RetentionMonths;
        if (dto.RetentionDays.HasValue)
            retention.RetentionDays = dto.RetentionDays;

        // Recalculate expiry date from start date
        if (retention.RetentionStartDate.HasValue)
        {
            var newExpiry = retention.RetentionStartDate.Value;
            newExpiry = newExpiry.AddYears(retention.RetentionYears ?? 0);
            newExpiry = newExpiry.AddMonths(retention.RetentionMonths ?? 0);
            newExpiry = newExpiry.AddDays(retention.RetentionDays ?? 0);
            retention.ExpiryDate = newExpiry;
        }
        else if (dto.NewExpiryDate.HasValue)
        {
            retention.ExpiryDate = dto.NewExpiryDate;
        }

        retention.IsModified = true;
        retention.ModificationReason = dto.Reason;
        retention.ModifiedBy = userId;
        retention.ModifiedAt = DateTime.UtcNow;

        // Update document retention expiry date
        if (retention.Document != null)
        {
            retention.Document.RetentionExpiryDate = retention.ExpiryDate;
            retention.Document.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "ModifyRetention",
            "DocumentRetention",
            retention.RetentionID,
            $"Modified retention for document: {retention.Document?.Title}. Old expiry: {oldExpiryDate}, New expiry: {retention.ExpiryDate}. Reason: {dto.Reason}",
            null,
            null,
            "RetentionManagement");

        return Ok(new { success = true, message = "Retention period modified successfully", newExpiryDate = retention.ExpiryDate });
    }

    /// <summary>
    /// Apply a retention policy to a document
    /// </summary>
    [HttpPost("apply")]
    public async Task<IActionResult> ApplyRetention([FromBody] ApplyRetentionDto dto)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.DocumentID == dto.DocumentId && d.FirmID == firmId);

        if (document == null)
            return NotFound(new { success = false, message = "Document not found" });

        RetentionPolicy? policy = null;
        int retentionYears = dto.RetentionYears ?? 0;
        int retentionMonths = dto.RetentionMonths ?? 0;
        int retentionDays = dto.RetentionDays ?? 0;

        if (dto.PolicyId.HasValue)
        {
            policy = await _context.RetentionPolicies
                .FirstOrDefaultAsync(p => p.PolicyID == dto.PolicyId && p.FirmId == firmId);

            if (policy != null)
            {
                retentionYears = policy.RetentionYears ?? 0;
                retentionMonths = policy.RetentionMonths ?? 0;
                retentionDays = policy.RetentionDays ?? 0;
            }
        }

        // Check if retention already exists
        var existingRetention = await _context.DocumentRetentions
            .FirstOrDefaultAsync(dr => dr.DocumentID == dto.DocumentId);

        var startDate = DateTime.UtcNow;
        var expiryDate = startDate
            .AddYears(retentionYears)
            .AddMonths(retentionMonths)
            .AddDays(retentionDays);

        if (existingRetention != null)
        {
            // Update existing retention
            existingRetention.PolicyID = dto.PolicyId;
            existingRetention.RetentionStartDate = startDate;
            existingRetention.ExpiryDate = expiryDate;
            existingRetention.RetentionYears = retentionYears;
            existingRetention.RetentionMonths = retentionMonths;
            existingRetention.RetentionDays = retentionDays;
            existingRetention.IsModified = true;
            existingRetention.ModificationReason = "Retention policy applied/updated";
            existingRetention.ModifiedBy = userId;
            existingRetention.ModifiedAt = DateTime.UtcNow;
        }
        else
        {
            // Create new retention record
            var retention = new DocumentRetention
            {
                DocumentID = dto.DocumentId,
                PolicyID = dto.PolicyId,
                FirmId = firmId,
                RetentionStartDate = startDate,
                ExpiryDate = expiryDate,
                RetentionYears = retentionYears,
                RetentionMonths = retentionMonths,
                RetentionDays = retentionDays,
                IsArchived = false,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };
            _context.DocumentRetentions.Add(retention);
        }

        // Update document retention expiry
        document.RetentionExpiryDate = expiryDate;
        document.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "ApplyRetention",
            "Document",
            dto.DocumentId,
            $"Applied retention to document: {document.Title}. Expiry: {expiryDate}",
            null,
            null,
            "RetentionManagement");

        return Ok(new
        {
            success = true,
            message = "Retention applied successfully",
            retentionInfo = new
            {
                documentId = dto.DocumentId,
                retentionStartDate = startDate,
                expiryDate = expiryDate,
                retentionYears = retentionYears,
                retentionMonths = retentionMonths,
                retentionDays = retentionDays
            }
        });
    }

    /// <summary>
    /// Process expired retentions and auto-archive
    /// </summary>
    [HttpPost("process-expired")]
    public async Task<IActionResult> ProcessExpiredRetentions()
    {
        var firmId = GetFirmId();
        var userId = GetCurrentUserId();
        var now = DateTime.UtcNow;

        var expiredRetentions = await _context.DocumentRetentions
            .Include(dr => dr.Document)
            .Where(dr => dr.FirmId == firmId && 
                         dr.IsArchived != true && 
                         dr.ExpiryDate <= now &&
                         dr.Document != null)
            .ToListAsync();

        var archivedCount = 0;

        foreach (var retention in expiredRetentions)
        {
            if (retention.Document == null) continue;

            // Create archive record
            var archive = new Archive
            {
                DocumentID = retention.DocumentID,
                ArchivedDate = now,
                Reason = "Retention period expired",
                ArchiveType = "Retention",
                OriginalRetentionDate = retention.ExpiryDate,
                ArchivedBy = userId,
                IsRestored = false
            };

            _context.Archives.Add(archive);

            // Update retention record
            retention.IsArchived = true;

            // Update document status
            retention.Document.WorkflowStage = "Archived";
            retention.Document.Status = "Archived";
            retention.Document.UpdatedAt = now;

            archivedCount++;
        }

        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "ProcessExpiredRetentions",
            "System",
            0,
            $"Processed {archivedCount} expired retention documents",
            null,
            null,
            "RetentionManagement");

        return Ok(new { success = true, message = $"Processed {archivedCount} expired documents", archivedCount });
    }

    /// <summary>
    /// Get default policy for a document type
    /// </summary>
    [HttpGet("default-policy/{documentType}")]
    public async Task<IActionResult> GetDefaultPolicy(string documentType)
    {
        var firmId = GetFirmId();

        var policy = await _context.RetentionPolicies
            .FirstOrDefaultAsync(p => p.FirmId == firmId && 
                                      p.DocumentType == documentType && 
                                      p.IsDefault == true && 
                                      p.IsActive == true);

        if (policy == null)
        {
            // Return default retention if no policy exists
            return Ok(new
            {
                success = true,
                hasPolicy = false,
                defaultRetention = new
                {
                    years = 7,
                    months = 0,
                    days = 0
                }
            });
        }

        return Ok(new
        {
            success = true,
            hasPolicy = true,
            policy = new
            {
                policyId = policy.PolicyID,
                policyName = policy.PolicyName,
                retentionYears = policy.RetentionYears ?? 0,
                retentionMonths = policy.RetentionMonths ?? 0,
                retentionDays = policy.RetentionDays ?? 0
            }
        });
    }

    #endregion

    #region Post-Retention Workflow (Hold, Destroy, Certificate)

    /// <summary>
    /// Get archives pending disposition (retention expired, in grace period)
    /// </summary>
    [HttpGet("disposition")]
    public async Task<IActionResult> GetDispositionQueue()
    {
        var firmId = GetFirmId();
        var now = DateTime.UtcNow;

        var archives = await _context.Archives
            .Include(a => a.Document)
                .ThenInclude(d => d!.Uploader)
            .Include(a => a.HoldPlacedByUser)
            .Where(a => a.Document != null &&
                       a.Document.FirmID == firmId &&
                       a.IsRestored != true &&
                       (a.ArchiveType == "AutoExpired" || a.ArchiveType == "Retention") &&
                       (a.RetentionDispositionStatus == "PendingReview" || 
                        a.RetentionDispositionStatus == "OnHold" ||
                        a.RetentionDispositionStatus == "Destroyed"))
            .OrderBy(a => a.GracePeriodEndDate)
            .Select(a => new
            {
                archiveId = a.ArchiveID,
                documentId = a.DocumentID,
                documentTitle = a.Document != null ? a.Document.Title : "Unknown",
                documentType = a.Document != null ? a.Document.DocumentType : null,
                clientName = a.Document != null && a.Document.Uploader != null
                    ? (a.Document.Uploader.FirstName ?? "") + " " + (a.Document.Uploader.LastName ?? "")
                    : null,
                archiveType = a.ArchiveType,
                archivedDate = a.ArchivedDate,
                retentionExpiredDate = a.OriginalRetentionDate,
                dispositionStatus = a.RetentionDispositionStatus ?? "PendingReview",
                gracePeriodStartDate = a.GracePeriodStartDate,
                gracePeriodEndDate = a.GracePeriodEndDate,
                daysUntilDeletion = a.GracePeriodEndDate.HasValue
                    ? Math.Max(0, (int)(a.GracePeriodEndDate.Value - now).TotalDays)
                    : 0,
                isOnHold = a.IsOnHold == true,
                holdReason = a.HoldReason,
                holdPlacedAt = a.HoldPlacedAt,
                holdPlacedBy = a.HoldPlacedByUser != null
                    ? (a.HoldPlacedByUser.FirstName ?? "") + " " + (a.HoldPlacedByUser.LastName ?? "")
                    : null,
                isDeleted = a.IsDeleted == true,
                destroyedAt = a.DestroyedAt,
                hasDestructionCertificate = a.HasDestructionCertificate == true
            })
            .ToListAsync();

        return Ok(new { success = true, archives });
    }

    /// <summary>
    /// Place a legal hold on an archived document (prevents auto-deletion)
    /// </summary>
    [HttpPost("hold/{archiveId}")]
    public async Task<IActionResult> PlaceHold(int archiveId, [FromBody] HoldDto dto)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        var archive = await _context.Archives
            .Include(a => a.Document)
            .FirstOrDefaultAsync(a => a.ArchiveID == archiveId &&
                                     a.Document != null &&
                                     a.Document.FirmID == firmId &&
                                     a.IsDeleted != true);

        if (archive == null)
            return NotFound(new { success = false, message = "Archive not found" });

        if (archive.IsOnHold == true)
            return BadRequest(new { success = false, message = "Document is already on legal hold" });

        archive.IsOnHold = true;
        archive.HoldPlacedAt = DateTime.UtcNow;
        archive.HoldPlacedBy = userId;
        archive.HoldReason = dto.Reason ?? "Legal hold placed by admin";
        archive.RetentionDispositionStatus = "OnHold";

        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "PlaceLegalHold",
            "Archive",
            archiveId,
            $"Legal hold placed on document: {archive.Document?.Title}. Reason: {dto.Reason}",
            null, null, "RetentionManagement");

        return Ok(new { success = true, message = "Legal hold placed successfully" });
    }

    /// <summary>
    /// Release a legal hold on an archived document (allows auto-deletion to proceed)
    /// </summary>
    [HttpPost("release-hold/{archiveId}")]
    public async Task<IActionResult> ReleaseHold(int archiveId)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        var archive = await _context.Archives
            .Include(a => a.Document)
            .FirstOrDefaultAsync(a => a.ArchiveID == archiveId &&
                                     a.Document != null &&
                                     a.Document.FirmID == firmId &&
                                     a.IsDeleted != true);

        if (archive == null)
            return NotFound(new { success = false, message = "Archive not found" });

        if (archive.IsOnHold != true)
            return BadRequest(new { success = false, message = "Document is not on legal hold" });

        archive.IsOnHold = false;
        archive.HoldReleasedAt = DateTime.UtcNow;
        archive.HoldReleasedBy = userId;
        archive.RetentionDispositionStatus = "PendingReview";
        // Reset grace period to 30 days from hold release
        archive.GracePeriodStartDate = DateTime.UtcNow;
        archive.GracePeriodEndDate = DateTime.UtcNow.AddDays(30);

        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "ReleaseLegalHold",
            "Archive",
            archiveId,
            $"Legal hold released on document: {archive.Document?.Title}. New grace period ends: {archive.GracePeriodEndDate:d}",
            null, null, "RetentionManagement");

        return Ok(new { success = true, message = "Legal hold released. 30-day grace period restarted.", newGracePeriodEnd = archive.GracePeriodEndDate });
    }

    /// <summary>
    /// Manually approve a document for immediate deletion (skip grace period)
    /// </summary>
    [HttpPost("approve-deletion/{archiveId}")]
    public async Task<IActionResult> ApproveDeletion(int archiveId)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        var archive = await _context.Archives
            .Include(a => a.Document)
                .ThenInclude(d => d!.Versions)
            .Include(a => a.Document)
                .ThenInclude(d => d!.Uploader)
            .FirstOrDefaultAsync(a => a.ArchiveID == archiveId &&
                                     a.Document != null &&
                                     a.Document.FirmID == firmId &&
                                     a.IsDeleted != true);

        if (archive == null)
            return NotFound(new { success = false, message = "Archive not found" });

        if (archive.IsOnHold == true)
            return BadRequest(new { success = false, message = "Cannot delete document on legal hold. Release the hold first." });

        var documentTitle = archive.Document?.Title ?? "Unknown";
        var documentType = archive.Document?.DocumentType ?? "Unknown";
        var clientName = archive.Document?.Uploader?.FullName ?? "Unknown";
        var now = DateTime.UtcNow;

        // Delete physical files
        if (archive.Document?.Versions != null)
        {
            foreach (var version in archive.Document.Versions)
            {
                if (!string.IsNullOrEmpty(version.FilePath) && System.IO.File.Exists(version.FilePath))
                {
                    try { System.IO.File.Delete(version.FilePath); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete file: {FilePath}", version.FilePath); }
                }
            }
        }

        // Mark as destroyed
        archive.RetentionDispositionStatus = "Destroyed";
        archive.IsDeleted = true;
        archive.DeletedAt = now;
        archive.DeletedBy = userId;
        archive.DestroyedAt = now;

        // Remove related data
        if (archive.Document?.Versions != null)
            _context.DocumentVersions.RemoveRange(archive.Document.Versions);

        var retentionRecords = await _context.DocumentRetentions
            .Where(dr => dr.DocumentID == archive.DocumentID).ToListAsync();
        _context.DocumentRetentions.RemoveRange(retentionRecords);

        var reviews = await _context.DocumentReviews
            .Where(r => r.DocumentId == archive.DocumentID).ToListAsync();
        _context.DocumentReviews.RemoveRange(reviews);

        // Keep document metadata marked as Destroyed
        if (archive.Document != null)
        {
            archive.Document.Status = "Destroyed";
            archive.Document.WorkflowStage = "Destroyed";
            archive.Document.UpdatedAt = now;
        }

        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "ManualDocumentDestruction",
            "Document",
            archive.DocumentID ?? 0,
            $"Admin manually destroyed document: '{documentTitle}' (Type: {documentType}, Client: {clientName})",
            null, null, "RetentionDestruction");

        return Ok(new { success = true, message = "Document permanently destroyed. Destruction certificate available." });
    }

    /// <summary>
    /// Generate destruction certificate data for a destroyed document
    /// </summary>
    [HttpGet("destruction-certificate/{archiveId}")]
    public async Task<IActionResult> GetDestructionCertificate(int archiveId)
    {
        var firmId = GetFirmId();

        var archive = await _context.Archives
            .Include(a => a.Document)
                .ThenInclude(d => d!.Uploader)
            .Include(a => a.Document)
                .ThenInclude(d => d!.Firm)
            .Include(a => a.ArchivedByUser)
            .Include(a => a.DeletedByUser)
            .FirstOrDefaultAsync(a => a.ArchiveID == archiveId &&
                                     a.Document != null &&
                                     a.Document.FirmID == firmId);

        if (archive == null)
            return NotFound(new { success = false, message = "Archive not found" });

        if (archive.RetentionDispositionStatus != "Destroyed" && archive.IsDeleted != true)
            return BadRequest(new { success = false, message = "Destruction certificate is only available for destroyed documents" });

        var certificate = new
        {
            certificateNumber = $"DOC-{archive.ArchiveID:D6}-{archive.DestroyedAt?.Year ?? DateTime.UtcNow.Year}",
            firmName = archive.Document?.Firm?.FirmName ?? "Unknown Firm",
            documentTitle = archive.Document?.Title ?? "Unknown",
            documentType = archive.Document?.DocumentType ?? "Unknown",
            documentId = archive.DocumentID,
            clientName = archive.Document?.Uploader?.FullName ?? "Unknown",
            originalUploadDate = archive.Document?.CreatedAt,
            approvedDate = archive.Document?.ApprovedAt,
            retentionStartDate = archive.OriginalRetentionDate != null
                ? archive.OriginalRetentionDate.Value.AddYears(-(archive.Document?.Retentions?.FirstOrDefault()?.RetentionYears ?? 0))
                : archive.Document?.ApprovedAt,
            retentionExpiryDate = archive.OriginalRetentionDate,
            archivedDate = archive.ArchivedDate,
            gracePeriodStartDate = archive.GracePeriodStartDate,
            gracePeriodEndDate = archive.GracePeriodEndDate,
            destroyedDate = archive.DestroyedAt ?? archive.DeletedAt,
            destroyedBy = archive.DeletedByUser?.FullName ?? "System (Auto)",
            wasOnHold = archive.HoldPlacedAt != null,
            holdReason = archive.HoldReason,
            holdReleasedAt = archive.HoldReleasedAt,
            destructionMethod = "Physical file deletion with metadata preservation",
            certificationStatement = $"This certifies that the document '{archive.Document?.Title}' " +
                $"(ID: {archive.DocumentID}) has been permanently destroyed in accordance with the " +
                $"firm's retention policy. All physical copies and digital files have been securely deleted. " +
                $"This record is maintained for audit compliance purposes."
        };

        // Mark certificate as generated
        archive.HasDestructionCertificate = true;
        await _context.SaveChangesAsync();

        return Ok(new { success = true, certificate });
    }

    /// <summary>
    /// Get disposition statistics
    /// </summary>
    /// <summary>
    /// Manually trigger the retention lifecycle processing (archive expired, process grace periods)
    /// Useful for demos and testing with short retention periods
    /// </summary>
    [HttpPost("process-retention-now")]
    public async Task<IActionResult> ProcessRetentionNow()
    {
        try
        {
            var firmId = GetFirmId();
            var now = DateTime.UtcNow;
            int archivedCount = 0;
            int destroyedCount = 0;

            // Step 1: Archive expired retention documents
            var expiredRetentions = await _context.DocumentRetentions
                .Include(r => r.Document)
                .Where(r => r.IsArchived != true &&
                           r.ExpiryDate <= now &&
                           r.Document != null &&
                           r.Document.FirmID == firmId &&
                           (r.Document.Status == "Completed" || r.Document.Status == "Approved"))
                .ToListAsync();

            foreach (var retention in expiredRetentions)
            {
                if (retention.Document == null) continue;

                var existingArchive = await _context.Archives
                    .FirstOrDefaultAsync(a => a.DocumentID == retention.DocumentID &&
                                             a.IsRestored != true && a.IsDeleted != true);

                if (existingArchive != null)
                {
                    retention.IsArchived = true;
                    retention.ModifiedAt = now;
                    continue;
                }

                var gracePeriodEnd = now.AddDays(30);

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

                _context.Archives.Add(archive);
                retention.Document.Status = "Archived";
                retention.Document.WorkflowStage = "Archived";
                retention.Document.UpdatedAt = now;
                retention.IsArchived = true;
                retention.ModifiedAt = now;
                retention.ModificationReason = "Auto-archived due to retention expiry";
                archivedCount++;
            }

            await _context.SaveChangesAsync();

            // Step 2: Auto-destroy grace period expired archives (not on hold)
            var readyForDeletion = await _context.Archives
                .Include(a => a.Document)
                    .ThenInclude(d => d!.Versions)
                .Where(a => a.GracePeriodEndDate <= now &&
                           a.IsOnHold != true &&
                           a.IsDeleted != true &&
                           a.IsRestored != true &&
                           a.RetentionDispositionStatus == "PendingReview" &&
                           a.Document != null && a.Document.FirmID == firmId &&
                           (a.ArchiveType == "AutoExpired" || a.ArchiveType == "Retention"))
                .ToListAsync();

            foreach (var archive in readyForDeletion)
            {
                if (archive.Document == null) continue;

                if (archive.Document.Versions != null)
                {
                    foreach (var version in archive.Document.Versions)
                    {
                        if (!string.IsNullOrEmpty(version.FilePath) && System.IO.File.Exists(version.FilePath))
                        {
                            try { System.IO.File.Delete(version.FilePath); } catch { }
                        }
                    }
                    _context.DocumentVersions.RemoveRange(archive.Document.Versions);
                }

                archive.RetentionDispositionStatus = "Destroyed";
                archive.IsDeleted = true;
                archive.DeletedAt = now;
                archive.DestroyedAt = now;
                archive.Document.Status = "Destroyed";
                archive.Document.WorkflowStage = "Destroyed";
                archive.Document.UpdatedAt = now;
                destroyedCount++;
            }

            await _context.SaveChangesAsync();

            var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
            if (archivedCount > 0 || destroyedCount > 0)
            {
                await _auditLogService.LogAsync(
                    "ManualRetentionProcessing", "System", userId,
                    $"Manual retention processing: {archivedCount} archived, {destroyedCount} destroyed",
                    null, null, "RetentionManagement");
            }

            return Ok(new { success = true, message = $"Processing complete: {archivedCount} document(s) archived, {destroyedCount} auto-destroyed.", archivedCount, destroyedCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in manual retention processing");
            return StatusCode(500, new { success = false, message = "Error processing retention lifecycle" });
        }
    }

    [HttpGet("disposition-stats")]
    public async Task<IActionResult> GetDispositionStats()
    {
        var firmId = GetFirmId();
        var now = DateTime.UtcNow;

        var stats = new
        {
            pendingReview = await _context.Archives
                .CountAsync(a => a.Document != null && a.Document.FirmID == firmId &&
                    a.RetentionDispositionStatus == "PendingReview" &&
                    a.IsDeleted != true && a.IsRestored != true),

            onHold = await _context.Archives
                .CountAsync(a => a.Document != null && a.Document.FirmID == firmId &&
                    a.IsOnHold == true && a.IsDeleted != true && a.IsRestored != true),

            destroyed = await _context.Archives
                .CountAsync(a => a.Document != null && a.Document.FirmID == firmId &&
                    a.RetentionDispositionStatus == "Destroyed"),

            expiringGracePeriod = await _context.Archives
                .CountAsync(a => a.Document != null && a.Document.FirmID == firmId &&
                    a.RetentionDispositionStatus == "PendingReview" &&
                    a.GracePeriodEndDate <= now.AddDays(7) &&
                    a.IsDeleted != true && a.IsRestored != true)
        };

        return Ok(new { success = true, stats });
    }

    #endregion
}

// DTOs
public class RetentionPolicyDto
{
    public string? PolicyName { get; set; }
    public string? DocumentType { get; set; }
    public int? RetentionYears { get; set; }
    public int? RetentionMonths { get; set; }
    public int? RetentionDays { get; set; }
    public string? Description { get; set; }
    public bool? IsDefault { get; set; }
}

public class ModifyRetentionDto
{
    public int? RetentionYears { get; set; }
    public int? RetentionMonths { get; set; }
    public int? RetentionDays { get; set; }
    public DateTime? NewExpiryDate { get; set; }
    public string? Reason { get; set; }
}

public class ApplyRetentionDto
{
    public int DocumentId { get; set; }
    public int? PolicyId { get; set; }
    public int? RetentionYears { get; set; }
    public int? RetentionMonths { get; set; }
    public int? RetentionDays { get; set; }
}

public class HoldDto
{
    public string? Reason { get; set; }
}
