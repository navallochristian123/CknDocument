using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// Archive entity - Archived documents
/// Table: Archive (LawFirmDMS database)
/// </summary>
[Table("Archive")]
public class Archive
{
    [Key]
    public int ArchiveID { get; set; }

    public int? DocumentID { get; set; }

    public int? FirmId { get; set; }

    /// <summary>
    /// When the document was archived
    /// </summary>
    public DateTime? ArchivedDate { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }

    /// <summary>
    /// Archive type: Manual, Retention, Rejected, Version, AutoExpired
    /// </summary>
    [MaxLength(50)]
    public string? ArchiveType { get; set; }

    public DateTime? OriginalRetentionDate { get; set; }

    /// <summary>
    /// Version number if this is a version archive
    /// </summary>
    public int? VersionNumber { get; set; }

    public int? ArchivedBy { get; set; }

    public bool? IsRestored { get; set; } = false;

    public DateTime? RestoredAt { get; set; }

    public int? RestoredBy { get; set; }

    /// <summary>
    /// Original status before archiving (Approved, Completed, Rejected)
    /// </summary>
    [MaxLength(50)]
    public string? OriginalStatus { get; set; }

    /// <summary>
    /// Original workflow stage before archiving
    /// </summary>
    [MaxLength(50)]
    public string? OriginalWorkflowStage { get; set; }

    /// <summary>
    /// Original folder ID for restoration
    /// </summary>
    public int? OriginalFolderId { get; set; }

    /// <summary>
    /// Scheduled date for permanent deletion (for retention documents)
    /// </summary>
    public DateTime? ScheduledDeleteDate { get; set; }

    /// <summary>
    /// Whether this archive has been permanently deleted
    /// </summary>
    public bool? IsDeleted { get; set; } = false;

    public DateTime? DeletedAt { get; set; }

    public int? DeletedBy { get; set; }

    // ===== Post-Retention Workflow Fields =====

    /// <summary>
    /// Whether the document is on legal hold (prevents deletion even after retention expires)
    /// </summary>
    public bool? IsOnHold { get; set; } = false;

    /// <summary>
    /// Date the legal hold was placed
    /// </summary>
    public DateTime? HoldPlacedAt { get; set; }

    /// <summary>
    /// Admin who placed the legal hold
    /// </summary>
    public int? HoldPlacedBy { get; set; }

    /// <summary>
    /// Reason for placing legal hold
    /// </summary>
    [MaxLength(500)]
    public string? HoldReason { get; set; }

    /// <summary>
    /// Date the legal hold was released
    /// </summary>
    public DateTime? HoldReleasedAt { get; set; }

    /// <summary>
    /// Admin who released the legal hold
    /// </summary>
    public int? HoldReleasedBy { get; set; }

    /// <summary>
    /// Post-retention workflow status: PendingReview, OnHold, ApprovedForDeletion, Destroyed
    /// </summary>
    [MaxLength(50)]
    public string? RetentionDispositionStatus { get; set; }

    /// <summary>
    /// When the grace period started (retention expiry date)
    /// </summary>
    public DateTime? GracePeriodStartDate { get; set; }

    /// <summary>
    /// When the grace period ends (30 days after retention expiry)
    /// </summary>
    public DateTime? GracePeriodEndDate { get; set; }

    /// <summary>
    /// Whether destruction certificate was generated
    /// </summary>
    public bool? HasDestructionCertificate { get; set; } = false;

    /// <summary>
    /// Path to the destruction certificate PDF
    /// </summary>
    [MaxLength(500)]
    public string? DestructionCertificatePath { get; set; }

    /// <summary>
    /// Date of destruction
    /// </summary>
    public DateTime? DestroyedAt { get; set; }

    /// <summary>
    /// Whether admin was notified about upcoming retention expiry
    /// </summary>
    public bool? ExpiryNotificationSent { get; set; } = false;

    /// <summary>
    /// Whether admin was notified at retention expiry
    /// </summary>
    public bool? ExpiryNotifiedAt { get; set; } = false;

    public DateTime? CreatedAt { get; set; }

    // Navigation properties
    [ForeignKey("DocumentID")]
    public virtual Document? Document { get; set; }

    [ForeignKey("FirmId")]
    public virtual Firm? Firm { get; set; }

    [ForeignKey("ArchivedBy")]
    public virtual User? ArchivedByUser { get; set; }

    [ForeignKey("RestoredBy")]
    public virtual User? RestoredByUser { get; set; }

    [ForeignKey("DeletedBy")]
    public virtual User? DeletedByUser { get; set; }

    [ForeignKey("HoldPlacedBy")]
    public virtual User? HoldPlacedByUser { get; set; }

    [ForeignKey("HoldReleasedBy")]
    public virtual User? HoldReleasedByUser { get; set; }

    [ForeignKey("OriginalFolderId")]
    public virtual ClientFolder? OriginalFolder { get; set; }
}
