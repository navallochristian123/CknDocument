using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;
using Microsoft.EntityFrameworkCore;

namespace CKNDocument.Services;

/// <summary>
/// Service for managing document workflow stages
/// Workflow: ClientUpload â†’ PendingStaffReview â†’ StaffReview â†’ PendingAdminReview â†’ AdminReview â†’ Approved â†’ Completed
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
    public const string STAGE_PENDING_SECOND_OPINION = "PendingSecondOpinion";
    public const string STAGE_SECOND_OPINION_REVIEW = "SecondOpinionReview";
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
            _logger.LogWarning("No active staff members found for firm {FirmId}. Document {DocumentId} will be set to PendingStaffReview (unassigned) so staff can claim it later.", firmId, documentId);
            
            // Still move document to PendingStaffReview so it appears in the queue
            // when a staff member is eventually created
            var unassignedDoc = await _context.Documents.FindAsync(documentId);
            if (unassignedDoc != null)
            {
                unassignedDoc.WorkflowStage = STAGE_PENDING_STAFF_REVIEW;
                unassignedDoc.Status = STATUS_PENDING;
                await _context.SaveChangesAsync();
            }
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
            _logger.LogWarning("No active admin members found for firm {FirmId}. Document {DocumentId} will be set to PendingAdminReview (unassigned).", firmId, documentId);
            
            // Still move document to PendingAdminReview so it appears in the queue
            var unassignedDoc = await _context.Documents.FindAsync(documentId);
            if (unassignedDoc != null)
            {
                unassignedDoc.WorkflowStage = STAGE_PENDING_ADMIN_REVIEW;
                await _context.SaveChangesAsync();
            }
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
            _logger.LogWarning("No active lawyers found for firm {FirmId}. Document {DocumentId} will be set to PendingLawyerReview (unassigned).", firmId, documentId);
            
            // Still move document to PendingLawyerReview so it appears in the queue
            var unassignedDoc = await _context.Documents.FindAsync(documentId);
            if (unassignedDoc != null)
            {
                unassignedDoc.WorkflowStage = STAGE_PENDING_LAWYER_REVIEW;
                await _context.SaveChangesAsync();
            }
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

        // Assign to lawyer (new workflow: Staff â†’ Lawyer â†’ Admin)
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

        // Create new version â€” Lawyer upload â†’ MAJOR version label
        var newVersionNumber = (document.CurrentVersion ?? 1) + 1;
        var existingLabels = document.Versions
            .OrderBy(v => v.VersionNumber)
            .Select(v => v.VersionLabel)
            .ToList();
        var newLabel = CalcMajorVersionLabel(existingLabels);

        var newVersion = new DocumentVersion
        {
            DocumentId = documentId,
            VersionNumber = newVersionNumber,
            VersionLabel = newLabel,
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

        // Update document â€” sync all fields with the new current version
        document.CurrentVersion = newVersionNumber;
        document.TotalFileSize = fileSize;
        document.OriginalFileName = originalFileName;
        document.FileExtension = fileExtension;
        document.MimeType = mimeType;
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

        // Create new version â€” Staff upload â†’ MINOR version label
        var newVersionNumber = (document.CurrentVersion ?? 1) + 1;
        var existingLabels = document.Versions
            .OrderBy(v => v.VersionNumber)
            .Select(v => v.VersionLabel)
            .ToList();
        var newLabel = CalcMinorVersionLabel(existingLabels);

        var newVersion = new DocumentVersion
        {
            DocumentId = documentId,
            VersionNumber = newVersionNumber,
            VersionLabel = newLabel,
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

        // Update document â€” sync all fields with the new current version
        document.CurrentVersion = newVersionNumber;
        document.TotalFileSize = fileSize;
        document.OriginalFileName = originalFileName;
        document.FileExtension = fileExtension;
        document.MimeType = mimeType;
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

        // Sync main document record with the current version's filename
        var currentVersion = await _context.DocumentVersions
            .Where(v => v.DocumentId == documentId && v.IsCurrentVersion == true)
            .FirstOrDefaultAsync();
        if (currentVersion != null && !string.IsNullOrEmpty(currentVersion.OriginalFileName))
        {
            document.OriginalFileName = currentVersion.OriginalFileName;
            document.FileExtension = currentVersion.FileExtension;
            document.MimeType = currentVersion.MimeType;
            document.TotalFileSize = currentVersion.FileSize;
            document.CurrentVersion = currentVersion.VersionNumber;
        }

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

        // Find default policy for this document type, or fallback to "All Types" policy
        var defaultPolicy = await _context.RetentionPolicies
            .FirstOrDefaultAsync(p => p.FirmId == document.FirmID && 
                                      p.DocumentType == document.DocumentType && 
                                      p.IsDefault == true && 
                                      p.IsActive == true);

        // If no type-specific policy, try "All Types" policy (empty or null DocumentType)
        if (defaultPolicy == null)
        {
            defaultPolicy = await _context.RetentionPolicies
                .FirstOrDefaultAsync(p => p.FirmId == document.FirmID && 
                                          (p.DocumentType == null || p.DocumentType == "") && 
                                          p.IsDefault == true && 
                                          p.IsActive == true);
        }

        // Last resort: find ANY active default policy for this firm
        if (defaultPolicy == null)
        {
            defaultPolicy = await _context.RetentionPolicies
                .FirstOrDefaultAsync(p => p.FirmId == document.FirmID && 
                                          p.IsDefault == true && 
                                          p.IsActive == true);
        }

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

        // Sync main document record with the current version's filename
        var curVer = await _context.DocumentVersions
            .Where(v => v.DocumentId == document.DocumentID && v.IsCurrentVersion == true)
            .FirstOrDefaultAsync();
        if (curVer != null && !string.IsNullOrEmpty(curVer.OriginalFileName))
        {
            document.OriginalFileName = curVer.OriginalFileName;
            document.FileExtension = curVer.FileExtension;
            document.MimeType = curVer.MimeType;
            document.TotalFileSize = curVer.FileSize;
            document.CurrentVersion = curVer.VersionNumber;
        }

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
            .Include(d => d.AssignedStaff)
            .Include(d => d.Versions.OrderByDescending(v => v.VersionNumber).Take(1))
            .Where(d => d.FirmID == firmId &&
                        !d.IsHighRisk &&
                        (d.WorkflowStage == STAGE_CLIENT_UPLOAD || 
                         d.WorkflowStage == STAGE_PENDING_STAFF_REVIEW || 
                         d.WorkflowStage == STAGE_STAFF_REVIEW ||
                         d.WorkflowStage == STAGE_STAFF_REJECTED));

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

    // ==========================================
    // HIGH-RISK / SECOND OPINION WORKFLOW METHODS
    // ==========================================

    /// <summary>
    /// Lawyer partially approves a document and assigns to another lawyer for 2nd opinion.
    /// This is available for all documents but primarily designed for high-risk ones.
    /// </summary>
    public async Task<SecondOpinionRequest> RequestSecondOpinionAsync(
        int documentId, int requestingLawyerId, int assignedToLawyerId, string remarks)
    {
        var document = await _context.Documents
            .Include(d => d.Uploader)
            .FirstOrDefaultAsync(d => d.DocumentID == documentId);

        if (document == null)
            throw new InvalidOperationException("Document not found");

        // Validate the 2nd lawyer belongs to the same firm
        var secondLawyer = await _context.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.UserID == assignedToLawyerId && 
                                      u.FirmID == document.FirmID &&
                                      u.Status == "Active" &&
                                      u.UserRoles.Any(ur => ur.Role != null && ur.Role.RoleName == "Lawyer"));

        if (secondLawyer == null)
            throw new InvalidOperationException("Selected lawyer not found or not active in this firm");

        if (assignedToLawyerId == requestingLawyerId)
            throw new InvalidOperationException("Cannot assign 2nd opinion to yourself");

        var requestingLawyer = await _context.Users.FindAsync(requestingLawyerId);

        // Create the 2nd opinion request record
        var request = new SecondOpinionRequest
        {
            DocumentId = documentId,
            FirmId = document.FirmID,
            RequestedByLawyerId = requestingLawyerId,
            AssignedToLawyerId = assignedToLawyerId,
            RequestRemarks = remarks,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        _context.SecondOpinionRequests.Add(request);

        // Create a review record for the partial approval
        var review = new DocumentReview
        {
            DocumentId = documentId,
            ReviewedBy = requestingLawyerId,
            ReviewStatus = "PartiallyApproved",
            Remarks = $"Partially approved - Requesting 2nd opinion. Reason: {remarks}",
            ReviewedAt = DateTime.UtcNow,
            ReviewerRole = "Lawyer",
            ReviewerType = "FirstOpinion",
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentReviews.Add(review);

        // Update document workflow stage
        document.WorkflowStage = STAGE_PENDING_SECOND_OPINION;
        document.SecondOpinionLawyerId = assignedToLawyerId;
        document.FirstOpinionLawyerId = requestingLawyerId;
        document.SecondOpinionRemarks = remarks;
        document.CurrentRemarks = $"Awaiting 2nd opinion from {secondLawyer.FullName}";

        await _context.SaveChangesAsync();

        // Notify 2nd lawyer
        await _notificationService.NotifyAsync(
            assignedToLawyerId,
            "ðŸ” 2nd Opinion Requested",
            $"Lawyer {requestingLawyer?.FullName} has requested your 2nd opinion on document '{document.Title}'. Reason: {remarks}",
            "SecondOpinionRequested",
            documentId,
            $"/Document/AssignedToMe");

        // Notify client
        if (document.UploadedBy.HasValue)
        {
            await _notificationService.NotifyAsync(
                document.UploadedBy.Value,
                "Document Under Extended Review",
                $"Your document '{document.Title}' is being reviewed by an additional lawyer for thorough review.",
                "SecondOpinionRequested",
                documentId,
                $"/Document/MyDocuments");
        }

        // Audit log
        await _auditLogService.LogAsync(
            "RequestSecondOpinion",
            "Document",
            documentId,
            $"Lawyer {requestingLawyer?.FullName} requested 2nd opinion from {secondLawyer.FullName} for document: {document.Title}. Reason: {remarks}",
            null,
            $"{{\"requestedBy\":{requestingLawyerId},\"assignedTo\":{assignedToLawyerId},\"remarks\":\"{remarks}\"}}",
            "DocumentReview");

        return request;
    }

    /// <summary>
    /// 2nd lawyer approves the document â†’ forwards to admin
    /// </summary>
    public async Task<DocumentReview> SecondOpinionApproveAsync(
        int documentId, int secondLawyerId, string? remarks)
    {
        var document = await _context.Documents
            .Include(d => d.Uploader)
            .Include(d => d.FirstOpinionLawyer)
            .FirstOrDefaultAsync(d => d.DocumentID == documentId);

        if (document == null)
            throw new InvalidOperationException("Document not found");

        if (document.SecondOpinionLawyerId != secondLawyerId)
            throw new InvalidOperationException("This document is not assigned to you for 2nd opinion");

        // Update the pending request
        var pendingRequest = await _context.SecondOpinionRequests
            .Where(r => r.DocumentId == documentId && r.AssignedToLawyerId == secondLawyerId && r.Status == "Pending")
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        if (pendingRequest != null)
        {
            pendingRequest.Status = "Approved";
            pendingRequest.ResponseRemarks = remarks;
            pendingRequest.RespondedAt = DateTime.UtcNow;
            pendingRequest.UpdatedAt = DateTime.UtcNow;
        }

        var secondLawyer = await _context.Users.FindAsync(secondLawyerId);

        // Create review record
        var review = new DocumentReview
        {
            DocumentId = documentId,
            ReviewedBy = secondLawyerId,
            ReviewStatus = STATUS_APPROVED,
            Remarks = $"2nd opinion approved. {remarks}",
            ReviewedAt = DateTime.UtcNow,
            ReviewerRole = "Lawyer",
            ReviewerType = "SecondOpinion",
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentReviews.Add(review);

        // Update document and forward to admin
        document.LawyerReviewedAt = DateTime.UtcNow;
        document.CurrentRemarks = $"Approved by 2nd opinion lawyer ({secondLawyer?.FullName}). {remarks}";

        // Clear 2nd opinion fields (keep FirstOpinionLawyerId for audit trail)
        document.SecondOpinionLawyerId = null;
        document.SecondOpinionRemarks = null;

        await _context.SaveChangesAsync();

        // Assign to admin
        var admin = await AssignToAdminAsync(documentId, document.FirmID);

        // Notify first lawyer
        if (document.FirstOpinionLawyerId.HasValue)
        {
            await _notificationService.NotifyAsync(
                document.FirstOpinionLawyerId.Value,
                "2nd Opinion: Document Approved",
                $"Lawyer {secondLawyer?.FullName} has approved document '{document.Title}' and forwarded it to admin.",
                "SecondOpinionApproved",
                documentId,
                $"/Document/AssignedToMe");
        }

        // Notify admin
        if (admin != null)
        {
            await _notificationService.NotifyAsync(
                admin.UserID,
                "Document for Final Review",
                $"Document '{document.Title}'{(document.IsHighRisk ? " [HIGH-RISK]" : "")} has been reviewed by two lawyers and is ready for your final approval.",
                "LawyerApproved",
                documentId,
                $"/Admin/Review/{documentId}");
        }

        // Notify client
        if (document.UploadedBy.HasValue)
        {
            await _notificationService.NotifyAsync(
                document.UploadedBy.Value,
                "Document Review Progress",
                $"Your document '{document.Title}' has been approved by lawyers and forwarded to admin for final approval.",
                "SecondOpinionApproved",
                documentId,
                $"/Document/MyDocuments");
        }

        // Audit log
        await _auditLogService.LogAsync(
            "SecondOpinionApprove",
            "Document",
            documentId,
            $"2nd opinion lawyer {secondLawyer?.FullName} approved document: {document.Title}",
            null,
            $"{{\"secondLawyerId\":{secondLawyerId},\"remarks\":\"{remarks}\"}}",
            "DocumentReview");

        return review;
    }

    /// <summary>
    /// 2nd lawyer returns the document to the 1st lawyer with remarks
    /// </summary>
    public async Task<DocumentReview> SecondOpinionReturnAsync(
        int documentId, int secondLawyerId, string remarks)
    {
        var document = await _context.Documents
            .Include(d => d.Uploader)
            .Include(d => d.FirstOpinionLawyer)
            .FirstOrDefaultAsync(d => d.DocumentID == documentId);

        if (document == null)
            throw new InvalidOperationException("Document not found");

        if (document.SecondOpinionLawyerId != secondLawyerId)
            throw new InvalidOperationException("This document is not assigned to you for 2nd opinion");

        // Update the pending request
        var pendingRequest = await _context.SecondOpinionRequests
            .Where(r => r.DocumentId == documentId && r.AssignedToLawyerId == secondLawyerId && r.Status == "Pending")
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        if (pendingRequest != null)
        {
            pendingRequest.Status = "Returned";
            pendingRequest.ResponseRemarks = remarks;
            pendingRequest.RespondedAt = DateTime.UtcNow;
            pendingRequest.UpdatedAt = DateTime.UtcNow;
        }

        var secondLawyer = await _context.Users.FindAsync(secondLawyerId);

        // Create review record
        var review = new DocumentReview
        {
            DocumentId = documentId,
            ReviewedBy = secondLawyerId,
            ReviewStatus = "Returned",
            Remarks = $"Returned to 1st lawyer. Reason: {remarks}",
            ReviewedAt = DateTime.UtcNow,
            ReviewerRole = "Lawyer",
            ReviewerType = "SecondOpinion",
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentReviews.Add(review);

        // Return document to the first lawyer's review
        document.WorkflowStage = STAGE_PENDING_LAWYER_REVIEW;
        document.AssignedLawyerId = document.FirstOpinionLawyerId;
        document.SecondOpinionLawyerId = null;
        document.SecondOpinionRemarks = null;
        document.CurrentRemarks = $"Returned by {secondLawyer?.FullName}: {remarks}";

        await _context.SaveChangesAsync();

        // Notify 1st lawyer
        if (document.FirstOpinionLawyerId.HasValue)
        {
            await _notificationService.NotifyAsync(
                document.FirstOpinionLawyerId.Value,
                "âš ï¸ Document Returned from 2nd Opinion",
                $"Lawyer {secondLawyer?.FullName} has returned document '{document.Title}' for your review. Reason: {remarks}",
                "SecondOpinionReturned",
                documentId,
                $"/Document/AssignedToMe");
        }

        // Audit log
        await _auditLogService.LogAsync(
            "SecondOpinionReturn",
            "Document",
            documentId,
            $"2nd opinion lawyer {secondLawyer?.FullName} returned document: {document.Title}. Reason: {remarks}",
            null,
            $"{{\"secondLawyerId\":{secondLawyerId},\"returnedToLawyerId\":{document.FirstOpinionLawyerId},\"remarks\":\"{remarks}\"}}",
            "DocumentReview");

        return review;
    }

    /// <summary>
    /// Get lawyers in the same firm (for 2nd opinion dropdown), excluding the requesting lawyer
    /// </summary>
    public async Task<List<User>> GetFirmLawyersAsync(int firmId, int? excludeLawyerId = null)
    {
        var query = _context.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Where(u => u.FirmID == firmId &&
                        u.Status == "Active" &&
                        u.UserRoles.Any(ur => ur.Role != null && ur.Role.RoleName == "Lawyer"));

        if (excludeLawyerId.HasValue)
        {
            query = query.Where(u => u.UserID != excludeLawyerId.Value);
        }

        return await query.OrderBy(u => u.FirstName).ThenBy(u => u.LastName).ToListAsync();
    }

    /// <summary>
    /// Get documents assigned to a lawyer for 2nd opinion review
    /// </summary>
    public async Task<List<Document>> GetSecondOpinionAssignedToMeAsync(int lawyerId)
    {
        return await _context.Documents
            .Include(d => d.Uploader)
            .Include(d => d.Folder)
            .Include(d => d.FirstOpinionLawyer)
            .Where(d => d.SecondOpinionLawyerId == lawyerId &&
                        (d.WorkflowStage == STAGE_PENDING_SECOND_OPINION || d.WorkflowStage == STAGE_SECOND_OPINION_REVIEW))
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Get documents that a lawyer has sent out for 2nd opinion
    /// </summary>
    public async Task<List<Document>> GetSecondOpinionSentByMeAsync(int lawyerId)
    {
        return await _context.Documents
            .Include(d => d.Uploader)
            .Include(d => d.Folder)
            .Include(d => d.SecondOpinionLawyer)
            .Where(d => d.FirstOpinionLawyerId == lawyerId &&
                        (d.WorkflowStage == STAGE_PENDING_SECOND_OPINION || d.WorkflowStage == STAGE_SECOND_OPINION_REVIEW))
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Get the 2nd opinion request history for a document
    /// </summary>
    public async Task<List<SecondOpinionRequest>> GetSecondOpinionHistoryAsync(int documentId)
    {
        return await _context.SecondOpinionRequests
            .Include(r => r.RequestedByLawyer)
            .Include(r => r.AssignedToLawyer)
            .Where(r => r.DocumentId == documentId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    // â”€â”€â”€ Version-label helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Next MINOR version label: "1" â†’ "1.1", "1.1" â†’ "1.2", "2" â†’ "2.1"
    /// </summary>
    private static string CalcMinorVersionLabel(IEnumerable<string?> existingLabels)
    {
        var labels = existingLabels.Where(l => !string.IsNullOrEmpty(l)).Select(l => l!).ToList();
        if (!labels.Any()) return "1.1";
        var latest = labels.Last();
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
    /// Next MAJOR version label: "1.1" â†’ "2", "2.1" â†’ "3"
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

    /// <summary>
    /// Admin overrides the workflow stage of a document.
    /// Allows admin to skip staff/lawyer review and move the document
    /// directly to a target stage (e.g. from StaffReview to LawyerReview,
    /// or from LawyerReview to AdminReview/Completed).
    /// </summary>
    public async Task<DocumentReview> AdminOverrideWorkflowAsync(
        int documentId, int adminId, string targetStage, string? remarks,
        string? metadataTitle, string? metadataDescription, string? metadataCategory,
        string? metadataDocumentType, string? metadataTags)
    {
        var document = await _context.Documents
            .Include(d => d.Uploader)
            .FirstOrDefaultAsync(d => d.DocumentID == documentId);

        if (document == null)
            throw new InvalidOperationException("Document not found");

        var previousStage = document.WorkflowStage;

        // Apply metadata updates if provided
        bool metadataChanged = false;
        if (!string.IsNullOrWhiteSpace(metadataTitle) && metadataTitle != document.Title)
        { document.Title = metadataTitle; metadataChanged = true; }
        if (!string.IsNullOrWhiteSpace(metadataDescription) && metadataDescription != document.Description)
        { document.Description = metadataDescription; metadataChanged = true; }
        if (!string.IsNullOrWhiteSpace(metadataCategory) && metadataCategory != document.Category)
        { document.Category = metadataCategory; metadataChanged = true; }
        if (!string.IsNullOrWhiteSpace(metadataDocumentType) && metadataDocumentType != document.DocumentType)
        { document.DocumentType = metadataDocumentType; metadataChanged = true; }
        if (metadataTags != null && metadataTags != document.Tags)
        { document.Tags = metadataTags; metadataChanged = true; }

        // If metadata was changed, create a minor version snapshot
        if (metadataChanged)
        {
            var existingVersions = await _context.DocumentVersions
                .Where(v => v.DocumentId == documentId)
                .OrderByDescending(v => v.VersionNumber)
                .ToListAsync();

            if (existingVersions.Any())
            {
                var currentVer = existingVersions.FirstOrDefault(v => v.IsCurrentVersion == true)
                               ?? existingVersions.First();

                var newLabel = CalcMinorVersionLabel(
                    existingVersions.Select(v => v.VersionLabel).ToList());
                var nextVersionNumber = existingVersions.Max(v => v.VersionNumber) + 1;

                foreach (var v in existingVersions)
                    v.IsCurrentVersion = false;

                var metaVersion = new DocumentVersion
                {
                    DocumentId = documentId,
                    VersionNumber = nextVersionNumber,
                    VersionLabel = newLabel,
                    FilePath = currentVer.FilePath,
                    FileSize = currentVer.FileSize,
                    OriginalFileName = currentVer.OriginalFileName,
                    FileExtension = currentVer.FileExtension,
                    MimeType = currentVer.MimeType,
                    UploadedBy = adminId,
                    ChangeDescription = $"Admin override: metadata updated and workflow moved from {previousStage} to {targetStage}",
                    ChangedBy = "Admin",
                    IsCurrentVersion = true,
                    CreatedAt = DateTime.UtcNow
                };

                _context.DocumentVersions.Add(metaVersion);
                document.CurrentVersion = nextVersionNumber;
            }
        }

        // Apply the workflow stage transition
        switch (targetStage)
        {
            case STAGE_PENDING_LAWYER_REVIEW:
            case STAGE_LAWYER_REVIEW:
                document.StaffReviewedAt = DateTime.UtcNow;
                // Auto-assign to lawyer if moving to lawyer review
                await AssignToLawyerAsync(documentId, document.FirmID);
                break;

            case STAGE_PENDING_ADMIN_REVIEW:
            case STAGE_ADMIN_REVIEW:
                document.StaffReviewedAt ??= DateTime.UtcNow;
                document.LawyerReviewedAt = DateTime.UtcNow;
                document.WorkflowStage = targetStage;
                document.Status = STATUS_UNDER_REVIEW;
                document.AssignedAdminId = adminId;
                break;

            case STAGE_COMPLETED:
                document.StaffReviewedAt ??= DateTime.UtcNow;
                document.LawyerReviewedAt ??= DateTime.UtcNow;
                document.AdminReviewedAt = DateTime.UtcNow;
                document.ApprovedAt = DateTime.UtcNow;
                document.WorkflowStage = STAGE_COMPLETED;
                document.Status = STATUS_COMPLETED;
                await ApplyRetentionOnApprovalAsync(document, adminId);
                break;

            default:
                throw new InvalidOperationException($"Invalid target stage: {targetStage}");
        }

        // Create a review record for the override
        var review = new DocumentReview
        {
            DocumentId = documentId,
            ReviewedBy = adminId,
            ReviewStatus = targetStage == STAGE_COMPLETED ? STATUS_APPROVED : "Override",
            Remarks = $"[Admin Override] Moved from {previousStage} to {targetStage}." +
                      (string.IsNullOrWhiteSpace(remarks) ? "" : $" Remarks: {remarks}"),
            ReviewedAt = DateTime.UtcNow,
            ReviewerRole = "Admin",
            IsChecklistComplete = targetStage == STAGE_COMPLETED,
            CreatedAt = DateTime.UtcNow
        };

        _context.DocumentReviews.Add(review);
        await _context.SaveChangesAsync();

        // Notify relevant parties
        if (document.UploadedBy.HasValue)
        {
            await _notificationService.NotifyAsync(
                document.UploadedBy.Value,
                "Document Workflow Updated",
                $"Your document '{document.Title}' workflow has been updated by admin. New stage: {targetStage}.",
                "AdminOverride",
                documentId,
                $"/Document/Details/{documentId}");
        }

        if (document.AssignedStaffId.HasValue)
        {
            await _notificationService.NotifyAsync(
                document.AssignedStaffId.Value,
                "Document Workflow Override",
                $"Document '{document.Title}' has been moved from {previousStage} to {targetStage} by admin.",
                "AdminOverride",
                documentId,
                $"/Document/Details/{documentId}");
        }

        if (document.AssignedLawyerId.HasValue)
        {
            await _notificationService.NotifyAsync(
                document.AssignedLawyerId.Value,
                "Document Workflow Override",
                $"Document '{document.Title}' has been moved from {previousStage} to {targetStage} by admin.",
                "AdminOverride",
                documentId,
                $"/Document/Details/{documentId}");
        }

        // Audit log
        await _auditLogService.LogAsync(
            "AdminOverrideWorkflow",
            "Document",
            documentId,
            $"Admin overrode workflow: {document.Title} moved from {previousStage} to {targetStage}" +
            (metadataChanged ? " (metadata updated)" : ""),
            null,
            System.Text.Json.JsonSerializer.Serialize(new { previousStage, targetStage, remarks, metadataChanged }),
            "WorkflowOverride");

        return review;
    }
}
