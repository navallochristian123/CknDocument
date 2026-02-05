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

    [ForeignKey("OriginalFolderId")]
    public virtual ClientFolder? OriginalFolder { get; set; }
}
