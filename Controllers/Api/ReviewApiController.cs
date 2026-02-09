using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;
using CKNDocument.Services;
using System.Security.Claims;

namespace CKNDocument.Controllers.Api;

/// <summary>
/// API Controller for Review operations
/// Handles document reviews for Staff and Admin
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOrStaff")]
public class ReviewApiController : ControllerBase
{
    private readonly LawFirmDMSDbContext _context;
    private readonly DocumentWorkflowService _workflowService;
    private readonly NotificationService _notificationService;
    private readonly AuditLogService _auditLogService;
    private readonly DocumentAIService _aiService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ReviewApiController> _logger;

    public ReviewApiController(
        LawFirmDMSDbContext context,
        DocumentWorkflowService workflowService,
        NotificationService notificationService,
        AuditLogService auditLogService,
        DocumentAIService aiService,
        IWebHostEnvironment environment,
        ILogger<ReviewApiController> logger)
    {
        _context = context;
        _workflowService = workflowService;
        _notificationService = notificationService;
        _auditLogService = auditLogService;
        _aiService = aiService;
        _environment = environment;
        _logger = logger;
    }

    private int GetCurrentUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    private int GetFirmId() => int.Parse(User.FindFirst("FirmId")?.Value ?? "0");
    private string GetUserRole() => User.FindFirst(ClaimTypes.Role)?.Value ?? "Staff";

    /// <summary>
    /// Get pending reviews for staff
    /// </summary>
    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingReviews([FromQuery] bool assignedToMe = false)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();
        var role = GetUserRole();

        List<Document> documents;

        if (role == "Staff")
        {
            documents = await _workflowService.GetPendingStaffReviewsAsync(
                firmId,
                assignedToMe ? userId : null);
        }
        else // Admin
        {
            documents = await _workflowService.GetPendingAdminReviewsAsync(
                firmId,
                assignedToMe ? userId : null);
        }

        var result = documents.Select(d => new
        {
            id = d.DocumentID,
            title = d.Title,
            description = d.Description,
            documentType = d.DocumentType,
            status = d.Status,
            workflowStage = d.WorkflowStage,
            originalFileName = d.OriginalFileName,
            fileExtension = d.FileExtension,
            totalFileSize = d.TotalFileSize,
            currentVersion = d.CurrentVersion,
            isDuplicate = d.IsDuplicate,
            isAIProcessed = d.IsAIProcessed,
            uploader = d.Uploader != null ? new { id = d.Uploader.UserID, name = d.Uploader.FullName } : null,
            folder = d.Folder != null ? new { id = d.Folder.FolderId, name = d.Folder.FolderName } : null,
            assignedStaff = d.AssignedStaff != null ? new { id = d.AssignedStaff.UserID, name = d.AssignedStaff.FullName } : null,
            createdAt = d.CreatedAt
        });

