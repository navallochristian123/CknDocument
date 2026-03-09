using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;
using CKNDocument.Services;
using System.Security.Claims;

namespace CKNDocument.Controllers.Api;

/// <summary>
/// API Controller for Document operations
/// Handles upload, download, versioning, and workflow
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "FirmMember")]
public class DocumentApiController : ControllerBase
{
    private readonly LawFirmDMSDbContext _context;
    private readonly DocumentWorkflowService _workflowService;
    private readonly NotificationService _notificationService;
    private readonly DocumentAIService _aiService;
    private readonly AuditLogService _auditLogService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DocumentApiController> _logger;

    // Allowed file extensions
    private readonly string[] _allowedExtensions = { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".jpg", ".jpeg", ".png", ".gif" };
    private const long MaxFileSize = 50 * 1024 * 1024; // 50MB

    public DocumentApiController(
        LawFirmDMSDbContext context,
        DocumentWorkflowService workflowService,
        NotificationService notificationService,
        DocumentAIService aiService,
        AuditLogService auditLogService,
        IWebHostEnvironment environment,
        ILogger<DocumentApiController> logger)
    {
        _context = context;
        _workflowService = workflowService;
        _notificationService = notificationService;
        _aiService = aiService;
        _auditLogService = auditLogService;
        _environment = environment;
        _logger = logger;
    }

    private int GetCurrentUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    private int GetFirmId() => int.Parse(User.FindFirst("FirmId")?.Value ?? "0");
    private string GetUserRole() => User.FindFirst(ClaimTypes.Role)?.Value ?? "Client";

