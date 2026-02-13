using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;
using Microsoft.EntityFrameworkCore;

namespace CKNDocument.Services;

/// <summary>
/// Service for managing document workflow stages
/// Workflow: ClientUpload → PendingStaffReview → StaffReview → PendingAdminReview → AdminReview → Approved → Completed
/// </summary>
public class DocumentWorkflowService
{
    private readonly LawFirmDMSDbContext _context;
    private readonly NotificationService _notificationService;
    private readonly AuditLogService _auditLogService;
    private readonly ILogger<DocumentWorkflowService> _logger;

    // Workflow Stage Constants
    public const string STAGE_CLIENT_UPLOAD = "ClientUpload";
    public const string STAGE_PENDING_STAFF_REVIEW = "PendingStaffReview";
    public const string STAGE_STAFF_REVIEW = "StaffReview";
    public const string STAGE_STAFF_REJECTED = "StaffRejected";
    public const string STAGE_PENDING_LAWYER_REVIEW = "PendingLawyerReview";
    public const string STAGE_LAWYER_REVIEW = "LawyerReview";
    public const string STAGE_LAWYER_REJECTED = "LawyerRejected";
    public const string STAGE_PENDING_ADMIN_REVIEW = "PendingAdminReview";
    public const string STAGE_ADMIN_REVIEW = "AdminReview";
    public const string STAGE_ADMIN_REJECTED = "AdminRejected";
    public const string STAGE_APPROVED = "Approved";
    public const string STAGE_COMPLETED = "Completed";
    public const string STAGE_ARCHIVED = "Archived";

    // Status Constants
    public const string STATUS_PENDING = "Pending";
    public const string STATUS_UNDER_REVIEW = "UnderReview";
    public const string STATUS_APPROVED = "Approved";
    public const string STATUS_REJECTED = "Rejected";
    public const string STATUS_COMPLETED = "Completed";
    public const string STATUS_ARCHIVED = "Archived";

