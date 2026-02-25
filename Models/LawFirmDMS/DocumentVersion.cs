using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// DocumentVersion entity - Version tracking for documents
/// Table: DocumentVersion (LawFirmDMS database)
/// </summary>
[Table("DocumentVersion")]
public class DocumentVersion : BaseEntity
{
    [Key]
    public int VersionId { get; set; }

    public int? DocumentId { get; set; }

    public int VersionNumber { get; set; } = 1;

    /// <summary>
    /// Human-readable version label: "1" for initial, "1.1"/"1.2" for staff edits, "2" for lawyer uploads.
    /// Falls back to VersionNumber.ToString() when null.
    /// </summary>
    [MaxLength(20)]
    public string? VersionLabel { get; set; }

    [MaxLength(1000)]
    public string? FilePath { get; set; }

    public long? FileSize { get; set; }

    public int? UploadedBy { get; set; }

    [MaxLength(500)]
    public string? OriginalFileName { get; set; }

    [MaxLength(20)]
    public string? FileExtension { get; set; }

    [MaxLength(100)]
    public string? MimeType { get; set; }

    [MaxLength(500)]
    public string? ChangeDescription { get; set; }

    [MaxLength(50)]
    public string? ChangedBy { get; set; }

    [MaxLength(128)]
    public string? FileHash { get; set; }

    public bool? IsCurrentVersion { get; set; } = false;

    // Navigation properties
    [ForeignKey("DocumentId")]
    public virtual Document? Document { get; set; }

    [ForeignKey("UploadedBy")]
    public virtual User? Uploader { get; set; }

    public virtual ICollection<DocumentSignature> Signatures { get; set; } = new List<DocumentSignature>();
}