        return Ok(new { success = true, documents = result });
    }

    /// <summary>
    /// Get checklist items for review
    /// </summary>
    [HttpGet("checklist-items")]
    public async Task<IActionResult> GetChecklistItems([FromQuery] string? documentType = null)
    {
        var firmId = GetFirmId();

        var query = _context.DocumentChecklistItems
            .Where(c => c.FirmId == firmId && c.IsActive == true);

        // Filter by document type if specified
        if (!string.IsNullOrEmpty(documentType))
        {
            query = query.Where(c => c.DocumentType == null || c.DocumentType == "" || c.DocumentType == documentType);
        }

        var items = await query
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new
            {
                checklistItemId = c.ChecklistItemId,
                itemName = c.ItemName,
                description = c.Description,
                documentType = c.DocumentType,
                isRequired = c.IsRequired ?? true,
                displayOrder = c.DisplayOrder
            })
            .ToListAsync();

        return Ok(new { success = true, items });
    }

    /// <summary>
    /// Get review history for a document
    /// </summary>
    [HttpGet("{documentId}/history")]
    public async Task<IActionResult> GetReviewHistory(int documentId)
    {
        var firmId = GetFirmId();

        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.FirmID == firmId);

        if (document == null)
            return NotFound(new { success = false, message = "Document not found" });

        var reviews = await _context.DocumentReviews
            .Include(r => r.Reviewer)
            .Include(r => r.ChecklistResults)
                .ThenInclude(cr => cr.ChecklistItem)
            .Where(r => r.DocumentId == documentId)
            .OrderByDescending(r => r.ReviewedAt)
            .Select(r => new
            {
                reviewId = r.ReviewId,
                reviewStatus = r.ReviewStatus,
                remarks = r.Remarks,
                internalNotes = r.InternalNotes,
                reviewerRole = r.ReviewerRole,
                reviewerName = r.Reviewer != null ? r.Reviewer.FullName : null,
                reviewedAt = r.ReviewedAt,
                isChecklistComplete = r.IsChecklistComplete,
                checklistScore = r.ChecklistScore,
                checklistResults = r.ChecklistResults.Select(cr => new
                {
                    itemId = cr.ChecklistItemId,
                    itemName = cr.ChecklistItem != null ? cr.ChecklistItem.ItemName : null,
                    isPassed = cr.IsPassed,
                    remarks = cr.Remarks
                }).ToList()
            })
            .ToListAsync();

        return Ok(new { success = true, reviews });
    }

    /// <summary>
    /// Get completed reviews
    /// </summary>
    [HttpGet("completed")]
    public async Task<IActionResult> GetCompletedReviews([FromQuery] int take = 50)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();
        var role = GetUserRole();

        var reviews = await _context.DocumentReviews
            .Include(r => r.Document)
                .ThenInclude(d => d!.Uploader)
            .Include(r => r.Reviewer)
            .Where(r => r.ReviewedBy == userId && r.ReviewerRole == role)
            .OrderByDescending(r => r.ReviewedAt)
            .Take(take)
            .Select(r => new
            {
                reviewId = r.ReviewId,
                documentId = r.DocumentId,
                documentTitle = r.Document != null ? r.Document.Title : null,
                documentFileName = r.Document != null ? r.Document.OriginalFileName : null,
                reviewStatus = r.ReviewStatus,
                remarks = r.Remarks,
                reviewedAt = r.ReviewedAt,
                uploaderName = r.Document != null && r.Document.Uploader != null ? r.Document.Uploader.FullName : null,
                isChecklistComplete = r.IsChecklistComplete
            })
            .ToListAsync();

        return Ok(new { success = true, reviews });
    }

    /// <summary>
    /// Get document for review with checklist
    /// </summary>
    [HttpGet("{documentId}")]
    public async Task<IActionResult> GetDocumentForReview(int documentId)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();
        var role = GetUserRole();

        var document = await _context.Documents
            .Include(d => d.Uploader)
            .Include(d => d.Folder)
            .Include(d => d.AssignedStaff)
            .Include(d => d.AssignedAdmin)
            .Include(d => d.Versions.OrderByDescending(v => v.VersionNumber))
            .Include(d => d.Reviews.OrderByDescending(r => r.ReviewedAt))
                .ThenInclude(r => r.Reviewer)
            .Include(d => d.Reviews)
                .ThenInclude(r => r.ChecklistResults)
                    .ThenInclude(cr => cr.ChecklistItem)
            .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.FirmID == firmId);

        if (document == null)
            return NotFound(new { success = false, message = "Document not found" });

        // Get AI analysis data first (needed for checklist filtering)
        object? aiAnalysis = null;
        string? detectedDocType = document.DocumentType;
        try
        {
            var analysis = await _context.DocumentAIAnalyses
                .Where(a => a.DocumentId == documentId && a.IsProcessed)
                .OrderByDescending(a => a.ProcessedAt)
                .FirstOrDefaultAsync();

            if (analysis != null)
            {
                detectedDocType = analysis.DetectedDocumentType ?? document.DocumentType;
                
                // Auto-create checklist items for this document type if they don't exist
                if (!string.IsNullOrEmpty(detectedDocType))
                {
                    await _aiService.EnsureChecklistItemsExistAsync(firmId, detectedDocType);
                }
                
                aiAnalysis = new
                {
                    detectedDocumentType = analysis.DetectedDocumentType,
                    confidence = (analysis.Confidence ?? 0) / 100.0,
                    summary = analysis.Summary,
                    checklist = string.IsNullOrEmpty(analysis.ChecklistJson) ? null : System.Text.Json.JsonSerializer.Deserialize<object>(analysis.ChecklistJson),
                    issues = string.IsNullOrEmpty(analysis.IssuesJson) ? null : System.Text.Json.JsonSerializer.Deserialize<object>(analysis.IssuesJson),
                    missingItems = string.IsNullOrEmpty(analysis.MissingItemsJson) ? null : System.Text.Json.JsonSerializer.Deserialize<object>(analysis.MissingItemsJson),
                    modelUsed = analysis.ModelUsed,
                    processedAt = analysis.ProcessedAt,
                    tokensUsed = analysis.TokensUsed
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading AI analysis for document {DocumentId}", documentId);
        }

        // Get checklist items filtered by detected document type
        // Priority: type-specific items first; if none exist, fall back to generic items
        List<object> checklistItems;
        
        if (!string.IsNullOrEmpty(detectedDocType))
        {
            // First check if type-specific items exist
            var typeSpecificItems = await _context.DocumentChecklistItems
                .Where(c => c.FirmId == firmId && c.IsActive == true && c.DocumentType == detectedDocType)
                .OrderBy(c => c.DisplayOrder)
                .Select(c => new
                {
                    id = c.ChecklistItemId,
                    itemName = c.ItemName,
                    description = c.Description,
                    documentType = c.DocumentType,
                    isRequired = c.IsRequired
                })
                .ToListAsync();

            if (typeSpecificItems.Any())
            {
                // Type-specific items exist - use only those
                checklistItems = typeSpecificItems.Cast<object>().ToList();
            }
            else
            {
                // No type-specific items - fall back to generic items
                checklistItems = await _context.DocumentChecklistItems
                    .Where(c => c.FirmId == firmId && c.IsActive == true && 
                               (c.DocumentType == null || c.DocumentType == ""))
                    .OrderBy(c => c.DisplayOrder)
                    .Select(c => new
                    {
                        id = c.ChecklistItemId,
                        itemName = c.ItemName,
                        description = c.Description,
                        documentType = c.DocumentType,
                        isRequired = c.IsRequired
                    })
                    .Cast<object>()
                    .ToListAsync();
            }
        }
        else
        {
            // No detected type - use generic items
            checklistItems = await _context.DocumentChecklistItems
                .Where(c => c.FirmId == firmId && c.IsActive == true && 
                           (c.DocumentType == null || c.DocumentType == ""))
                .OrderBy(c => c.DisplayOrder)
                .Select(c => new
                {
                    id = c.ChecklistItemId,
                    itemName = c.ItemName,
                    description = c.Description,
                    documentType = c.DocumentType,
                    isRequired = c.IsRequired
                })
                .Cast<object>()
                .ToListAsync();
        }

        return Ok(new
        {
            success = true,
            document = new
            {
                id = document.DocumentID,
                title = document.Title,
                description = document.Description,
                category = document.Category,
                documentType = document.DocumentType,
                status = document.Status,
                workflowStage = document.WorkflowStage,
                currentVersion = document.CurrentVersion,
                originalFileName = document.OriginalFileName,
                fileExtension = document.FileExtension,
                mimeType = document.MimeType,
                totalFileSize = document.TotalFileSize,
                isAIProcessed = document.IsAIProcessed,
                isDuplicate = document.IsDuplicate,
                currentRemarks = document.CurrentRemarks,
                uploader = document.Uploader != null ? new { id = document.Uploader.UserID, name = document.Uploader.FullName, email = document.Uploader.Email } : null,
                folder = document.Folder != null ? new { id = document.Folder.FolderId, name = document.Folder.FolderName } : null,
                assignedStaff = document.AssignedStaff != null ? new { id = document.AssignedStaff.UserID, name = document.AssignedStaff.FullName } : null,
                assignedAdmin = document.AssignedAdmin != null ? new { id = document.AssignedAdmin.UserID, name = document.AssignedAdmin.FullName } : null,
                createdAt = document.CreatedAt,
                versions = document.Versions.Select(v => new
                {
                    versionId = v.VersionId,
                    versionNumber = v.VersionNumber,
                    originalFileName = v.OriginalFileName,
                    fileSize = v.FileSize,
                    changeDescription = v.ChangeDescription,
                    changedBy = v.ChangedBy,
                    isCurrentVersion = v.IsCurrentVersion,
                    createdAt = v.CreatedAt
                }),
                reviewHistory = document.Reviews.Select(r => new
                {
                    reviewId = r.ReviewId,
                    reviewStatus = r.ReviewStatus,
                    remarks = r.Remarks,
                    internalNotes = r.InternalNotes,
                    reviewerRole = r.ReviewerRole,
                    reviewer = r.Reviewer?.FullName,
                    reviewedAt = r.ReviewedAt,
                    isChecklistComplete = r.IsChecklistComplete,
                    checklistScore = r.ChecklistScore,
                    checklistResults = r.ChecklistResults.Select(cr => new
                    {
                        itemId = cr.ChecklistItemId,
                        itemName = cr.ChecklistItem?.ItemName,
                        isPassed = cr.IsPassed,
                        remarks = cr.Remarks
                    })
                })
            },
            checklistItems = checklistItems,
            aiAnalysis = aiAnalysis
        });
    }

    /// <summary>
    /// Staff approves document
    /// </summary>
    [HttpPost("{documentId}/approve")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> StaffApprove(int documentId, [FromBody] StaffReviewDto dto)
    {
        try
        {
            var userId = GetCurrentUserId();
            var firmId = GetFirmId();

            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.FirmID == firmId);

            if (document == null)
                return NotFound(new { success = false, message = "Document not found" });

            if (document.AssignedStaffId != userId)
                return BadRequest(new { success = false, message = "This document is not assigned to you" });

            // Prepare checklist results
            List<DocumentChecklistResult>? checklistResults = null;
            if (dto.ChecklistResults != null && dto.ChecklistResults.Any())
            {
                checklistResults = dto.ChecklistResults.Select(r => new DocumentChecklistResult
                {
                    ChecklistItemId = r.ChecklistItemId,
                    IsPassed = r.IsPassed,
                    Remarks = r.Remarks
                }).ToList();
            }

            var review = await _workflowService.StaffApproveAsync(
                documentId, userId, dto.Remarks, dto.InternalNotes, checklistResults);

            return Ok(new
            {
                success = true,
                message = "Document approved and forwarded to admin",
                reviewId = review.ReviewId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving document {DocumentId}", documentId);
            return StatusCode(500, new { success = false, message = "An error occurred while approving the document" });
        }
    }

    /// <summary>
    /// Staff rejects document
    /// </summary>
    [HttpPost("{documentId}/reject")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> StaffReject(int documentId, [FromBody] StaffReviewDto dto)
    {
        try
        {
            var userId = GetCurrentUserId();
            var firmId = GetFirmId();

            if (string.IsNullOrWhiteSpace(dto.Remarks))
                return BadRequest(new { success = false, message = "Rejection remarks are required" });

            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.FirmID == firmId);

            if (document == null)
                return NotFound(new { success = false, message = "Document not found" });

            if (document.AssignedStaffId != userId)
                return BadRequest(new { success = false, message = "This document is not assigned to you" });

            // Prepare checklist results
            List<DocumentChecklistResult>? checklistResults = null;
            if (dto.ChecklistResults != null && dto.ChecklistResults.Any())
            {
                checklistResults = dto.ChecklistResults.Select(r => new DocumentChecklistResult
                {
                    ChecklistItemId = r.ChecklistItemId,
                    IsPassed = r.IsPassed,
                    Remarks = r.Remarks
                }).ToList();
            }

            var review = await _workflowService.StaffRejectAsync(
                documentId, userId, dto.Remarks, checklistResults);

            return Ok(new
            {
                success = true,
                message = "Document rejected",
                reviewId = review.ReviewId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting document {DocumentId}", documentId);
            return StatusCode(500, new { success = false, message = "An error occurred while rejecting the document" });
        }
    }

    /// <summary>
    /// Staff edits document (creates new version)
    /// </summary>
    [HttpPost("{documentId}/edit")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> StaffEditDocument(int documentId, [FromForm] StaffEditDocumentDto dto)
    {
        try
        {
            var userId = GetCurrentUserId();
            var firmId = GetFirmId();

            if (dto.File == null || dto.File.Length == 0)
                return BadRequest(new { success = false, message = "No file provided" });

            if (string.IsNullOrWhiteSpace(dto.ChangeDescription))
                return BadRequest(new { success = false, message = "Change description is required" });

            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.FirmID == firmId);

            if (document == null)
                return NotFound(new { success = false, message = "Document not found" });

            // Staff can only edit documents assigned to them
            if (document.AssignedStaffId != userId)
                return BadRequest(new { success = false, message = "This document is not assigned to you" });

            // Save file
            var uploadPath = Path.Combine(_environment.ContentRootPath, "Uploads", firmId.ToString(), document.UploadedBy.ToString() ?? "0");
            Directory.CreateDirectory(uploadPath);

            var extension = Path.GetExtension(dto.File.FileName).ToLower();
            var uniqueFileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(uploadPath, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await dto.File.CopyToAsync(stream);
            }

            var version = await _workflowService.StaffEditDocumentAsync(
                documentId,
                userId,
                filePath,
                dto.File.FileName,
                dto.File.Length,
                dto.File.ContentType,
                dto.ChangeDescription);

            return Ok(new
            {
                success = true,
                message = "Document updated, new version created",
                versionId = version.VersionId,
                versionNumber = version.VersionNumber
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error editing document {DocumentId}", documentId);
            return StatusCode(500, new { success = false, message = "An error occurred while editing the document" });
        }
    }

    /// <summary>
    /// Admin approves document
    /// </summary>
    [HttpPost("{documentId}/admin-approve")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AdminApprove(int documentId, [FromBody] AdminReviewDto dto)
    {
        try
        {
            var userId = GetCurrentUserId();
            var firmId = GetFirmId();

            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.FirmID == firmId);

            if (document == null)
                return NotFound(new { success = false, message = "Document not found" });

            // Verify document is in correct stage - allow multiple valid stages
            var validStages = new[] { 
                DocumentWorkflowService.STAGE_PENDING_ADMIN_REVIEW, 
                DocumentWorkflowService.STAGE_ADMIN_REVIEW,
                DocumentWorkflowService.STAGE_STAFF_REVIEW,
                "StaffApproved" // In case status is used instead of stage
            };
            
            if (!validStages.Contains(document.WorkflowStage) && document.Status != "StaffApproved")
            {
                _logger.LogWarning("Document {DocumentId} has invalid stage {Stage} for admin approval", documentId, document.WorkflowStage);
                return BadRequest(new { success = false, message = $"Document is not ready for admin review. Current stage: {document.WorkflowStage}" });
            }

            DocumentReview review;
            DocumentRetention? retention = null;

            // If retention info is provided, use custom retention
            if (dto.RetentionYears.HasValue || dto.RetentionMonths.HasValue || dto.RetentionDays.HasValue || dto.PolicyId.HasValue)
            {
                var result = await _workflowService.AdminApproveWithRetentionAsync(
                    documentId, userId, dto.Remarks, 
                    dto.PolicyId, dto.RetentionYears, dto.RetentionMonths, dto.RetentionDays);
                review = result.review;
                retention = result.retention;
            }
            else
            {
                review = await _workflowService.AdminApproveAsync(documentId, userId, dto.Remarks);
                
                // Fetch the auto-created retention
                retention = await _context.DocumentRetentions
                    .FirstOrDefaultAsync(r => r.DocumentID == documentId);
            }

            // Save checklist results if provided
            if (dto.ChecklistResults != null && dto.ChecklistResults.Any())
            {
                foreach (var checklistResult in dto.ChecklistResults)
                {
                    // Only save valid checklist item IDs (positive numbers)
                    if (checklistResult.ChecklistItemId > 0)
                    {
                        var result = new DocumentChecklistResult
                        {
                            ReviewId = review.ReviewId,
                            ChecklistItemId = checklistResult.ChecklistItemId,
                            IsPassed = checklistResult.IsPassed,
                            Remarks = checklistResult.Remarks,
                            CheckedAt = DateTime.UtcNow
                        };
                        _context.DocumentChecklistResults.Add(result);
                    }
                }
                
                // Update review checklist status
                review.IsChecklistComplete = true;
                review.ChecklistScore = dto.ChecklistResults.Count(r => r.IsPassed);
                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                success = true,
                message = "Document approved and completed",
                reviewId = review.ReviewId,
                retention = retention != null ? new
                {
                    retentionId = retention.RetentionID,
                    startDate = retention.RetentionStartDate,
                    expiryDate = retention.ExpiryDate,
                    retentionYears = retention.RetentionYears,
                    retentionMonths = retention.RetentionMonths,
                    retentionDays = retention.RetentionDays
                } : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error admin approving document {DocumentId}: {Message}", documentId, ex.Message);
            return StatusCode(500, new { success = false, message = $"An error occurred while approving the document: {ex.Message}" });
        }
    }

    /// <summary>
    /// Get default retention policy for document type
    /// </summary>
    [HttpGet("{documentId}/default-retention")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetDefaultRetention(int documentId)
    {
        var firmId = GetFirmId();

        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.FirmID == firmId);

        if (document == null)
            return NotFound(new { success = false, message = "Document not found" });

        // Find default policy for this document type
        var policy = await _context.RetentionPolicies
            .FirstOrDefaultAsync(p => p.FirmId == firmId && 
                                      p.DocumentType == document.DocumentType && 
                                      p.IsDefault == true && 
                                      p.IsActive == true);

        // Get all available policies for the firm
        var policies = await _context.RetentionPolicies
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
                isDefault = p.IsDefault
            })
            .ToListAsync();

        return Ok(new
        {
            success = true,
            documentType = document.DocumentType,
            hasDefaultPolicy = policy != null,
            defaultPolicy = policy != null ? new
            {
                policyId = (int?)policy.PolicyID,
                policyName = policy.PolicyName ?? "Default",
                retentionYears = policy.RetentionYears ?? 7,
                retentionMonths = policy.RetentionMonths ?? 0,
                retentionDays = policy.RetentionDays ?? 0
            } : new
            {
                policyId = (int?)null,
                policyName = "Default (7 years)",
                retentionYears = 7,
                retentionMonths = 0,
                retentionDays = 0
            },
            availablePolicies = policies
        });
    }

    /// <summary>
    /// Admin rejects document
    /// </summary>
    [HttpPost("{documentId}/admin-reject")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AdminReject(int documentId, [FromBody] AdminReviewDto dto)
    {
        try
        {
            var userId = GetCurrentUserId();
            var firmId = GetFirmId();

            if (string.IsNullOrWhiteSpace(dto.Remarks))
                return BadRequest(new { success = false, message = "Rejection remarks are required" });

            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.FirmID == firmId);

            if (document == null)
                return NotFound(new { success = false, message = "Document not found" });

            if (document.WorkflowStage != DocumentWorkflowService.STAGE_PENDING_ADMIN_REVIEW &&
                document.WorkflowStage != DocumentWorkflowService.STAGE_ADMIN_REVIEW)
                return BadRequest(new { success = false, message = "Document is not pending admin review" });

            var review = await _workflowService.AdminRejectAsync(documentId, userId, dto.Remarks);

            return Ok(new
            {
                success = true,
                message = "Document rejected",
                reviewId = review.ReviewId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error admin rejecting document {DocumentId}", documentId);
            return StatusCode(500, new { success = false, message = "An error occurred while rejecting the document" });
        }
    }

    /// <summary>
    /// Get review statistics
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();
        var role = GetUserRole();

        var stats = new
        {
            pendingCount = role == "Staff"
                ? await _context.Documents.CountAsync(d => d.FirmID == firmId &&
                    (d.WorkflowStage == DocumentWorkflowService.STAGE_PENDING_STAFF_REVIEW ||
                     d.WorkflowStage == DocumentWorkflowService.STAGE_STAFF_REVIEW) &&
                    d.AssignedStaffId == userId)
                : await _context.Documents.CountAsync(d => d.FirmID == firmId &&
                    (d.WorkflowStage == DocumentWorkflowService.STAGE_PENDING_ADMIN_REVIEW ||
                     d.WorkflowStage == DocumentWorkflowService.STAGE_ADMIN_REVIEW)),

            approvedToday = await _context.DocumentReviews.CountAsync(r =>
                r.ReviewedBy == userId &&
                r.ReviewerRole == role &&
                r.ReviewStatus == "Approved" &&
                r.ReviewedAt.HasValue &&
                r.ReviewedAt.Value.Date == DateTime.UtcNow.Date),

            rejectedToday = await _context.DocumentReviews.CountAsync(r =>
                r.ReviewedBy == userId &&
                r.ReviewerRole == role &&
                r.ReviewStatus == "Rejected" &&
                r.ReviewedAt.HasValue &&
                r.ReviewedAt.Value.Date == DateTime.UtcNow.Date),

            totalReviewed = await _context.DocumentReviews.CountAsync(r =>
                r.ReviewedBy == userId &&
                r.ReviewerRole == role)
        };

        return Ok(new { success = true, stats });
    }
}

// DTOs
public class StaffReviewDto
{
    public string? Remarks { get; set; }
    public string? InternalNotes { get; set; }
    public List<ChecklistResultDto>? ChecklistResults { get; set; }
}

public class ChecklistResultDto
{
    public int ChecklistItemId { get; set; }
    public bool IsPassed { get; set; }
    public string? Remarks { get; set; }
}

public class StaffEditDocumentDto
{
    public IFormFile? File { get; set; }
    public string? ChangeDescription { get; set; }
}

public class AdminReviewDto
{
    public string? Remarks { get; set; }
    public int? PolicyId { get; set; }
    public int? RetentionYears { get; set; }
    public int? RetentionMonths { get; set; }
    public int? RetentionDays { get; set; }
    public List<ChecklistResultDto>? ChecklistResults { get; set; }
}
