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
                CreatedAt = DateTime.UtcNow
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            // Create initial version
            var version = new DocumentVersion
            {
                DocumentId = document.DocumentID,
                VersionNumber = 1,
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

            // Process with AI
            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                var aiResult = await _aiService.ProcessDocumentAsync(document.DocumentID, fileStream, dto.File.FileName);
                
                // Ensure checklist items exist for detected document type
                if (!string.IsNullOrEmpty(aiResult.DetectedDocumentType))
                {
                    await _aiService.EnsureChecklistItemsExistAsync(firmId, aiResult.DetectedDocumentType);
                }
            }

            // Assign to staff for review
            var assignedStaff = await _workflowService.AssignToStaffAsync(document.DocumentID, firmId);

            // Notify all staff members
            await _notificationService.NotifyAllStaffAsync(
                firmId,
                "New Document Pending Review",
                $"Client uploaded a new document: {document.Title}",
                NotificationService.TYPE_DOCUMENT_PENDING_REVIEW,
                document.DocumentID,
                $"/Review/Review/{document.DocumentID}");

            // Audit log
            await _auditLogService.LogAsync(
                "DocumentUpload",
                "Document",
                document.DocumentID,
                $"Client uploaded document: {document.Title}",
                null,
                null,
                "DocumentUpload");

            return Ok(new
            {
                success = true,
                message = "Document uploaded successfully",
                documentId = document.DocumentID,
                assignedTo = assignedStaff?.FullName
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
                folder = document.Folder != null ? new { id = document.Folder.FolderId, name = document.Folder.FolderName } : null,
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
                    reviewer = r.Reviewer?.FullName,
                    reviewedAt = r.ReviewedAt,
                    isChecklistComplete = r.IsChecklistComplete
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

        if (!System.IO.File.Exists(version.FilePath))
            return NotFound(new { success = false, message = "File not found on server" });

        var fileBytes = await System.IO.File.ReadAllBytesAsync(version.FilePath);
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
                originalFileName = d.OriginalFileName,
                fileExtension = d.FileExtension,
                totalFileSize = d.TotalFileSize,
                currentRemarks = d.CurrentRemarks,
                folder = d.Folder != null ? new { id = d.Folder.FolderId, name = d.Folder.FolderName } : null,
                createdAt = d.CreatedAt,
                updatedAt = d.UpdatedAt
            })
            .ToListAsync();

        return Ok(new { success = true, documents });
    }

    /// <summary>
    /// Archive document (Client can archive their own)
    /// </summary>
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> ArchiveDocument(int id, [FromBody] ArchiveDocumentDto dto)
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

        var archive = await _workflowService.ArchiveDocumentAsync(id, userId, dto.Reason ?? "Archived by user", "Manual");

        return Ok(new { success = true, message = "Document archived successfully", archiveId = archive.ArchiveID });
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

        document.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "DocumentUpdate",
            "Document",
            id,
            $"Updated document metadata: {document.Title}",
            null,
            null,
            "DocumentEdit");

        return Ok(new { success = true, message = "Document updated successfully" });
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

        var versions = await _context.DocumentVersions
            .Include(v => v.Uploader)
            .Where(v => v.Document != null && v.Document.DocumentID == id && v.Document.FirmID == firmId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new
            {
                versionId = v.VersionId,
                versionNumber = v.VersionNumber,
                originalFileName = v.OriginalFileName,
                fileSize = v.FileSize,
                changeDescription = v.ChangeDescription,
                changedBy = v.ChangedBy,
                uploader = v.Uploader != null ? v.Uploader.FullName : null,
                isCurrentVersion = v.IsCurrentVersion,
                createdAt = v.CreatedAt
            })
            .ToListAsync();

        return Ok(new { success = true, versions });
    }

    /// <summary>
    /// View document (inline, no download) - for staff viewing
    /// </summary>
    [HttpGet("{id}/view")]
    public async Task<IActionResult> ViewDocument(int id)
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

        var version = document.Versions.FirstOrDefault(v => v.IsCurrentVersion == true)
            ?? document.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();

        if (version == null || string.IsNullOrEmpty(version.FilePath))
            return NotFound();

        if (!System.IO.File.Exists(version.FilePath))
            return NotFound();

        var fileBytes = await System.IO.File.ReadAllBytesAsync(version.FilePath);
        var contentType = version.MimeType ?? "application/octet-stream";

        // Set Content-Disposition to inline (view in browser, not download)
        Response.Headers.ContentDisposition = $"inline; filename=\"{version.OriginalFileName}\"";

        return File(fileBytes, contentType);
    }

    /// <summary>
    /// Get AI analysis for a document
    /// </summary>
    [HttpGet("{id}/ai-analysis")]
    [Authorize(Policy = "AdminOrStaff")]
    public async Task<IActionResult> GetAIAnalysis(int id)
    {
        var firmId = GetFirmId();

        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.DocumentID == id && d.FirmID == firmId);

        if (document == null)
            return NotFound(new { success = false, message = "Document not found" });

        // Get AI analysis from the AI service
        var analysis = await _aiService.AnalyzeDocumentAsync(id);

        if (!analysis.Success)
        {
            return Ok(new
            {
                success = false,
                message = analysis.ErrorMessage ?? "Failed to analyze document"
            });
        }

        // Convert issues to API format
        var issues = analysis.Issues.Select(i => new
        {
            type = i.Type == "error" ? "Error" : i.Type == "warning" ? "Warning" : "Info",
            message = i.Message,
            severity = i.Type == "error" ? "High" : i.Type == "warning" ? "Medium" : "Low",
            location = "Document"
        }).ToList();

        return Ok(new
        {
            success = true,
            documentId = id,
            documentType = analysis.DocumentType,
            confidence = analysis.Confidence,
            isConfidential = analysis.IsConfidential,
            isDuplicate = analysis.IsDuplicate,
            duplicateInfo = analysis.IsDuplicate ? $"Possible duplicate of document #{analysis.DuplicateOfDocumentId}" : null,
            issues = issues,
            keywords = analysis.Keywords
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
}

public class DocumentUpdateDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
}

public class ArchiveDocumentDto
{
    public string? Reason { get; set; }
}