    /// <summary>
    /// Upload a new document (Client)
    /// </summary>
    [HttpPost("upload")]
    [Authorize(Policy = "FirmMember")]
    public async Task<IActionResult> Upload([FromForm] DocumentUploadDto dto)
    {
        try
        {
            if (dto.File == null || dto.File.Length == 0)
                return BadRequest(new { success = false, message = "No file provided" });

            if (dto.File.Length > MaxFileSize)
                return BadRequest(new { success = false, message = "File size exceeds 50MB limit" });

            var extension = Path.GetExtension(dto.File.FileName).ToLower();
            if (!_allowedExtensions.Contains(extension))
                return BadRequest(new { success = false, message = "File type not allowed. Allowed types: " + string.Join(", ", _allowedExtensions) });

            var userId = GetCurrentUserId();
            var firmId = GetFirmId();
            var role = GetUserRole();

            _logger.LogInformation("Upload attempt by UserId: {UserId}, FirmId: {FirmId}, Role: {Role}", userId, firmId, role);

            if (userId == 0)
                return BadRequest(new { success = false, message = "User not authenticated properly" });

            if (firmId == 0)
                return BadRequest(new { success = false, message = "User is not associated with a law firm" });

            // Check firm storage limit based on subscription plan
            var firm = await _context.Firms.AsNoTracking().FirstOrDefaultAsync(f => f.FirmID == firmId);
            if (firm == null)
                return BadRequest(new { success = false, message = "Law firm not found" });

            var maxStorageBytes = firm.MaxStorageMB * 1024L * 1024L; // Convert MB to bytes
            var currentStorageUsed = await _context.Documents
                .Where(d => d.FirmID == firmId)
                .SumAsync(d => (long)(d.TotalFileSize ?? 0));

            var newTotalStorage = currentStorageUsed + dto.File.Length;
            if (newTotalStorage > maxStorageBytes)
            {
                var usedGB = Math.Round(currentStorageUsed / (1024.0 * 1024.0 * 1024.0), 2);
                var maxGB = Math.Round(firm.MaxStorageMB / 1024.0, 1);
                var fileSizeMB = Math.Round(dto.File.Length / (1024.0 * 1024.0), 2);
                return BadRequest(new { 
                    success = false, 
                    message = $"Storage limit exceeded. Your firm's plan allows {maxGB} GB of storage. Currently used: {usedGB} GB. This file ({fileSizeMB} MB) would exceed the limit. Please contact your administrator to upgrade the subscription plan."
                });
            }

            // Validate folder belongs to user (only for clients)
            if (dto.FolderId.HasValue && role == "Client")
            {
                var folder = await _context.ClientFolders
                    .FirstOrDefaultAsync(f => f.FolderId == dto.FolderId && f.ClientId == userId);
                if (folder == null)
                    return BadRequest(new { success = false, message = "Invalid folder" });
            }
            else if (dto.FolderId.HasValue)
            {
                // For staff/admin, just verify folder exists in the firm
                var folder = await _context.ClientFolders
                    .FirstOrDefaultAsync(f => f.FolderId == dto.FolderId && f.FirmId == firmId);
                if (folder == null)
                    return BadRequest(new { success = false, message = "Invalid folder" });
            }

            // Create storage directory
            var uploadPath = Path.Combine(_environment.ContentRootPath, "Uploads", firmId.ToString(), userId.ToString());
            Directory.CreateDirectory(uploadPath);

            // Generate unique filename
            var uniqueFileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(uploadPath, uniqueFileName);

            // Save file
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await dto.File.CopyToAsync(stream);
            }

            // Create document record
            var document = new Document
            {
                FirmID = firmId,
                Title = dto.Title ?? Path.GetFileNameWithoutExtension(dto.File.FileName),
                Description = dto.Description,
                Category = dto.Category,
                Status = "Pending",
                UploadedBy = userId,
                FolderId = dto.FolderId,
                DocumentType = dto.DocumentType,
                WorkflowStage = DocumentWorkflowService.STAGE_CLIENT_UPLOAD,
                OriginalFileName = dto.File.FileName,
                FileExtension = extension,
                MimeType = dto.File.ContentType,
                TotalFileSize = dto.File.Length,
                CurrentVersion = 1,
                IsAIProcessed = false,
                IsDuplicate = false,
                IsHighRisk = dto.IsHighRisk,
                CreatedAt = DateTime.UtcNow
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            // Create initial version
            var version = new DocumentVersion
            {
                DocumentId = document.DocumentID,
                VersionNumber = 1,
                VersionLabel = "1",
                FilePath = filePath,
                FileSize = dto.File.Length,
                UploadedBy = userId,
                OriginalFileName = dto.File.FileName,
                FileExtension = extension,
                MimeType = dto.File.ContentType,
                ChangeDescription = "Initial upload",
                ChangedBy = "Client",
                IsCurrentVersion = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.DocumentVersions.Add(version);
            await _context.SaveChangesAsync();

            // Process with AI (non-blocking - upload succeeds even if AI fails)
            try
            {
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    var aiResult = await _aiService.ProcessDocumentAsync(document.DocumentID, fileStream, dto.File.FileName);
                    
                    // Ensure checklist items exist for detected document type
                    if (aiResult.Success && !string.IsNullOrEmpty(aiResult.DetectedDocumentType))
                    {
                        await _aiService.EnsureChecklistItemsExistAsync(firmId, aiResult.DetectedDocumentType);
                    }
                }
                _logger.LogInformation("AI processing completed for document {DocumentId}", document.DocumentID);
            }
            catch (Exception aiEx)
            {
                _logger.LogWarning(aiEx, "AI processing failed for document {DocumentId}, upload will continue without AI analysis", document.DocumentID);
            }

            // Route document based on risk level (wrapped in try-catch so upload succeeds even if assignment fails)
            User? assignedUser = null;
            try
            {
                if (dto.IsHighRisk)
                {
                    // HIGH-RISK: Skip staff, assign directly to lawyer
                    assignedUser = await _workflowService.AssignToLawyerAsync(document.DocumentID, firmId);

                    // Notify the assigned lawyer about high-risk document
                    if (assignedUser != null)
                    {
                        await _notificationService.NotifyAsync(
                            assignedUser.UserID,
                        "âš ï¸ High-Risk Document for Review",
                            $"A high-risk document '{document.Title}' has been uploaded and requires your immediate review.",
                            "HighRiskDocument",
                            document.DocumentID,
                            $"/Lawyer/PendingReviews");
                    }
    
                    // Also notify client about the high-risk routing
                    await _notificationService.NotifyAsync(
                        userId,
                        "High-Risk Document Submitted",
                        $"Your high-risk document '{document.Title}' has been sent directly to a lawyer for immediate review.",
                        "HighRiskDocument",
                        document.DocumentID,
                        $"/Document/MyDocuments");
                }
                else
                {
                    // NORMAL FLOW: Assign to staff for review
                    assignedUser = await _workflowService.AssignToStaffAsync(document.DocumentID, firmId);
    
                    // Notify all staff members
                    await _notificationService.NotifyAllStaffAsync(
                        firmId,
                        "New Document Pending Review",
                        $"Client uploaded a new document: {document.Title}",
                        NotificationService.TYPE_DOCUMENT_PENDING_REVIEW,
                        document.DocumentID,
                        $"/Review/Review/{document.DocumentID}");
                }
            }
            catch (Exception assignEx)
            {
                _logger.LogWarning(assignEx, "Failed to assign/notify for document {DocumentId}. Document saved but assignment may need manual action.", document.DocumentID);
                // Document is already saved — it will still appear in the staff review list as unassigned
            }

            // Audit log
            try
            {
                await _auditLogService.LogAsync(
                    "DocumentUpload",
                    "Document",
                    document.DocumentID,
                    $"Client uploaded document: {document.Title}{(dto.IsHighRisk ? " [HIGH-RISK]" : "")}",
                    null,
                    dto.IsHighRisk ? "{\"isHighRisk\":true}" : null,
                    "DocumentUpload");
            }
            catch (Exception auditEx)
            {
                _logger.LogWarning(auditEx, "Failed to log audit for document upload {DocumentId}", document.DocumentID);
            }

            return Ok(new
            {
                success = true,
                message = dto.IsHighRisk 
                    ? "High-risk document uploaded and sent directly to lawyer for review" 
                    : "Document uploaded successfully",
                documentId = document.DocumentID,
                assignedTo = assignedUser?.FullName,
                isHighRisk = dto.IsHighRisk
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading document. InnerException: {Inner}", ex.InnerException?.Message);
            var errorMessage = "An error occurred while uploading the document";
#if DEBUG
            errorMessage = $"{errorMessage}: {ex.Message}";
            if (ex.InnerException != null)
            {
                errorMessage += $" | Inner: {ex.InnerException.Message}";
            }
#endif
            return StatusCode(500, new { success = false, message = errorMessage });
        }
    }

    /// <summary>
    /// Manual upload by Admin, Lawyer, or Staff (no client account needed)
    /// Records who performed the manual upload for audit trail
    /// </summary>
    [HttpPost("manual-upload")]
    [Authorize(Policy = "AdminOrStaff")]
    public async Task<IActionResult> ManualUpload([FromForm] ManualUploadDto dto)
    {
        try
        {
            if (dto.File == null || dto.File.Length == 0)
                return BadRequest(new { success = false, message = "No file provided" });

            if (dto.File.Length > MaxFileSize)
                return BadRequest(new { success = false, message = "File size exceeds 50MB limit" });

            var extension = Path.GetExtension(dto.File.FileName).ToLower();
            if (!_allowedExtensions.Contains(extension))
                return BadRequest(new { success = false, message = "File type not allowed. Allowed types: " + string.Join(", ", _allowedExtensions) });

            var userId = GetCurrentUserId();
            var firmId = GetFirmId();
            var role = GetUserRole();

            if (userId == 0 || firmId == 0)
                return BadRequest(new { success = false, message = "User not authenticated properly" });

            // Check firm storage limit
            var firm = await _context.Firms.AsNoTracking().FirstOrDefaultAsync(f => f.FirmID == firmId);
            if (firm == null)
                return BadRequest(new { success = false, message = "Law firm not found" });

            var maxStorageBytes = firm.MaxStorageMB * 1024L * 1024L;
            var currentStorageUsed = await _context.Documents
                .Where(d => d.FirmID == firmId)
                .SumAsync(d => (long)(d.TotalFileSize ?? 0));

            if (currentStorageUsed + dto.File.Length > maxStorageBytes)
            {
                var usedGB = Math.Round(currentStorageUsed / (1024.0 * 1024.0 * 1024.0), 2);
                var maxGB = Math.Round(firm.MaxStorageMB / 1024.0, 1);
                return BadRequest(new { success = false, message = $"Storage limit exceeded. Used: {usedGB} GB / {maxGB} GB." });
            }

            // Validate folder if provided
            if (dto.FolderId.HasValue)
            {
                var folder = await _context.ClientFolders
                    .FirstOrDefaultAsync(f => f.FolderId == dto.FolderId && f.FirmId == firmId);
                if (folder == null)
                    return BadRequest(new { success = false, message = "Invalid folder" });
            }

            // Validate client if provided (optional - manual upload may not have a client)
            if (dto.OnBehalfOfClientId.HasValue)
            {
                var client = await _context.Users
                    .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.UserID == dto.OnBehalfOfClientId && u.FirmID == firmId);
                if (client == null)
                    return BadRequest(new { success = false, message = "Invalid client selected" });
            }

            // The document is stored under the uploader (staff/lawyer/admin) path
            var uploadPath = Path.Combine(_environment.ContentRootPath, "Uploads", firmId.ToString(), userId.ToString());
            Directory.CreateDirectory(uploadPath);

            var uniqueFileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(uploadPath, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await dto.File.CopyToAsync(stream);
            }

            // Get uploader info for recording
            var uploader = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserID == userId);
            var uploaderName = uploader != null ? $"{uploader.FirstName} {uploader.LastName}".Trim() : "Unknown";

            // UploadedBy = the staff/lawyer/admin who manually uploaded
            var document = new Document
            {
                FirmID = firmId,
                Title = dto.Title ?? Path.GetFileNameWithoutExtension(dto.File.FileName),
                Description = dto.Description + (string.IsNullOrWhiteSpace(dto.ClientName) 
                    ? $"\n[Manual upload by {role}: {uploaderName}]" 
                    : $"\n[Manual upload by {role}: {uploaderName}, on behalf of: {dto.ClientName}]"),
                Category = dto.Category,
                Status = "Pending",
                UploadedBy = dto.OnBehalfOfClientId ?? userId,
                FolderId = dto.FolderId,
                DocumentType = dto.DocumentType,
                WorkflowStage = DocumentWorkflowService.STAGE_CLIENT_UPLOAD,
                OriginalFileName = dto.File.FileName,
                FileExtension = extension,
                MimeType = dto.File.ContentType,
                TotalFileSize = dto.File.Length,
                CurrentVersion = 1,
                IsAIProcessed = false,
                IsDuplicate = false,
                IsHighRisk = dto.IsHighRisk,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = $"{role}:{uploaderName} (Manual Upload)"
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            // Create initial version - record the actual uploader role
            var version = new DocumentVersion
            {
                DocumentId = document.DocumentID,
                VersionNumber = 1,
                VersionLabel = "1",
                FilePath = filePath,
                FileSize = dto.File.Length,
                UploadedBy = userId,
                OriginalFileName = dto.File.FileName,
                FileExtension = extension,
                MimeType = dto.File.ContentType,
                ChangeDescription = $"Manual upload by {role}: {uploaderName}" + 
                    (string.IsNullOrWhiteSpace(dto.ClientName) ? "" : $" (on behalf of {dto.ClientName})"),
                ChangedBy = role,
                IsCurrentVersion = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.DocumentVersions.Add(version);
            await _context.SaveChangesAsync();

            // AI Processing (non-blocking)
            try
            {
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    var aiResult = await _aiService.ProcessDocumentAsync(document.DocumentID, fileStream, dto.File.FileName);
                    if (aiResult.Success && !string.IsNullOrEmpty(aiResult.DetectedDocumentType))
                        await _aiService.EnsureChecklistItemsExistAsync(firmId, aiResult.DetectedDocumentType);
                }
            }
            catch (Exception aiEx)
            {
                _logger.LogWarning(aiEx, "AI processing failed for manual upload document {DocumentId}", document.DocumentID);
            }

            // Workflow routing based on uploader role:
            // Staff uploads → goes to Lawyer review
            // Lawyer uploads → goes directly to Admin review
            // Admin uploads → goes to Lawyer review (standard flow)
            User? assignedUser = null;
            string assignedToRole = "";
            try
            {
                if (role == "Staff")
                {
                    // Staff manual upload → assign to Lawyer
                    assignedUser = await _workflowService.AssignToLawyerAsync(document.DocumentID, firmId);
                    assignedToRole = "Lawyer";
                    if (assignedUser != null)
                    {
                        await _notificationService.NotifyAsync(
                            assignedUser.UserID,
                            "📄 New Document for Review (Manual Upload)",
                            $"Staff {uploaderName} manually uploaded document '{document.Title}' for your review.",
                            "DocumentPendingReview",
                            document.DocumentID,
                            "/Lawyer/PendingReviews");
                    }
                }
                else if (role == "Lawyer")
                {
                    // Lawyer manual upload → assign directly to Admin
                    assignedUser = await _workflowService.AssignToAdminAsync(document.DocumentID, firmId);
                    assignedToRole = "Admin";
                    if (assignedUser != null)
                    {
                        await _notificationService.NotifyAsync(
                            assignedUser.UserID,
                            "📄 New Document for Admin Review (Manual Upload)",
                            $"Lawyer {uploaderName} manually uploaded document '{document.Title}' for your approval.",
                            "DocumentPendingReview",
                            document.DocumentID,
                            "/Admin/Documents");
                    }
                }
                else if (role == "Admin")
                {
                    // Admin manual upload → assign to Lawyer for review first
                    assignedUser = await _workflowService.AssignToLawyerAsync(document.DocumentID, firmId);
                    assignedToRole = "Lawyer";
                    if (assignedUser != null)
                    {
                        await _notificationService.NotifyAsync(
                            assignedUser.UserID,
                            "📄 New Document for Review (Manual Upload by Admin)",
                            $"Admin {uploaderName} manually uploaded document '{document.Title}' for your review.",
                            "DocumentPendingReview",
                            document.DocumentID,
                            "/Lawyer/PendingReviews");
                    }
                }
            }
            catch (Exception assignEx)
            {
                _logger.LogWarning(assignEx, "Failed to assign/notify for manual upload document {DocumentId}", document.DocumentID);
            }

            // Audit log
            try
            {
                await _auditLogService.LogAsync(
                    "ManualDocumentUpload",
                    "Document",
                    document.DocumentID,
                    $"{role} {uploaderName} manually uploaded document: {document.Title}" +
                        (dto.OnBehalfOfClientId.HasValue ? $" (on behalf of client ID {dto.OnBehalfOfClientId})" : "") +
                        (!string.IsNullOrWhiteSpace(dto.ClientName) ? $" (client: {dto.ClientName})" : "") +
                        (dto.IsHighRisk ? " [HIGH-RISK]" : ""),
                    null,
                    System.Text.Json.JsonSerializer.Serialize(new { 
                        manualUpload = true, 
                        uploadedByUserId = userId, 
                        uploadedByRole = role, 
                        uploadedByName = uploaderName,
                        onBehalfOfClientId = dto.OnBehalfOfClientId,
                        clientName = dto.ClientName,
                        isHighRisk = dto.IsHighRisk 
                    }),
                    "ManualDocumentUpload");
            }
            catch (Exception auditEx)
            {
                _logger.LogWarning(auditEx, "Failed to log audit for manual upload {DocumentId}", document.DocumentID);
            }

            return Ok(new
            {
                success = true,
                message = $"Document manually uploaded by {role} successfully" + 
                    (assignedUser != null ? $". Assigned to {assignedToRole}: {assignedUser.FullName}" : ""),
                documentId = document.DocumentID,
                assignedTo = assignedUser?.FullName,
                assignedToRole = assignedToRole,
                isHighRisk = dto.IsHighRisk,
                manualUploadBy = uploaderName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in manual upload");
            return StatusCode(500, new { success = false, message = "An error occurred during manual upload" });
        }
    }

    /// <summary>
    /// Get document details
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetDocument(int id)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();
        var role = GetUserRole();

        var document = await _context.Documents
            .Include(d => d.Uploader)
            .Include(d => d.Folder)
                .ThenInclude(f => f!.Client)
            .Include(d => d.AssignedStaff)
            .Include(d => d.AssignedAdmin)
            .Include(d => d.Versions.OrderByDescending(v => v.VersionNumber))
            .Include(d => d.Reviews.OrderByDescending(r => r.ReviewedAt))
                .ThenInclude(r => r.Reviewer)
            .FirstOrDefaultAsync(d => d.DocumentID == id && d.FirmID == firmId);

        if (document == null)
            return NotFound(new { success = false, message = "Document not found" });

        // Check access permissions
        if (role == "Client" && document.UploadedBy != userId)
            return Forbid();

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
                totalFileSize = document.TotalFileSize,
                isAIProcessed = document.IsAIProcessed,
                isDuplicate = document.IsDuplicate,
                currentRemarks = document.CurrentRemarks,
                uploader = new { id = document.Uploader?.UserID, name = document.Uploader?.FullName },
                uploaderName = document.Uploader?.FullName,
                clientName = document.Folder?.Client?.FullName ?? document.Uploader?.FullName,
                folder = document.Folder != null ? new { id = document.Folder.FolderId, name = document.Folder.FolderName, clientName = document.Folder.Client?.FullName } : null,
                assignedStaff = document.AssignedStaff != null ? new { id = document.AssignedStaff.UserID, name = document.AssignedStaff.FullName } : null,
                assignedAdmin = document.AssignedAdmin != null ? new { id = document.AssignedAdmin.UserID, name = document.AssignedAdmin.FullName } : null,
                createdAt = document.CreatedAt,
                staffReviewedAt = document.StaffReviewedAt,
                adminReviewedAt = document.AdminReviewedAt,
                approvedAt = document.ApprovedAt,
                versions = document.Versions.Select(v => new
                {
                    versionId = v.VersionId,
                    versionNumber = v.VersionNumber,
                    versionLabel = v.VersionLabel ?? v.VersionNumber.ToString(),
                    originalFileName = v.OriginalFileName,
                    fileSize = v.FileSize,
                    changeDescription = v.ChangeDescription,
                    changedBy = v.ChangedBy,
                    isCurrentVersion = v.IsCurrentVersion,
                    createdAt = v.CreatedAt
                }),
                reviews = document.Reviews.Select(r => new
                {
                    reviewId = r.ReviewId,
                    reviewStatus = r.ReviewStatus,
                    remarks = r.Remarks,
                    reviewerRole = r.ReviewerRole,
                    reviewerName = r.Reviewer?.FullName,
                    reviewerEmail = r.Reviewer?.Email,
                    reviewedAt = r.ReviewedAt,
                    isChecklistComplete = r.IsChecklistComplete,
                    checklistScore = r.ChecklistScore
                })
            }
        });
    }

    /// <summary>
    /// Download document file
    /// </summary>
    [HttpGet("{id}/download")]
    public async Task<IActionResult> Download(int id, [FromQuery] int? versionId = null)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();
        var role = GetUserRole();

        var document = await _context.Documents
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.DocumentID == id && d.FirmID == firmId);

        if (document == null)
            return NotFound(new { success = false, message = "Document not found" });

        // Check access permissions
        if (role == "Client" && document.UploadedBy != userId)
            return Forbid();

        DocumentVersion? version;
        if (versionId.HasValue)
        {
            version = document.Versions.FirstOrDefault(v => v.VersionId == versionId);
        }
        else
        {
            version = document.Versions.FirstOrDefault(v => v.IsCurrentVersion == true) 
                ?? document.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        }

        if (version == null || string.IsNullOrEmpty(version.FilePath))
            return NotFound(new { success = false, message = "File not found" });

        var resolvedDownloadPath = ResolveVersionFilePath(version.FilePath);
        if (resolvedDownloadPath == null)
            return NotFound(new { success = false, message = "File not found on server" });

        var fileBytes = await System.IO.File.ReadAllBytesAsync(resolvedDownloadPath);
        var contentType = version.MimeType ?? "application/octet-stream";
        var fileName = version.OriginalFileName ?? $"document{version.FileExtension}";

        // Audit log
        await _auditLogService.LogAsync(
            "DocumentDownload",
            "Document",
            id,
            $"Downloaded document: {document.Title} (Version {version.VersionNumber})",
            null,
            null,
            "DocumentAccess");

        return File(fileBytes, contentType, fileName);
    }

    /// <summary>
    /// Get client's documents
    /// </summary>
    [HttpGet("my-documents")]
    [Authorize(Policy = "ClientOnly")]
    public async Task<IActionResult> GetMyDocuments([FromQuery] int? folderId = null, [FromQuery] string? status = null)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        var query = _context.Documents
            .Include(d => d.Folder)
            .Include(d => d.Versions.OrderByDescending(v => v.VersionNumber).Take(1))
            .Where(d => d.FirmID == firmId && d.UploadedBy == userId && d.WorkflowStage != "Archived");

        if (folderId.HasValue)
        {
            query = query.Where(d => d.FolderId == folderId);
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(d => d.Status == status);
        }

        var documents = await query
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new
            {
                id = d.DocumentID,
                title = d.Title,
                description = d.Description,
                category = d.Category,
                documentType = d.DocumentType,
                status = d.Status,
                workflowStage = d.WorkflowStage,
                currentVersion = d.CurrentVersion,
                currentVersionLabel = d.Versions
                    .Where(v => v.IsCurrentVersion == true)
                    .Select(v => v.VersionLabel)
                    .FirstOrDefault() ?? d.CurrentVersion.ToString(),
                originalFileName = d.Versions
                    .Where(v => v.IsCurrentVersion == true)
                    .Select(v => v.OriginalFileName)
                    .FirstOrDefault() ?? d.OriginalFileName,
                fileExtension = d.FileExtension,
                totalFileSize = d.TotalFileSize,
                currentRemarks = d.CurrentRemarks,
                folder = d.Folder != null ? new { id = d.Folder.FolderId, name = d.Folder.FolderName } : null,
                createdAt = d.CreatedAt
            })
            .ToListAsync();

        return Ok(new { success = true, documents });
    }

    /// <summary>
    /// Search client's documents and folders
    /// Allows filtering by status, type, folder, and search term
    /// </summary>
    [HttpGet("search/client")]
    [Authorize(Policy = "FirmMember")]
    public async Task<IActionResult> SearchClientDocuments(
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? type = null,
        [FromQuery] int? folderId = null)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();
        var role = GetUserRole();

        // Build documents query
        var docsQuery = _context.Documents
            .Include(d => d.Folder)
            .Include(d => d.Uploader)
            .Where(d => d.FirmID == firmId && d.WorkflowStage != "Archived");

        // Clients only see their own documents
        if (role == "Client")
        {
            docsQuery = docsQuery.Where(d => d.UploadedBy == userId);
        }

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            docsQuery = docsQuery.Where(d => 
                (d.Title != null && d.Title.ToLower().Contains(searchLower)) ||
                (d.OriginalFileName != null && d.OriginalFileName.ToLower().Contains(searchLower)) ||
                (d.Description != null && d.Description.ToLower().Contains(searchLower)) ||
                (d.DocumentType != null && d.DocumentType.ToLower().Contains(searchLower)));
        }

        // Apply status filter
        if (!string.IsNullOrWhiteSpace(status))
        {
            docsQuery = docsQuery.Where(d => d.Status == status);
        }

        // Apply type filter
        if (!string.IsNullOrWhiteSpace(type))
        {
            docsQuery = docsQuery.Where(d => d.DocumentType == type);
        }

        // Apply folder filter
        if (folderId.HasValue)
        {
            docsQuery = docsQuery.Where(d => d.FolderId == folderId);
        }

        var documents = await docsQuery
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new
            {
                id = d.DocumentID,
                title = d.Title,
                description = d.Description,
                category = d.Category,
                documentType = d.DocumentType,
                status = d.Status,
                workflowStage = d.WorkflowStage,
                originalFileName = d.OriginalFileName,
                fileExtension = d.FileExtension,
                totalFileSize = d.TotalFileSize,
                folderId = d.FolderId,
                folderName = d.Folder != null ? d.Folder.FolderName : null,
                uploaderName = d.Uploader != null ? (d.Uploader.FirstName ?? "") + " " + (d.Uploader.LastName ?? "") : null,
                createdAt = d.CreatedAt
            })
            .ToListAsync();

        // Get folders for this user
        var foldersQuery = _context.ClientFolders
            .Include(f => f.Documents.Where(d => d.WorkflowStage != "Archived"))
            .Where(f => f.FirmId == firmId);

        if (role == "Client")
        {
            foldersQuery = foldersQuery.Where(f => f.ClientId == userId);
        }

        // Apply search to folders too
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            foldersQuery = foldersQuery.Where(f => 
                f.FolderName.ToLower().Contains(searchLower) ||
                (f.Description != null && f.Description.ToLower().Contains(searchLower)));
        }

        var folders = await foldersQuery
            .OrderBy(f => f.FolderName)
            .Select(f => new
            {
                id = f.FolderId,
                name = f.FolderName,
                description = f.Description,
                color = f.Color,
                documentCount = f.Documents.Count,
                createdAt = f.CreatedAt
            })
            .ToListAsync();

        // Log the search action
        await _auditLogService.LogAsync(
            "DocumentSearch",
            "Search",
            null,
            $"Searched documents with term: '{search}', status: '{status}', type: '{type}'",
            null,
            null,
            "DocumentManagement");

        return Ok(new { success = true, documents, folders });
    }

    /// <summary>
    /// Archive document (Client can archive their own)
    /// </summary>
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> ArchiveDocument(int id, [FromBody] DocumentArchiveDto? dto = null)
    {
        try
        {
            var userId = GetCurrentUserId();
            var firmId = GetFirmId();
            var role = GetUserRole();

            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.DocumentID == id && d.FirmID == firmId);

            if (document == null)
                return NotFound(new { success = false, message = "Document not found" });

            // Check permissions
            if (role == "Client" && document.UploadedBy != userId)
                return StatusCode(403, new { success = false, message = "You don't have permission to archive this document" });

            // Clients can archive approved, completed, OR rejected documents
            if (role == "Client" && document.Status != "Completed" && document.Status != "Approved" && document.Status != "Rejected")
                return BadRequest(new { success = false, message = "Only approved, completed, or rejected documents can be archived" });

            // Check for existing non-restored archive
            var existingArchive = await _context.Archives
                .FirstOrDefaultAsync(a => a.DocumentID == id && a.IsRestored != true);
            
            if (existingArchive != null)
            {
                // For rejected docs that were auto-archived, just update the document status to Archived
                // since the archive record already exists
                if (document.Status == "Rejected" && existingArchive.ArchiveType == "Rejected")
                {
                    // Use ExecuteUpdateAsync to avoid UpdatedAt column issue
                    await _context.Documents
                        .Where(d => d.DocumentID == id)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(d => d.Status, "Archived")
                            .SetProperty(d => d.WorkflowStage, "Archived"));
                    
                    return Ok(new { success = true, message = "Document archived successfully", archiveId = existingArchive.ArchiveID });
                }
                
                return BadRequest(new { success = false, message = "This document is already archived" });
            }

            var archive = await _workflowService.ArchiveDocumentAsync(id, userId, dto?.Reason ?? "Archived by user", "Manual");

            return Ok(new { success = true, message = "Document archived successfully", archiveId = archive.ArchiveID });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error archiving document {Id}. InnerException: {Inner}", id, ex.InnerException?.Message);
            var errorMsg = "An error occurred while archiving the document";