    public DocumentWorkflowService(
        LawFirmDMSDbContext context,
        NotificationService notificationService,
        AuditLogService auditLogService,
        ILogger<DocumentWorkflowService> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    /// <summary>
    /// Assign document to a staff member for review (round-robin or least loaded)
    /// </summary>
    public async Task<User?> AssignToStaffAsync(int documentId, int firmId)
    {
        // Get all active staff members in the firm
        var staffMembers = await _context.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .Where(u => u.FirmID == firmId &&
                        u.Status == "Active" &&
                        u.UserRoles.Any(ur => ur.Role != null && ur.Role.RoleName == "Staff"))
            .ToListAsync();

        if (!staffMembers.Any())
        {
            _logger.LogWarning("No active staff members found for firm {FirmId}", firmId);
            return null;
        }

        // Get staff workload (count of pending documents assigned to each)
        var staffWorkload = await _context.Documents
            .Where(d => d.FirmID == firmId &&
                        d.AssignedStaffId != null &&
                        (d.WorkflowStage == STAGE_PENDING_STAFF_REVIEW || d.WorkflowStage == STAGE_STAFF_REVIEW))
            .GroupBy(d => d.AssignedStaffId)
            .Select(g => new { StaffId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StaffId!.Value, x => x.Count);

        // Find staff with least workload
        var selectedStaff = staffMembers
            .OrderBy(s => staffWorkload.GetValueOrDefault(s.UserID, 0))
            .First();

        // Update document assignment
        var document = await _context.Documents.FindAsync(documentId);
        if (document != null)
        {
            document.AssignedStaffId = selectedStaff.UserID;
            document.WorkflowStage = STAGE_PENDING_STAFF_REVIEW;
            document.Status = STATUS_PENDING;
            document.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return selectedStaff;
    }

    /// <summary>
    /// Assign document to an admin for final review
    /// </summary>
    public async Task<User?> AssignToAdminAsync(int documentId, int firmId)
    {
        // Get all active admin members in the firm
        var adminMembers = await _context.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .Where(u => u.FirmID == firmId &&
                        u.Status == "Active" &&
                        u.UserRoles.Any(ur => ur.Role != null && ur.Role.RoleName == "Admin"))
            .ToListAsync();

        if (!adminMembers.Any())
        {
            _logger.LogWarning("No active admin members found for firm {FirmId}", firmId);
            return null;
        }

        // Get admin workload
        var adminWorkload = await _context.Documents
            .Where(d => d.FirmID == firmId &&
                        d.AssignedAdminId != null &&
                        (d.WorkflowStage == STAGE_PENDING_ADMIN_REVIEW || d.WorkflowStage == STAGE_ADMIN_REVIEW))
            .GroupBy(d => d.AssignedAdminId)
            .Select(g => new { AdminId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AdminId!.Value, x => x.Count);

        // Find admin with least workload
        var selectedAdmin = adminMembers
            .OrderBy(a => adminWorkload.GetValueOrDefault(a.UserID, 0))
            .First();

        // Update document assignment
        var document = await _context.Documents.FindAsync(documentId);
        if (document != null)
        {
            document.AssignedAdminId = selectedAdmin.UserID;
            document.WorkflowStage = STAGE_PENDING_ADMIN_REVIEW;
            document.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return selectedAdmin;
    }

    /// <summary>
    /// Assign document to a lawyer for review (after staff metadata review)
    /// </summary>
    public async Task<User?> AssignToLawyerAsync(int documentId, int firmId)
    {
        // Get all active lawyer members in the firm
        var lawyerMembers = await _context.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .Where(u => u.FirmID == firmId &&
                        u.Status == "Active" &&
                        u.UserRoles.Any(ur => ur.Role != null && ur.Role.RoleName == "Lawyer"))
            .ToListAsync();

        if (!lawyerMembers.Any())
        {
            _logger.LogWarning("No active lawyers found for firm {FirmId}", firmId);
            return null;
        }

        // Get lawyer workload
        var lawyerWorkload = await _context.Documents
            .Where(d => d.FirmID == firmId &&
                        d.AssignedLawyerId != null &&
                        (d.WorkflowStage == STAGE_PENDING_LAWYER_REVIEW || d.WorkflowStage == STAGE_LAWYER_REVIEW))
            .GroupBy(d => d.AssignedLawyerId)
            .Select(g => new { LawyerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LawyerId!.Value, x => x.Count);

        // Find lawyer with least workload
        var selectedLawyer = lawyerMembers
            .OrderBy(l => lawyerWorkload.GetValueOrDefault(l.UserID, 0))
            .First();

        // Update document assignment
        var document = await _context.Documents.FindAsync(documentId);
        if (document != null)
        {
            document.AssignedLawyerId = selectedLawyer.UserID;
            document.WorkflowStage = STAGE_PENDING_LAWYER_REVIEW;
            document.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return selectedLawyer;
    }

    /// <summary>
    /// Staff approves document and forwards to lawyer
    /// </summary>
    public async Task<DocumentReview> StaffApproveAsync(int documentId, int staffId, string? remarks, string? internalNotes, List<DocumentChecklistResult>? checklistResults)
    {
        var document = await _context.Documents
            .Include(d => d.Uploader)
            .FirstOrDefaultAsync(d => d.DocumentID == documentId);

        if (document == null)
            throw new InvalidOperationException("Document not found");

        // Create review record
        var review = new DocumentReview
        {
            DocumentId = documentId,
            ReviewedBy = staffId,
            ReviewStatus = STATUS_APPROVED,
            Remarks = remarks,
            InternalNotes = internalNotes,
            ReviewedAt = DateTime.UtcNow,
            ReviewerRole = "Staff",
            IsChecklistComplete = checklistResults?.All(r => r.IsPassed == true) ?? true,
            ChecklistScore = checklistResults?.Count(r => r.IsPassed == true) ?? 0,
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentReviews.Add(review);
        await _context.SaveChangesAsync();

        // Add checklist results if provided
        if (checklistResults != null && checklistResults.Any())
        {
            foreach (var result in checklistResults)
            {
                result.ReviewId = review.ReviewId;
                result.CheckedAt = DateTime.UtcNow;
                _context.DocumentChecklistResults.Add(result);
            }
            await _context.SaveChangesAsync();
        }

        // Update document workflow
        document.StaffReviewedAt = DateTime.UtcNow;
        document.UpdatedAt = DateTime.UtcNow;

        // Assign to lawyer (new workflow: Staff → Lawyer → Admin)
        var lawyer = await AssignToLawyerAsync(documentId, document.FirmID);

        // Notify lawyer
        if (lawyer != null)
        {
            await _notificationService.NotifyAsync(
                lawyer.UserID,
                "New Document for Review",
                $"Document '{document.Title}' has been reviewed by staff and forwarded for your review.",
                "StaffApproved",
                documentId,
                $"/Lawyer/PendingReviews");
        }

        // Notify client
        if (document.UploadedBy.HasValue)
        {
            await _notificationService.NotifyAsync(
                document.UploadedBy.Value,
                "Document Reviewed by Staff",
                $"Your document '{document.Title}' has been reviewed by staff and forwarded to lawyer for review.",
                "StaffApproved",
                documentId,
                $"/Document/Details/{documentId}");
        }

        // Audit log
        await _auditLogService.LogAsync(
            "StaffApprove",
            "Document",
            documentId,
            $"Staff approved document: {document.Title}",
            null,
            $"{{\"remarks\":\"{remarks}\"}}",
            "DocumentReview");

        return review;
    }

    /// <summary>
    /// Staff rejects document
    /// </summary>
    public async Task<DocumentReview> StaffRejectAsync(int documentId, int staffId, string remarks, List<DocumentChecklistResult>? checklistResults)
    {
        var document = await _context.Documents
            .Include(d => d.Uploader)
            .FirstOrDefaultAsync(d => d.DocumentID == documentId);

        if (document == null)
            throw new InvalidOperationException("Document not found");

        // Create review record
        var review = new DocumentReview
        {
            DocumentId = documentId,
            ReviewedBy = staffId,
            ReviewStatus = STATUS_REJECTED,
            Remarks = remarks,
            ReviewedAt = DateTime.UtcNow,
            ReviewerRole = "Staff",
            IsChecklistComplete = false,
            ChecklistScore = checklistResults?.Count(r => r.IsPassed == true) ?? 0,
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentReviews.Add(review);
        await _context.SaveChangesAsync();

        // Add checklist results if provided
        if (checklistResults != null && checklistResults.Any())
        {
            foreach (var result in checklistResults)
            {
                result.ReviewId = review.ReviewId;
                result.CheckedAt = DateTime.UtcNow;
                _context.DocumentChecklistResults.Add(result);
            }
            await _context.SaveChangesAsync();
        }

        // Update document
        document.WorkflowStage = STAGE_STAFF_REJECTED;
        document.Status = STATUS_REJECTED;
        document.CurrentRemarks = remarks;
        document.StaffReviewedAt = DateTime.UtcNow;
        document.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Auto-archive rejected document immediately
        await AutoArchiveRejectedDocumentAsync(document, staffId, remarks, "Staff");

        // Notify client
        if (document.UploadedBy.HasValue)
        {
            await _notificationService.NotifyAsync(
                document.UploadedBy.Value,
                "Document Rejected",
                $"Your document '{document.Title}' has been rejected. Reason: {remarks}",
                "StaffRejected",
                documentId,
                $"/Document/Details/{documentId}");
        }

        // Audit log
        await _auditLogService.LogAsync(
            "StaffReject",
            "Document",
            documentId,
            $"Staff rejected document: {document.Title}. Reason: {remarks}",
            null,
            $"{{\"remarks\":\"{remarks}\"}}",
            "DocumentReview");

        return review;
    }

    /// <summary>
    /// Lawyer approves document and forwards to admin
    /// </summary>
    public async Task<DocumentReview> LawyerApproveAsync(int documentId, int lawyerId, string? remarks, string? internalNotes, List<DocumentChecklistResult>? checklistResults)
    {
        var document = await _context.Documents
            .Include(d => d.Uploader)
            .FirstOrDefaultAsync(d => d.DocumentID == documentId);

        if (document == null)
            throw new InvalidOperationException("Document not found");

        // Create review record
        var review = new DocumentReview
        {
            DocumentId = documentId,
            ReviewedBy = lawyerId,
            ReviewStatus = STATUS_APPROVED,
            Remarks = remarks,
            InternalNotes = internalNotes,
            ReviewedAt = DateTime.UtcNow,
            ReviewerRole = "Lawyer",
            IsChecklistComplete = checklistResults?.All(r => r.IsPassed == true) ?? true,
            ChecklistScore = checklistResults?.Count(r => r.IsPassed == true) ?? 0,
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentReviews.Add(review);
        await _context.SaveChangesAsync();

        // Add checklist results if provided
        if (checklistResults != null && checklistResults.Any())
        {
            foreach (var result in checklistResults)
            {
                result.ReviewId = review.ReviewId;
                result.CheckedAt = DateTime.UtcNow;
                _context.DocumentChecklistResults.Add(result);
            }
            await _context.SaveChangesAsync();
        }

        // Update document workflow
        document.LawyerReviewedAt = DateTime.UtcNow;
        document.UpdatedAt = DateTime.UtcNow;

        // Assign to admin
        var admin = await AssignToAdminAsync(documentId, document.FirmID);

        // Notify admin
        if (admin != null)
        {
            await _notificationService.NotifyAsync(
                admin.UserID,
                "New Document for Final Review",
                $"Document '{document.Title}' has been reviewed by lawyer and forwarded for your final approval.",
                "LawyerApproved",
                documentId,
                $"/Admin/Review/{documentId}");
        }

        // Notify client
        if (document.UploadedBy.HasValue)
        {
            await _notificationService.NotifyAsync(
                document.UploadedBy.Value,
                "Document Reviewed by Lawyer",
                $"Your document '{document.Title}' has been reviewed by lawyer and forwarded to admin for final approval.",
                "LawyerApproved",
                documentId,
                $"/Document/Details/{documentId}");
        }

        // Audit log
        await _auditLogService.LogAsync(
            "LawyerApprove",
            "Document",
            documentId,
            $"Lawyer approved document: {document.Title}",
            null,
            $"{{\"remarks\":\"{remarks}\"}}",
            "DocumentReview");

        return review;
    }

    /// <summary>
    /// Lawyer rejects document
    /// </summary>
    public async Task<DocumentReview> LawyerRejectAsync(int documentId, int lawyerId, string remarks, List<DocumentChecklistResult>? checklistResults)
    {
        var document = await _context.Documents
            .Include(d => d.Uploader)
            .FirstOrDefaultAsync(d => d.DocumentID == documentId);

        if (document == null)
            throw new InvalidOperationException("Document not found");

        // Create review record
        var review = new DocumentReview
        {
            DocumentId = documentId,
            ReviewedBy = lawyerId,
            ReviewStatus = STATUS_REJECTED,
            Remarks = remarks,
            ReviewedAt = DateTime.UtcNow,
            ReviewerRole = "Lawyer",
            IsChecklistComplete = false,
            ChecklistScore = checklistResults?.Count(r => r.IsPassed == true) ?? 0,
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentReviews.Add(review);
        await _context.SaveChangesAsync();

        // Add checklist results if provided
        if (checklistResults != null && checklistResults.Any())
        {
            foreach (var result in checklistResults)
            {
                result.ReviewId = review.ReviewId;
                result.CheckedAt = DateTime.UtcNow;
                _context.DocumentChecklistResults.Add(result);
            }
            await _context.SaveChangesAsync();
        }

        // Update document
        document.WorkflowStage = STAGE_LAWYER_REJECTED;
        document.Status = STATUS_REJECTED;
        document.CurrentRemarks = remarks;
        document.LawyerReviewedAt = DateTime.UtcNow;
        document.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Auto-archive rejected document
        await AutoArchiveRejectedDocumentAsync(document, lawyerId, remarks, "Lawyer");

        // Notify client
        if (document.UploadedBy.HasValue)
        {
            await _notificationService.NotifyAsync(
                document.UploadedBy.Value,
                "Document Rejected by Lawyer",
                $"Your document '{document.Title}' has been rejected by lawyer. Reason: {remarks}",
                "LawyerRejected",
                documentId,
                $"/Document/Details/{documentId}");
        }

        // Notify staff who originally reviewed
        if (document.AssignedStaffId.HasValue)
        {
            await _notificationService.NotifyAsync(
                document.AssignedStaffId.Value,
                "Document Rejected by Lawyer",
                $"Document '{document.Title}' that you reviewed has been rejected by lawyer. Reason: {remarks}",
                "LawyerRejected",
                documentId,
                $"/Staff/PendingReviews");
        }

        // Audit log
        await _auditLogService.LogAsync(
            "LawyerReject",
            "Document",
            documentId,
            $"Lawyer rejected document: {document.Title}. Reason: {remarks}",
            null,
            $"{{\"remarks\":\"{remarks}\"}}",
            "DocumentReview");

        return review;
    }

    /// <summary>
    /// Lawyer edits document (creates new version)
    /// </summary>
    public async Task<DocumentVersion> LawyerEditDocumentAsync(int documentId, int lawyerId, string filePath, string originalFileName, long fileSize, string? mimeType, string changeDescription)
    {
        var document = await _context.Documents
            .Include(d => d.Uploader)
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.DocumentID == documentId);

        if (document == null)
            throw new InvalidOperationException("Document not found");

        // Mark current version as not current
        var currentVersion = document.Versions.FirstOrDefault(v => v.IsCurrentVersion == true);
        if (currentVersion != null)
        {
            currentVersion.IsCurrentVersion = false;
        }

        // Get file extension
        var fileExtension = Path.GetExtension(originalFileName);

        // Create new version
        var newVersionNumber = (document.CurrentVersion ?? 1) + 1;
        var newVersion = new DocumentVersion
        {
            DocumentId = documentId,
            VersionNumber = newVersionNumber,
            FilePath = filePath,
            FileSize = fileSize,
            UploadedBy = lawyerId,
            OriginalFileName = originalFileName,
            FileExtension = fileExtension,
            MimeType = mimeType,
            ChangeDescription = changeDescription,
            ChangedBy = "Lawyer",
            IsCurrentVersion = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentVersions.Add(newVersion);

        // Update document
        document.CurrentVersion = newVersionNumber;
        document.TotalFileSize = fileSize;
        document.FileExtension = fileExtension;
        document.MimeType = mimeType;
        document.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Notify client
        if (document.UploadedBy.HasValue)
        {
            await _notificationService.NotifyAsync(
                document.UploadedBy.Value,
                "Document Updated by Lawyer",
                $"Your document '{document.Title}' has been updated by a lawyer. New version: {newVersionNumber}. Changes: {changeDescription}",
                "LawyerEdit",
                documentId,
                $"/Document/Details/{documentId}");
        }

        // Audit log
        await _auditLogService.LogAsync(
            "LawyerEdit",
            "Document",
            documentId,
            $"Lawyer created new version {newVersionNumber}: {changeDescription}",
            null,
            $"{{\"version\":{newVersionNumber},\"changeDescription\":\"{changeDescription}\"}}",
            "DocumentVersion");

        return newVersion;
    }

    /// <summary>
    /// Get pending documents for lawyer review
    /// </summary>
    public async Task<List<Document>> GetPendingLawyerReviewsAsync(int firmId, int? lawyerId = null)
    {
        var query = _context.Documents
            .Include(d => d.Uploader)
            .Include(d => d.Folder)
            .Include(d => d.AssignedStaff)
            .Include(d => d.AssignedLawyer)
            .Where(d => d.FirmID == firmId &&
                        (d.WorkflowStage == STAGE_PENDING_LAWYER_REVIEW || d.WorkflowStage == STAGE_LAWYER_REVIEW));

        if (lawyerId.HasValue)
        {
            query = query.Where(d => d.AssignedLawyerId == lawyerId || d.AssignedLawyerId == null);
        }

        return await query.OrderByDescending(d => d.CreatedAt).ToListAsync();
    }

    /// <summary>
    /// Staff edits document (creates new version)
    /// </summary>
    public async Task<DocumentVersion> StaffEditDocumentAsync(int documentId, int staffId, string filePath, string originalFileName, long fileSize, string? mimeType, string changeDescription)
    {
        var document = await _context.Documents
            .Include(d => d.Uploader)
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.DocumentID == documentId);

        if (document == null)
            throw new InvalidOperationException("Document not found");

        // Mark current version as not current
        var currentVersion = document.Versions.FirstOrDefault(v => v.IsCurrentVersion == true);
        if (currentVersion != null)
        {
            currentVersion.IsCurrentVersion = false;
        }

        // Get file extension
        var fileExtension = Path.GetExtension(originalFileName);

        // Create new version
        var newVersionNumber = (document.CurrentVersion ?? 1) + 1;
        var newVersion = new DocumentVersion
        {
            DocumentId = documentId,
            VersionNumber = newVersionNumber,
            FilePath = filePath,
            FileSize = fileSize,
            UploadedBy = staffId,
            OriginalFileName = originalFileName,
            FileExtension = fileExtension,
            MimeType = mimeType,
            ChangeDescription = changeDescription,
            ChangedBy = "Staff",
            IsCurrentVersion = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentVersions.Add(newVersion);

        // Update document
        document.CurrentVersion = newVersionNumber;
        document.TotalFileSize = fileSize;
        document.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Notify client
        if (document.UploadedBy.HasValue)
        {
            await _notificationService.NotifyAsync(
                document.UploadedBy.Value,
                "Document Updated",
                $"Your document '{document.Title}' has been updated by staff. Version {newVersionNumber} created. Reason: {changeDescription}",
                "DocumentVersioned",
                documentId,
                $"/Document/Details/{documentId}");
        }

        // Audit log
        await _auditLogService.LogAsync(
            "StaffEditDocument",
            "Document",
            documentId,
            $"Staff created new version {newVersionNumber} for document: {document.Title}",
            null,
            $"{{\"version\":{newVersionNumber},\"changeDescription\":\"{changeDescription}\"}}",
            "DocumentVersion");

        return newVersion;
    }

    /// <summary>
    /// Admin approves document
    /// </summary>
    public async Task<DocumentReview> AdminApproveAsync(int documentId, int adminId, string? remarks)
    {
        var document = await _context.Documents
            .Include(d => d.Uploader)
            .FirstOrDefaultAsync(d => d.DocumentID == documentId);

        if (document == null)
            throw new InvalidOperationException("Document not found");

        // Create review record
        var review = new DocumentReview
        {
            DocumentId = documentId,
            ReviewedBy = adminId,
            ReviewStatus = STATUS_APPROVED,
            Remarks = remarks,
            ReviewedAt = DateTime.UtcNow,
            ReviewerRole = "Admin",
            IsChecklistComplete = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentReviews.Add(review);

        // Update document
        document.WorkflowStage = STAGE_COMPLETED;
        document.Status = STATUS_COMPLETED;
        document.AdminReviewedAt = DateTime.UtcNow;
        document.ApprovedAt = DateTime.UtcNow;
        document.UpdatedAt = DateTime.UtcNow;

        // Apply retention policy automatically
        await ApplyRetentionOnApprovalAsync(document, adminId);

        await _context.SaveChangesAsync();

        // Notify client
        if (document.UploadedBy.HasValue)
        {
            await _notificationService.NotifyAsync(
                document.UploadedBy.Value,
                "Document Approved",
                $"Your document '{document.Title}' has been fully approved and completed.",
                "AdminApproved",
                documentId,
                $"/Document/Details/{documentId}");
        }

        // Notify assigned staff
        if (document.AssignedStaffId.HasValue)
        {
            await _notificationService.NotifyAsync(
                document.AssignedStaffId.Value,
                "Document Completed",
                $"Document '{document.Title}' that you reviewed has been approved by admin.",
                "AdminApproved",
                documentId,
                $"/Document/Details/{documentId}");
        }

        // Audit log
        await _auditLogService.LogAsync(
            "AdminApprove",
            "Document",
            documentId,
            $"Admin approved document: {document.Title}",
            null,
            null,
            "DocumentReview");

        return review;
    }

    /// <summary>
    /// Apply retention policy when document is approved
    /// </summary>
    private async Task ApplyRetentionOnApprovalAsync(Document document, int approvedBy)
    {
        // Check if retention already exists
        var existingRetention = await _context.DocumentRetentions
            .FirstOrDefaultAsync(dr => dr.DocumentID == document.DocumentID);

        if (existingRetention != null)
            return; // Already has retention

        // Find default policy for this document type
        var defaultPolicy = await _context.RetentionPolicies
            .FirstOrDefaultAsync(p => p.FirmId == document.FirmID && 
                                      p.DocumentType == document.DocumentType && 
                                      p.IsDefault == true && 
                                      p.IsActive == true);

        int retentionYears = 7; // Default 7 years
        int retentionMonths = 0;
        int retentionDays = 0;
        int? policyId = null;

        if (defaultPolicy != null)
        {
            retentionYears = defaultPolicy.RetentionYears ?? 7;
            retentionMonths = defaultPolicy.RetentionMonths ?? 0;
            retentionDays = defaultPolicy.RetentionDays ?? 0;
            policyId = defaultPolicy.PolicyID;
        }

        var startDate = DateTime.UtcNow;
        var expiryDate = startDate
            .AddYears(retentionYears)
            .AddMonths(retentionMonths)
            .AddDays(retentionDays);

        var retention = new DocumentRetention
        {
            DocumentID = document.DocumentID,
            PolicyID = policyId,
            FirmId = document.FirmID,
            RetentionStartDate = startDate,
            ExpiryDate = expiryDate,
            RetentionYears = retentionYears,
            RetentionMonths = retentionMonths,
            RetentionDays = retentionDays,
            IsArchived = false,
            CreatedBy = approvedBy,
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentRetentions.Add(retention);

        // Update document with retention expiry
        document.RetentionExpiryDate = expiryDate;

        await _auditLogService.LogAsync(
            "ApplyRetention",
            "Document",
            document.DocumentID,
            $"Auto-applied retention to approved document: {document.Title}. Expiry: {expiryDate}",
            null,
            null,
            "RetentionManagement");
    }

    /// <summary>
    /// Admin approves document with custom retention
    /// </summary>
    public async Task<(DocumentReview review, DocumentRetention retention)> AdminApproveWithRetentionAsync(
        int documentId, int adminId, string? remarks, int? policyId, int? retentionYears, int? retentionMonths, int? retentionDays)
    {
        var document = await _context.Documents
            .Include(d => d.Uploader)
            .FirstOrDefaultAsync(d => d.DocumentID == documentId);

        if (document == null)
            throw new InvalidOperationException("Document not found");

        // Create review record
        var review = new DocumentReview
        {
            DocumentId = documentId,
            ReviewedBy = adminId,
            ReviewStatus = STATUS_APPROVED,
            Remarks = remarks,
            ReviewedAt = DateTime.UtcNow,
            ReviewerRole = "Admin",
            IsChecklistComplete = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentReviews.Add(review);

        // Update document
        document.WorkflowStage = STAGE_COMPLETED;
        document.Status = STATUS_COMPLETED;
        document.AdminReviewedAt = DateTime.UtcNow;
        document.ApprovedAt = DateTime.UtcNow;
        document.UpdatedAt = DateTime.UtcNow;

        // Apply custom retention
        int years = retentionYears ?? 7;
        int months = retentionMonths ?? 0;
        int days = retentionDays ?? 0;

        if (policyId.HasValue)
        {
            var policy = await _context.RetentionPolicies.FindAsync(policyId);
            if (policy != null)
            {
                years = policy.RetentionYears ?? 7;
                months = policy.RetentionMonths ?? 0;
                days = policy.RetentionDays ?? 0;
            }
        }

        var startDate = DateTime.UtcNow;
        var expiryDate = startDate.AddYears(years).AddMonths(months).AddDays(days);

        var retention = new DocumentRetention
        {
            DocumentID = documentId,
            PolicyID = policyId,
            FirmId = document.FirmID,
            RetentionStartDate = startDate,
            ExpiryDate = expiryDate,
            RetentionYears = years,
            RetentionMonths = months,
            RetentionDays = days,
            IsArchived = false,
            CreatedBy = adminId,
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentRetentions.Add(retention);
        document.RetentionExpiryDate = expiryDate;

        await _context.SaveChangesAsync();

        // Notify client
        if (document.UploadedBy.HasValue)
        {
            await _notificationService.NotifyAsync(
                document.UploadedBy.Value,
                "Document Approved",
                $"Your document '{document.Title}' has been approved with a {years} year(s), {months} month(s) retention period.",
                "AdminApproved",
                documentId,
                $"/Document/Details/{documentId}");
        }

        // Notify assigned staff
        if (document.AssignedStaffId.HasValue)
        {
            await _notificationService.NotifyAsync(
                document.AssignedStaffId.Value,
                "Document Completed",
                $"Document '{document.Title}' that you reviewed has been approved by admin.",
                "AdminApproved",
                documentId,
                $"/Document/Details/{documentId}");
        }

        // Audit log
        await _auditLogService.LogAsync(
            "AdminApproveWithRetention",
            "Document",
            documentId,
            $"Admin approved document with custom retention: {document.Title}. Expiry: {expiryDate}",
            null,
            null,
            "DocumentReview");

        return (review, retention);
    }

    /// <summary>
    /// Admin rejects document
    /// </summary>
    public async Task<DocumentReview> AdminRejectAsync(int documentId, int adminId, string remarks)
    {
        var document = await _context.Documents
            .Include(d => d.Uploader)
            .FirstOrDefaultAsync(d => d.DocumentID == documentId);

        if (document == null)
            throw new InvalidOperationException("Document not found");

        // Create review record
        var review = new DocumentReview
        {
            DocumentId = documentId,
            ReviewedBy = adminId,
            ReviewStatus = STATUS_REJECTED,
            Remarks = remarks,
            ReviewedAt = DateTime.UtcNow,
            ReviewerRole = "Admin",
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentReviews.Add(review);

        // Update document
        document.WorkflowStage = STAGE_ADMIN_REJECTED;
        document.Status = STATUS_REJECTED;
        document.CurrentRemarks = remarks;
        document.AdminReviewedAt = DateTime.UtcNow;
        document.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Auto-archive rejected document immediately
        await AutoArchiveRejectedDocumentAsync(document, adminId, remarks, "Admin");

        // Notify client
        if (document.UploadedBy.HasValue)
        {
            await _notificationService.NotifyAsync(
                document.UploadedBy.Value,
                "Document Rejected by Admin",
                $"Your document '{document.Title}' has been rejected by admin. Reason: {remarks}",
                "AdminRejected",
                documentId,
                $"/Document/Details/{documentId}");
        }

        // Notify assigned staff
        if (document.AssignedStaffId.HasValue)
        {
            await _notificationService.NotifyAsync(
                document.AssignedStaffId.Value,
                "Document Rejected by Admin",
                $"Document '{document.Title}' that you reviewed has been rejected by admin. Reason: {remarks}",
                "AdminRejected",
                documentId,
                $"/Document/Details/{documentId}");
        }

        // Audit log
        await _auditLogService.LogAsync(
            "AdminReject",
            "Document",
            documentId,
            $"Admin rejected document: {document.Title}. Reason: {remarks}",
            null,
            $"{{\"remarks\":\"{remarks}\"}}",
            "DocumentReview");

        return review;
    }

    /// <summary>
    /// Auto-archive rejected document immediately
    /// </summary>
    private async Task AutoArchiveRejectedDocumentAsync(Document document, int rejectedBy, string rejectionReason, string rejectorRole)
    {
        try
        {
            // Check if already archived
            var existingArchive = await _context.Archives
                .FirstOrDefaultAsync(a => a.DocumentID == document.DocumentID && a.IsRestored != true);

            if (existingArchive != null)
            {
                _logger.LogInformation("Document {DocumentId} already archived, skipping auto-archive", document.DocumentID);
                return;
            }

            var archive = new Archive
            {
                DocumentID = document.DocumentID,
                FirmId = document.FirmID,
                ArchivedDate = DateTime.UtcNow,
                Reason = $"[{rejectorRole} Rejection] {rejectionReason}",
                ArchiveType = "Rejected",
                OriginalStatus = document.Status,
                OriginalWorkflowStage = document.WorkflowStage,
                OriginalFolderId = document.FolderId,
                VersionNumber = document.CurrentVersion ?? 1,
                ArchivedBy = rejectedBy,
                IsRestored = false,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.Archives.Add(archive);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Auto-archived rejected document {DocumentId} by {Role}", document.DocumentID, rejectorRole);

            // Audit log
            await _auditLogService.LogAsync(
                "AutoArchiveRejected",
                "Archive",
                archive.ArchiveID,
                $"Auto-archived rejected document: {document.Title}. Rejected by: {rejectorRole}",
                null,
                $"{{\"rejectionReason\":\"{rejectionReason}\",\"rejectorRole\":\"{rejectorRole}\"}}",
                "ArchiveManagement");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error auto-archiving rejected document {DocumentId}", document.DocumentID);
            // Don't throw - rejection should still succeed even if archive fails
        }
    }

    /// <summary>
    /// Archive a document
    /// </summary>
    public async Task<Archive> ArchiveDocumentAsync(int documentId, int archivedBy, string reason, string archiveType = "Manual")
    {
        var document = await _context.Documents
            .Include(d => d.Uploader)
            .FirstOrDefaultAsync(d => d.DocumentID == documentId);

        if (document == null)
            throw new InvalidOperationException("Document not found");

        var archive = new Archive
        {
            DocumentID = documentId,
            FirmId = document.FirmID,
            ArchivedDate = DateTime.UtcNow,
            Reason = reason,
            ArchiveType = archiveType,
            OriginalRetentionDate = document.RetentionExpiryDate,
            ArchivedBy = archivedBy,
            IsRestored = false,
            OriginalStatus = document.Status,
            OriginalWorkflowStage = document.WorkflowStage,
            OriginalFolderId = document.FolderId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Archives.Add(archive);

        // Update document
        document.WorkflowStage = STAGE_ARCHIVED;
        document.Status = STATUS_ARCHIVED;
        document.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Notify client (non-blocking - archive succeeds even if notification fails)
        try
        {
            if (document.UploadedBy.HasValue)
            {
                await _notificationService.NotifyAsync(
                    document.UploadedBy.Value,
                    "Document Archived",
                    $"Your document '{document.Title}' has been archived. Reason: {reason}",
                    "DocumentArchived",
                    documentId,
                    $"/Document/Details/{documentId}");
            }
        }
        catch (Exception notifEx)
        {
            _logger.LogWarning(notifEx, "Failed to send notification for archive of document {DocumentId}", documentId);
        }

        // Audit log (non-blocking)
        try
        {
            await _auditLogService.LogAsync(
                "ArchiveDocument",
                "Document",
                documentId,
                $"Document archived: {document.Title}. Reason: {reason}",
                null,
                $"{{\"reason\":\"{reason}\",\"archiveType\":\"{archiveType}\"}}",
                "DocumentArchive");
        }
        catch (Exception auditEx)
        {
            _logger.LogWarning(auditEx, "Failed to log audit for archive of document {DocumentId}", documentId);
        }

        return archive;
    }

    /// <summary>
    /// Get document workflow history
    /// </summary>
    public async Task<List<DocumentReview>> GetDocumentReviewHistoryAsync(int documentId)
    {
        return await _context.DocumentReviews
            .Include(r => r.Reviewer)
            .Include(r => r.ChecklistResults)
            .ThenInclude(cr => cr.ChecklistItem)
            .Where(r => r.DocumentId == documentId)
            .OrderByDescending(r => r.ReviewedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Get pending documents for staff
    /// </summary>
    public async Task<List<Document>> GetPendingStaffReviewsAsync(int firmId, int? staffId = null)
    {
        var query = _context.Documents
            .Include(d => d.Uploader)
            .Include(d => d.Folder)
            .Include(d => d.Versions.OrderByDescending(v => v.VersionNumber).Take(1))
            .Where(d => d.FirmID == firmId &&
                        (d.WorkflowStage == STAGE_CLIENT_UPLOAD || 
                         d.WorkflowStage == STAGE_PENDING_STAFF_REVIEW || 
                         d.WorkflowStage == STAGE_STAFF_REVIEW));

        if (staffId.HasValue)
        {
            // For assigned filter, include unassigned documents as well (ClientUpload stage)
            query = query.Where(d => d.AssignedStaffId == staffId || d.AssignedStaffId == null);
        }

        return await query.OrderByDescending(d => d.CreatedAt).ToListAsync();
    }

    /// <summary>
    /// Get pending documents for admin
    /// </summary>
    public async Task<List<Document>> GetPendingAdminReviewsAsync(int firmId, int? adminId = null)
    {
        var query = _context.Documents
            .Include(d => d.Uploader)
            .Include(d => d.Folder)
            .Include(d => d.AssignedStaff)
            .Include(d => d.Reviews.OrderByDescending(r => r.ReviewedAt).Take(1))
            .Where(d => d.FirmID == firmId &&
                        (d.WorkflowStage == STAGE_PENDING_ADMIN_REVIEW || d.WorkflowStage == STAGE_ADMIN_REVIEW));

        if (adminId.HasValue)
        {
            query = query.Where(d => d.AssignedAdminId == adminId);
        }

        return await query.OrderByDescending(d => d.CreatedAt).ToListAsync();
    }
}
