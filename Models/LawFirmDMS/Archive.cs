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

    /// <summary>
    /// When the document was archived
    /// </summary>
    public DateTime? ArchivedDate { get; set; }

    [MaxLength(255)]
    public string? Reason { get; set; }

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

    // Navigation properties
    [ForeignKey("DocumentID")]
    public virtual Document? Document { get; set; }

    [ForeignKey("ArchivedBy")]
    public virtual User? ArchivedByUser { get; set; }

    [ForeignKey("RestoredBy")]
    public virtual User? RestoredByUser { get; set; }
}