#if DEBUG
            errorMsg = $"{errorMsg}: {ex.Message}";
            if (ex.InnerException != null)
                errorMsg += $" | Inner: {ex.InnerException.Message}";
#endif
            return StatusCode(500, new { success = false, message = errorMsg });
        }
    }

    /// <summary>
    /// Edit document metadata
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateDocument(int id, [FromBody] DocumentUpdateDto dto)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();
        var role = GetUserRole();

        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.DocumentID == id && d.FirmID == firmId);

        if (document == null)
            return NotFound(new { success = false, message = "Document not found" });

        // Check permissions
        if (role == "Client" && document.UploadedBy != userId)
            return Forbid();

        // Update metadata
        if (!string.IsNullOrEmpty(dto.Title))
            document.Title = dto.Title;
        if (!string.IsNullOrEmpty(dto.Description))
            document.Description = dto.Description;
        if (!string.IsNullOrEmpty(dto.Category))
            document.Category = dto.Category;
        if (!string.IsNullOrEmpty(dto.DocumentType))
            document.DocumentType = dto.DocumentType;
        if (dto.Tags != null)
            document.Tags = dto.Tags;


        // â”€â”€ Staff metadata edit â†’ create a minor version snapshot â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Whenever Staff (or Admin) edits while the document is in a review stage,
        // record a new DocumentVersion so the lawyer / admin can see what changed.
        bool isMetadataVersionCreated = false;
        if (role == "Staff" || role == "Admin")
        {
            var staffStages = new[] {
                "PendingStaffReview", "StaffReview",
                "PendingLawyerReview", "LawyerReview",
                "PendingAdminReview", "AdminReview"
            };

            // Load existing versions to determine the current label
            var existingVersions = await _context.DocumentVersions
                .Where(v => v.DocumentId == id)
                .OrderByDescending(v => v.VersionNumber)
                .ToListAsync();

            if (staffStages.Contains(document.WorkflowStage) && existingVersions.Any())
            {
                var currentVer = existingVersions.First(v => v.IsCurrentVersion == true)
                               ?? existingVersions.First();

                // Calculate next minor version label  (e.g. "1" â†’ "1.1", "1.1" â†’ "1.2")
                var newLabel = CalcMinorVersionLabel(
                    existingVersions.Select(v => v.VersionLabel).ToList());

                var nextVersionNumber = existingVersions.Max(v => v.VersionNumber) + 1;

                // Set all existing as not current
                foreach (var v in existingVersions)
                    v.IsCurrentVersion = false;

                var metaVersion = new DocumentVersion
                {
                    DocumentId = id,
                    VersionNumber = nextVersionNumber,
                    VersionLabel = newLabel,
                    // Same file as the current version
                    FilePath = currentVer.FilePath,
                    FileSize = currentVer.FileSize,
                    OriginalFileName = currentVer.OriginalFileName,
                    FileExtension = currentVer.FileExtension,
                    MimeType = currentVer.MimeType,
                    UploadedBy = userId,
                    ChangeDescription = $"Metadata updated by {role}: title/description/type/category",
                    ChangedBy = role,
                    IsCurrentVersion = true,
                    CreatedAt = DateTime.UtcNow
                };

                _context.DocumentVersions.Add(metaVersion);
                document.CurrentVersion = nextVersionNumber;
                isMetadataVersionCreated = true;
            }
        }

        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "DocumentUpdate",
            "Document",
            id,
            $"Updated document metadata: {document.Title}" + (isMetadataVersionCreated ? " (new metadata version created)" : ""),
            null,
            null,
            "DocumentEdit");

        return Ok(new { success = true, message = "Document updated successfully", metadataVersionCreated = isMetadataVersionCreated });
    }

    /// <summary>
    /// Get checklist items for a firm
    /// </summary>
    [HttpGet("checklist-items")]
    [Authorize(Policy = "AdminOrStaff")]
    public async Task<IActionResult> GetChecklistItems()
    {
        var firmId = GetFirmId();

        var items = await _context.DocumentChecklistItems
            .Where(c => c.FirmId == firmId && c.IsActive == true)
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new
            {
                id = c.ChecklistItemId,
                itemName = c.ItemName,
                description = c.Description,
                isRequired = c.IsRequired,
                displayOrder = c.DisplayOrder
            })
            .ToListAsync();

        return Ok(new { success = true, items });
    }

    /// <summary>
    /// Get all document versions
    /// </summary>
    [HttpGet("{id}/versions")]
    public async Task<IActionResult> GetVersions(int id)
    {
        var firmId = GetFirmId();

        // Verify document belongs to this firm
        var documentExists = await _context.Documents.AnyAsync(d => d.DocumentID == id && d.FirmID == firmId);
        if (!documentExists)
            return NotFound(new { success = false, message = "Document not found" });

        var versions = await _context.DocumentVersions
            .Include(v => v.Uploader)
            .Where(v => v.DocumentId == id)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new
            {
                versionId = v.VersionId,
                versionNumber = v.VersionNumber,
                versionLabel = v.VersionLabel ?? v.VersionNumber.ToString(),
                originalFileName = v.OriginalFileName,
                fileSize = v.FileSize,
                changeDescription = v.ChangeDescription,
                changedBy = v.ChangedBy,
                uploader = v.Uploader != null ? (v.Uploader.FirstName ?? "") + " " + (v.Uploader.LastName ?? "") : null,
                isCurrentVersion = v.IsCurrentVersion,
                createdAt = v.CreatedAt
            })
            .ToListAsync();

        return Ok(new { success = true, versions });
    }

    /// <summary>
    /// Upload a new version of a document (Staff/Admin can upload new versions)
    /// </summary>
    [HttpPost("{id}/upload-version")]
    [Authorize(Policy = "FirmMember")]
    public async Task<IActionResult> UploadVersion(int id, [FromForm] UploadVersionDto dto)
    {
        try
        {
            var userId = GetCurrentUserId();
            var firmId = GetFirmId();
            var role = GetUserRole();

            // Only Staff and Admin can upload new versions
            if (role == "Client")
                return Forbid();

            if (dto.File == null || dto.File.Length == 0)
                return BadRequest(new { success = false, message = "No file provided" });

            if (string.IsNullOrWhiteSpace(dto.ChangeDescription))
                return BadRequest(new { success = false, message = "Change description is required" });

            var document = await _context.Documents
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(d => d.DocumentID == id && d.FirmID == firmId);

            if (document == null)
                return NotFound(new { success = false, message = "Document not found" });

            // Determine next version number
            var currentMaxVersion = document.Versions.Any() 
                ? document.Versions.Max(v => v.VersionNumber) 
                : 0;
            var newVersionNumber = currentMaxVersion + 1;

            // Determine version label
            // Staff uploads â†’ minor version bump (1.1, 1.2, â€¦)
            // Lawyer / Admin file uploads â†’ major version bump (2, 3, â€¦)
            var existingLabels = document.Versions
                .Select(v => v.VersionLabel)
                .ToList();

            string newVersionLabel;
            if (role == "Staff")
                newVersionLabel = CalcMinorVersionLabel(existingLabels);
            else // Lawyer, Admin
                newVersionLabel = CalcMajorVersionLabel(existingLabels);

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

            // Set all existing versions as not current
            foreach (var v in document.Versions)
            {
                v.IsCurrentVersion = false;
            }

            // Create new version
            var uploaderName = await _context.Users
                .Where(u => u.UserID == userId)
                .Select(u => ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim())
                .FirstOrDefaultAsync() ?? role;

            var newVersion = new DocumentVersion
            {
                DocumentId = id,
                VersionNumber = newVersionNumber,
                VersionLabel = newVersionLabel,
                FilePath = filePath,
                OriginalFileName = dto.File.FileName,
                FileSize = dto.File.Length,
                MimeType = dto.File.ContentType,
                ChangeDescription = dto.ChangeDescription,
                ChangedBy = dto.ChangedBy ?? uploaderName,
                UploadedBy = userId,
                IsCurrentVersion = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.DocumentVersions.Add(newVersion);
            document.CurrentVersion = newVersionNumber;

            await _context.SaveChangesAsync();

            // Log audit
            await _auditLogService.LogAsync(
                role == "Admin" ? "AdminEditDocument" : "StaffEditDocument",
                "Document",
                id,
                $"{role} uploaded new version {newVersionLabel} (v{newVersionNumber}): {dto.ChangeDescription}",
                null, null, "Workflow");

            return Ok(new
            {
                success = true,
                message = $"Version {newVersionLabel} created successfully",
                versionId = newVersion.VersionId,
                versionNumber = newVersionNumber,
                versionLabel = newVersionLabel
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading new version for document {DocumentId}", id);
            return StatusCode(500, new { success = false, message = "An error occurred while uploading the new version" });
        }
    }

    /// <summary>
    /// Get AI analysis for a document (full OpenAI analysis)
    /// Triggers real-time analysis if no stored result is found.
    /// </summary>
    [HttpGet("{id}/ai-analysis")]
    [Authorize(Policy = "FirmMember")]
    public async Task<IActionResult> GetAIAnalysis(int id)
    {
        try
        {
            var firmId = GetFirmId();

            var document = await _context.Documents
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(d => d.DocumentID == id && d.FirmID == firmId);

            if (document == null)
                return NotFound(new { success = false, message = "Document not found" });

            // â”€â”€ 1. Try to load a previously stored (processed) AI analysis â”€â”€â”€â”€â”€â”€â”€â”€â”€
            DocumentAIAnalysis? storedAnalysis = null;
            try
            {
                storedAnalysis = await _aiService.GetAnalysisAsync(id);
            }
            catch (Exception dbEx)
            {
                _logger.LogWarning(dbEx, "Could not query AI analysis table for document {DocumentId} (table may not exist yet).", id);
            }

            if (storedAnalysis != null && storedAnalysis.IsProcessed == true)
            {
                List<AIChecklistItem>? checklist = null;
                List<AIDocumentIssue>? issues = null;
                List<AIMissingItem>? missingItems = null;

                try
                {
                    if (!string.IsNullOrEmpty(storedAnalysis.ChecklistJson))
                        checklist = System.Text.Json.JsonSerializer.Deserialize<List<AIChecklistItem>>(storedAnalysis.ChecklistJson);
                    if (!string.IsNullOrEmpty(storedAnalysis.IssuesJson))
                        issues = System.Text.Json.JsonSerializer.Deserialize<List<AIDocumentIssue>>(storedAnalysis.IssuesJson);
                    if (!string.IsNullOrEmpty(storedAnalysis.MissingItemsJson))
                        missingItems = System.Text.Json.JsonSerializer.Deserialize<List<AIMissingItem>>(storedAnalysis.MissingItemsJson);
                }
                catch { /* ignore JSON parse errors â€“ handled by empty fallbacks */ }

                return Ok(new
                {
                    success = true,
                    documentId = id,
                    analysis = new
                    {
                        detectedDocumentType = storedAnalysis.DetectedDocumentType,
                        confidence = (storedAnalysis.Confidence ?? 0) / 100.0,
                        summary = storedAnalysis.Summary,
                        checklist = checklist ?? new List<AIChecklistItem>(),
                        issues = issues ?? new List<AIDocumentIssue>(),
                        missingItems = missingItems ?? new List<AIMissingItem>(),
                        isProcessed = true,
                        processedAt = storedAnalysis.ProcessedAt,
                        modelUsed = storedAnalysis.ModelUsed
                    },
                    isDuplicate = document.IsDuplicate,
                    duplicateInfo = document.IsDuplicate == true ? $"Possible duplicate of document #{document.DuplicateOfDocumentId}" : null
                });
            }

            // â”€â”€ 2. No stored analysis â€” trigger real-time analysis from file content â”€
            _logger.LogInformation("No stored AI analysis found for document {DocumentId}. Running real-time analysis.", id);

            var currentVersion = document.Versions
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefault(v => v.IsCurrentVersion == true)
                ?? document.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();

            OpenAIAnalysisResult? liveResult = null;

            if (currentVersion != null && !string.IsNullOrEmpty(currentVersion.FilePath))
            {
                var resolvedPath = ResolveVersionFilePath(currentVersion.FilePath);
                if (resolvedPath != null)
                {
                    try
                    {
                        var fileName = currentVersion.OriginalFileName ?? document.OriginalFileName ?? "document";
                        var fileExt  = currentVersion.FileExtension ?? document.FileExtension ?? "";

                        var extractedText = await _aiService.ExtractTextFromFileAsync(resolvedPath, fileName);
                        liveResult = await _aiService.AnalyzeWithOpenAIAsync(extractedText, fileName, fileExt);

                        // Persist the result so future calls are instant
                        if (liveResult.Success)
                        {
                            try
                            {
                                var newRecord = new DocumentAIAnalysis
                                {
                                    DocumentId = id,
                                    FirmId = firmId,
                                    DetectedDocumentType = liveResult.DocumentType,
                                    Confidence = liveResult.Confidence,
                                    Summary = liveResult.Summary,
                                    ChecklistJson = System.Text.Json.JsonSerializer.Serialize(liveResult.Checklist),
                                    IssuesJson = System.Text.Json.JsonSerializer.Serialize(liveResult.Issues),
                                    MissingItemsJson = System.Text.Json.JsonSerializer.Serialize(liveResult.MissingItems),
                                    RawResponseJson = liveResult.RawResponse,
                                    ExtractedText = extractedText.Length > 10000 ? extractedText[..10000] : extractedText,
                                    IsProcessed = true,
                                    ProcessedAt = DateTime.UtcNow,
                                    ModelUsed = liveResult.ModelUsed,
                                    TokensUsed = liveResult.TokensUsed,
                                    CreatedAt = DateTime.UtcNow
                                };

                                _context.DocumentAIAnalyses.Add(newRecord);
                                document.IsAIProcessed = true;
                                if (!string.IsNullOrEmpty(liveResult.DocumentType))
                                    document.DocumentType = liveResult.DocumentType;
                                await _context.SaveChangesAsync();
                            }
                            catch (Exception saveEx)
                            {
                                _logger.LogWarning(saveEx, "Could not persist live AI analysis for document {DocumentId}.", id);
                                foreach (var entry in _context.ChangeTracker.Entries()
                                    .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified).ToList())
                                    entry.State = EntityState.Detached;
                            }
                        }
                    }
                    catch (Exception aiEx)
                    {
                        _logger.LogWarning(aiEx, "Real-time AI analysis call failed for document {DocumentId}.", id);
                    }
                }
            }

            if (liveResult != null && liveResult.Success)
            {
                return Ok(new
                {
                    success = true,
                    documentId = id,
                    analysis = new
                    {
                        detectedDocumentType = liveResult.DocumentType,
                        confidence = liveResult.Confidence / 100.0,
                        summary = liveResult.Summary,
                        checklist = liveResult.Checklist,
                        issues = liveResult.Issues,
                        missingItems = liveResult.MissingItems,
                        isProcessed = true,
                        processedAt = DateTime.UtcNow,
                        modelUsed = liveResult.ModelUsed
                    },
                    isDuplicate = document.IsDuplicate,
                    duplicateInfo = document.IsDuplicate == true ? $"Possible duplicate of document #{document.DuplicateOfDocumentId}" : null
                });
            }

            // â”€â”€ 3. Keyword-based fallback (no OpenAI / file not found) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var fallback = await _aiService.AnalyzeDocumentAsync(id);
            return Ok(new
            {
                success = fallback.Success,
                documentId = id,
                analysis = new
                {
                    detectedDocumentType = fallback.DocumentType,
                    confidence = fallback.Confidence / 100.0,
                    summary = string.IsNullOrEmpty(fallback.Summary)
                        ? "AI analysis is running. If this persists, check that OpenAI API key is configured and the document file is accessible."
                        : fallback.Summary,
                    checklist = fallback.AIChecklist,
                    issues = fallback.AIIssues,
                    missingItems = fallback.AIMissingItems,
                    isProcessed = false,
                    modelUsed = "Keyword-based fallback",
                    processedAt = (DateTime?)null
                },
                isDuplicate = fallback.IsDuplicate,
                duplicateInfo = fallback.IsDuplicate ? $"Possible duplicate of document #{fallback.DuplicateOfDocumentId}" : null,
                keywords = fallback.Keywords
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading AI analysis for document {DocumentId}", id);
            return Ok(new { success = false, message = "AI analysis unavailable: " + ex.Message, analysis = new { summary = "An error occurred while loading AI analysis.", isProcessed = false } });
        }
    }


    [HttpGet("{id}/audit-trail")]
    public async Task<IActionResult> GetAuditTrail(int id)
    {
        try
        {
            var firmId = GetFirmId();
            var userId = GetCurrentUserId();
            var role = GetUserRole();

            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.DocumentID == id && d.FirmID == firmId);

            if (document == null)
                return NotFound(new { success = false, message = "Document not found" });

            // Check permissions
            if (role == "Client" && document.UploadedBy != userId)
                return Forbid();

            // Get audit logs for this document
            var auditTrail = await _context.AuditLogs
                .Include(a => a.User)
                .Where(a => a.EntityType == "Document" && a.EntityID == id)
                .OrderByDescending(a => a.Timestamp)
                .Take(100)
                .Select(a => new
                {
                    action = a.Action,
                    description = a.Description,
                    timestamp = a.Timestamp,
                    userName = a.User != null ? (a.User.FirstName ?? "") + " " + (a.User.LastName ?? "") : "System",
                    actionCategory = a.ActionCategory
                })
                .ToListAsync();

            return Ok(new { success = true, auditTrail });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading audit trail for document {DocumentId}", id);
            return Ok(new { success = false, message = "Error loading audit trail", auditTrail = new List<object>() });
        }
    }

    /// <summary>
    /// Get text content of a document for viewing
    /// </summary>
    [HttpGet("{id}/text-content")]
    public async Task<IActionResult> GetTextContent(int id, [FromQuery] int? versionId = null)
    {
        try
        {
            var firmId = GetFirmId();
            var userId = GetCurrentUserId();
            var role = GetUserRole();

            var document = await _context.Documents
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(d => d.DocumentID == id && d.FirmID == firmId);

            if (document == null)
                return NotFound(new { success = false, message = "Document not found" });

            // Check permissions
            if (role == "Client" && document.UploadedBy != userId)
                return Forbid();

            DocumentVersion? version;
            if (versionId.HasValue)
            {
                version = document.Versions.FirstOrDefault(v => v.VersionId == versionId);
            }
            else
            {
                version = document.Versions.FirstOrDefault(v => v.IsCurrentVersion == true) 
                    ?? document.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            }

            if (version == null || string.IsNullOrEmpty(version.FilePath))
                return NotFound(new { success = false, message = "File not found" });

            var resolvedPath = ResolveVersionFilePath(version.FilePath);
            if (resolvedPath == null)
                return Ok(new { success = false, message = "File not found on server. It may have been moved or deleted." });

            // Extract text content
            var textContent = await _aiService.ExtractTextFromFileAsync(resolvedPath, version.OriginalFileName ?? "document");

            return Ok(new { 
                success = true, 
                content = textContent,
                versionNumber = version.VersionNumber,
                changedBy = version.ChangedBy
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text content for document {DocumentId}", id);
            return Ok(new { success = false, message = "Error extracting text content" });
        }
    }

    /// <summary>
    /// View document inline (without downloading)
    /// </summary>
    [HttpGet("{id}/view")]
    public async Task<IActionResult> ViewInline(int id, [FromQuery] int? versionId = null)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();
        var role = GetUserRole();

        var document = await _context.Documents
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.DocumentID == id && d.FirmID == firmId);

        if (document == null)
            return NotFound();

        // Check access permissions
        if (role == "Client" && document.UploadedBy != userId)
            return Forbid();

        DocumentVersion? version;
        if (versionId.HasValue)
        {
            version = document.Versions.FirstOrDefault(v => v.VersionId == versionId);
        }
        else
        {
            version = document.Versions.FirstOrDefault(v => v.IsCurrentVersion == true) 
                ?? document.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        }

        if (version == null || string.IsNullOrEmpty(version.FilePath))
            return NotFound();

        var resolvedViewPath = ResolveVersionFilePath(version.FilePath);
        if (resolvedViewPath == null)
            return NotFound();

        var fileBytes = await System.IO.File.ReadAllBytesAsync(resolvedViewPath);
        var contentType = version.MimeType ?? "application/octet-stream";

        // Set headers for inline viewing
        Response.Headers.ContentDisposition = $"inline; filename=\"{Uri.EscapeDataString(version.OriginalFileName ?? "document")}\"";

        // Log view action
        await _auditLogService.LogAsync(
            "DocumentView",
            "Document",
            id,
            $"Viewed document inline: {document.Title} (Version {version.VersionNumber})",
            null,
            null,
            "DocumentAccess");

        return File(fileBytes, contentType);
    }

    /// <summary>
    /// Verify signature in a document using AI
    /// </summary>
    [HttpPost("{id}/verify-signature")]
    public async Task<IActionResult> VerifySignature(int id)
    {
        try
        {
            var firmId = GetFirmId();

            var document = await _context.Documents
                .Include(d => d.Uploader)
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(d => d.DocumentID == id && d.FirmID == firmId);

            if (document == null)
                return NotFound(new { success = false, message = "Document not found" });

            // Get the current version's file
            var version = document.Versions
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefault(v => v.IsCurrentVersion == true) ?? 
                document.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();

            if (version == null || string.IsNullOrEmpty(version.FilePath))
                return BadRequest(new { success = false, message = "Document file not found" });

            if (!System.IO.File.Exists(version.FilePath))
                return BadRequest(new { success = false, message = "Document file not found on disk" });

            // Get expected signer name (the client who uploaded)
            var expectedName = document.Uploader?.SignatureName ?? document.Uploader?.FullName ?? "";

            using var fileStream = new FileStream(version.FilePath, FileMode.Open, FileAccess.Read);
            var result = await _aiService.VerifySignatureAsync(id, fileStream, expectedName);

            // Update document with signature verification status
            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                "SignatureVerification",
                "Document",
                id,
                $"Signature verification: {result.VerificationStatus} ({result.ConfidenceScore}% confidence)",
                null,
                null,
                "DocumentReview");

            return Ok(new
            {
                success = true,
                verification = new
                {
                    documentId = result.DocumentId,
                    isVerified = result.IsVerified,
                    confidenceScore = result.ConfidenceScore,
                    signerNameDetected = result.SignerNameDetected,
                    verificationStatus = result.VerificationStatus,
                    message = result.Message
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying signature for document {DocumentId}", id);
            return StatusCode(500, new { success = false, message = "An error occurred during signature verification" });
        }
    }

    /// <summary>
    /// Get line-level text diff between two document versions (for redline view)
    /// </summary>
    [HttpGet("{id}/text-diff")]
    public async Task<IActionResult> GetTextDiff(int id, [FromQuery] int? fromVersionId = null, [FromQuery] int? toVersionId = null)
    {
        try
        {
            var firmId = GetFirmId();
            var role = GetUserRole();

            var document = await _context.Documents
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(d => d.DocumentID == id && d.FirmID == firmId);

            if (document == null)
                return NotFound(new { success = false, message = "Document not found" });

            var versions = document.Versions.OrderBy(v => v.VersionNumber).ToList();
            if (versions.Count < 2)
                return Ok(new { success = false, message = "Need at least 2 versions to compare" });

            DocumentVersion? fromVer = fromVersionId.HasValue
                ? versions.FirstOrDefault(v => v.VersionId == fromVersionId)
                : versions.FirstOrDefault();

            DocumentVersion? toVer = toVersionId.HasValue
                ? versions.FirstOrDefault(v => v.VersionId == toVersionId)
                : versions.LastOrDefault();

            if (fromVer == null || toVer == null)
                return BadRequest(new { success = false, message = "Invalid version IDs" });

            var fromPath = ResolveVersionFilePath(fromVer.FilePath);
            var toPath   = ResolveVersionFilePath(toVer.FilePath);
            if (fromPath == null || toPath == null)
                return BadRequest(new { success = false, message = "Version file(s) not found on server. The original files may have been moved or deleted." });

            var fromText = await _aiService.ExtractTextFromFileAsync(fromPath, fromVer.OriginalFileName ?? "doc");
            var toText   = await _aiService.ExtractTextFromFileAsync(toPath,   toVer.OriginalFileName   ?? "doc");

            var diffTokens = ComputeLineDiff(fromText, toText);

            return Ok(new
            {
                success = true,
                fromVersion = new { fromVer.VersionId, fromVer.VersionNumber, fromVer.ChangedBy, fromVer.CreatedAt, fromVer.ChangeDescription },
                toVersion   = new { toVer.VersionId,   toVer.VersionNumber,   toVer.ChangedBy,   toVer.CreatedAt,   toVer.ChangeDescription   },
                diff = diffTokens,
                addedCount   = diffTokens.Count(t => t.Type == "added"),
                removedCount = diffTokens.Count(t => t.Type == "removed"),
                unchangedCount = diffTokens.Count(t => t.Type == "unchanged")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing text diff for document {DocumentId}", id);
            return StatusCode(500, new { success = false, message = "Error computing diff" });
        }
    }

    /// <summary>
    /// AI-powered change analysis between two document versions
    /// </summary>
    [HttpPost("{id}/ai-changes")]
    public async Task<IActionResult> GetAIChanges(int id, [FromQuery] int? fromVersionId = null, [FromQuery] int? toVersionId = null)
    {
        try
        {
            var firmId = GetFirmId();

            var document = await _context.Documents
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(d => d.DocumentID == id && d.FirmID == firmId);

            if (document == null)
                return NotFound(new { success = false, message = "Document not found" });

            var versions = document.Versions.OrderBy(v => v.VersionNumber).ToList();
            if (versions.Count < 2)
                return Ok(new { success = false, message = "Need at least 2 versions to compare" });

            DocumentVersion? fromVer = fromVersionId.HasValue
                ? versions.FirstOrDefault(v => v.VersionId == fromVersionId)
                : versions.FirstOrDefault();

            DocumentVersion? toVer = toVersionId.HasValue
                ? versions.FirstOrDefault(v => v.VersionId == toVersionId)
                : versions.LastOrDefault();

            if (fromVer == null || toVer == null)
                return BadRequest(new { success = false, message = "Invalid version IDs" });

            var fromPath = ResolveVersionFilePath(fromVer.FilePath);
            var toPath   = ResolveVersionFilePath(toVer.FilePath);
            if (fromPath == null || toPath == null)
                return BadRequest(new { success = false, message = "Version file(s) not found on server. The original files may have been moved or deleted." });

            var fromText = await _aiService.ExtractTextFromFileAsync(fromPath, fromVer.OriginalFileName ?? "doc");
            var toText   = await _aiService.ExtractTextFromFileAsync(toPath,   toVer.OriginalFileName   ?? "doc");

            var analysis = await _aiService.AnalyzeDocumentChangesAsync(
                fromText, toText,
                document.Title ?? document.OriginalFileName ?? "Document",
                fromVer.VersionNumber, toVer.VersionNumber);

            await _auditLogService.LogAsync(
                "AIChangeAnalysis", "Document", id,
                $"AI change analysis run between v{fromVer.VersionNumber} and v{toVer.VersionNumber}",
                null, null, "DocumentReview");

            return Ok(new
            {
                success = true,
                fromVersion = new { fromVer.VersionId, fromVer.VersionNumber, fromVer.ChangedBy },
                toVersion   = new { toVer.VersionId,   toVer.VersionNumber,   toVer.ChangedBy   },
                analysis
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running AI change analysis for document {DocumentId}", id);
            return StatusCode(500, new { success = false, message = "Error running AI analysis" });
        }
    }

    // â”€â”€â”€ Version-label helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Calculate the next MINOR version label for a staff-only metadata/file edit.
    /// Examples: "1" â†’ "1.1", "1.1" â†’ "1.2", "2" â†’ "2.1"
    /// </summary>
    private static string CalcMinorVersionLabel(IEnumerable<string?> existingLabels)
    {
        var labels = existingLabels.Where(l => !string.IsNullOrEmpty(l)).Select(l => l!).ToList();
        if (!labels.Any()) return "1.1";

        // Find the label of the most recent version
        var latest = labels.Last(); // last in the ordered list
        if (latest.Contains('.'))
        {
            var parts = latest.Split('.');
            if (int.TryParse(parts[0], out var maj) && int.TryParse(parts[1], out var min))
                return $"{maj}.{min + 1}";
        }
        else
        {
            if (int.TryParse(latest, out var maj))
                return $"{maj}.1";
        }
        return labels.Count + ".1";
    }

    /// <summary>
    /// Calculate the next MAJOR version label for a lawyer or admin file upload.
    /// Examples: "1" â†’ "2", "1.1" â†’ "2", "1.2" â†’ "2", "2.1" â†’ "3"
    /// </summary>
    private static string CalcMajorVersionLabel(IEnumerable<string?> existingLabels)
    {
        var labels = existingLabels.Where(l => !string.IsNullOrEmpty(l)).Select(l => l!).ToList();
        if (!labels.Any()) return "1";

        int currentMajor = 1;
        foreach (var label in labels)
        {
            var majorStr = label.Split('.')[0];
            if (int.TryParse(majorStr, out var m) && m > currentMajor)
                currentMajor = m;
        }
        return $"{currentMajor + 1}";
    }

    // â”€â”€â”€ File path resolver â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Resolves a stored FilePath to an absolute path that currently exists on disk.
    /// Handles cases where content root has changed (e.g. deployment moved).
    /// Returns null if file cannot be located.
    /// </summary>
    private string? ResolveVersionFilePath(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return null;

        // 1) Stored path exists as-is (happy path â€” most uploads)
        if (System.IO.File.Exists(storedPath)) return storedPath;

        // 2) Remap by extracting the "Uploads/..." segment and rebuilding from the current content root.
        //    Handles cases where the app was previously run from bin\Debug\net8.0 (contentRoot differs).
        var normalised = storedPath.Replace('\\', '/');
        var uploadsIdx = normalised.IndexOf("/Uploads/", StringComparison.OrdinalIgnoreCase);
        if (uploadsIdx >= 0)
        {
            var relativePart = normalised.Substring(uploadsIdx + 1); // "Uploads/firmId/userId/guid.ext"
            var candidate = Path.Combine(_environment.ContentRootPath, relativePart);
            if (System.IO.File.Exists(candidate)) return candidate;
        }

        // 3) Last resort: the stored filename is a GUID so it is globally unique.
        //    Search the entire Uploads tree for it (covers any root mismatch).
        var fileName = Path.GetFileName(storedPath);
        if (!string.IsNullOrEmpty(fileName))
        {
            var uploadsRoot = Path.Combine(_environment.ContentRootPath, "Uploads");
            if (Directory.Exists(uploadsRoot))
            {
                var found = Directory.GetFiles(uploadsRoot, fileName, SearchOption.AllDirectories)
                                     .FirstOrDefault();
                if (found != null) return found;
            }
        }

        return null;
    }

    // â”€â”€â”€ Diff helper â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private record DiffToken(string Text, string Type);

    private static List<DiffToken> ComputeLineDiff(string oldText, string newText)
    {
        // Split into non-empty lines (paragraphs)
        var oldLines = SplitLines(oldText);
        var newLines = SplitLines(newText);

        // Cap to prevent O(nÂ²) slow-down
        const int MaxLines = 400;
        if (oldLines.Count > MaxLines) oldLines = oldLines.Take(MaxLines).ToList();
        if (newLines.Count > MaxLines) newLines = newLines.Take(MaxLines).ToList();

        int m = oldLines.Count, n = newLines.Count;

        // Build LCS table
        var dp = new int[m + 1, n + 1];
        for (int i = m - 1; i >= 0; i--)
            for (int j = n - 1; j >= 0; j--)
                dp[i, j] = oldLines[i] == newLines[j]
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        // Walk back through LCS table to produce diff tokens
        var result = new List<DiffToken>();
        int oi = 0, ni = 0;
        while (oi < m || ni < n)
        {
            if (oi < m && ni < n && oldLines[oi] == newLines[ni])
            {
                result.Add(new DiffToken(oldLines[oi], "unchanged"));
                oi++; ni++;
            }
            else if (ni < n && (oi >= m || dp[oi, ni + 1] >= dp[oi + 1, ni]))
            {
                result.Add(new DiffToken(newLines[ni], "added"));
                ni++;
            }
            else
            {
                result.Add(new DiffToken(oldLines[oi], "removed"));
                oi++;
            }
        }
        return result;
    }

    private static List<string> SplitLines(string text)
    {
        return text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(l => l.Trim())
                   .Where(l => l.Length > 0)
                   .ToList();
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Get client's signature info for comparison/display
    /// </summary>
    [HttpGet("{id}/client-signature")]
    public async Task<IActionResult> GetClientSignature(int id)
    {
        var firmId = GetFirmId();

        var document = await _context.Documents
            .Include(d => d.Uploader)
            .FirstOrDefaultAsync(d => d.DocumentID == id && d.FirmID == firmId);

        if (document == null)
            return NotFound(new { success = false, message = "Document not found" });

        var uploader = document.Uploader;
        if (uploader == null)
            return Ok(new { success = true, hasSignature = false });

        return Ok(new
        {
            success = true,
            hasSignature = !string.IsNullOrEmpty(uploader.SignaturePath),
            signaturePath = uploader.SignaturePath,
            signatureName = uploader.SignatureName,
            clientName = uploader.FullName
        });
    }
}

// DTOs
public class DocumentUploadDto
{
    public IFormFile? File { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? DocumentType { get; set; }
    public int? FolderId { get; set; }
    public bool IsHighRisk { get; set; } = false;
}

public class ManualUploadDto
{
    public IFormFile? File { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? DocumentType { get; set; }
    public int? FolderId { get; set; }
    public bool IsHighRisk { get; set; } = false;
    /// <summary>Optional: associate with an existing client account</summary>
    public int? OnBehalfOfClientId { get; set; }
    /// <summary>Optional: client name when no client account exists (emergency upload)</summary>
    public string? ClientName { get; set; }
}

public class DocumentUpdateDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? DocumentType { get; set; }
    public string? Tags { get; set; }
    public string? Priority { get; set; }
}

public class DocumentArchiveDto
{
    public string? Reason { get; set; }
}

public class UploadVersionDto
{
    public IFormFile? File { get; set; }
    public string? ChangeDescription { get; set; }
    public string? ChangedBy { get; set; }
}
