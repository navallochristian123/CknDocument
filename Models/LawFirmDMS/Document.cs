using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// Document entity - Core document management
/// Table: Document (LawFirmDMS database)
/// </summary>
[Table("Document")]
public class Document : BaseEntity, IAuditableEntity
{
    [Key]
    public int DocumentID { get; set; }

    [Required]
    public int FirmID { get; set; }

    [MaxLength(255)]
    public string? Title { get; set; }

    public string? Description { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; }

    public int? UploadedBy { get; set; }

    public int? FolderId { get; set; }

    [MaxLength(100)]
    public string? DocumentType { get; set; }

    [MaxLength(50)]
    public string? WorkflowStage { get; set; } = "ClientUpload";

    public string? CurrentRemarks { get; set; }

    public int? AssignedStaffId { get; set; }

    public int? AssignedAdminId { get; set; }

    [MaxLength(500)]
    public string? OriginalFileName { get; set; }

    [MaxLength(20)]
    public string? FileExtension { get; set; }

    [MaxLength(100)]
    public string? MimeType { get; set; }

    public long? TotalFileSize { get; set; }

    public int? CurrentVersion { get; set; } = 1;

    public bool? IsAIProcessed { get; set; } = false;

    public bool? IsDuplicate { get; set; } = false;

    public int? DuplicateOfDocumentId { get; set; }

    public DateTime? StaffReviewedAt { get; set; }

    public DateTime? AdminReviewedAt { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public DateTime? RetentionExpiryDate { get; set; }

    // Navigation properties
    [ForeignKey("FirmID")]
    public virtual Firm? Firm { get; set; }

    [ForeignKey("UploadedBy")]
    public virtual User? Uploader { get; set; }

    [ForeignKey("FolderId")]
    public virtual ClientFolder? Folder { get; set; }

    [ForeignKey("AssignedStaffId")]
    public virtual User? AssignedStaff { get; set; }

    [ForeignKey("AssignedAdminId")]
    public virtual User? AssignedAdmin { get; set; }

    [ForeignKey("DuplicateOfDocumentId")]
    public virtual Document? DuplicateOfDocument { get; set; }

    public virtual ICollection<DocumentVersion> Versions { get; set; } = new List<DocumentVersion>();
    public virtual ICollection<DocumentReview> Reviews { get; set; } = new List<DocumentReview>();
    public virtual ICollection<DocumentAccess> Accesses { get; set; } = new List<DocumentAccess>();
    public virtual ICollection<DocumentRetention> Retentions { get; set; } = new List<DocumentRetention>();
    public virtual ICollection<DocumentSignature> Signatures { get; set; } = new List<DocumentSignature>();
    public virtual ICollection<Archive> Archives { get; set; } = new List<Archive>();
    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    // IAuditableEntity implementation
    [MaxLength(100)]
    public string? CreatedBy { get; set; }

    [MaxLength(100)]
    public string? UpdatedBy { get; set; }
}
