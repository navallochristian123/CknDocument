using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// DocumentSignature entity - Digital signature tracking
/// Table: DocumentSignature (LawFirmDMS database)
/// </summary>
[Table("DocumentSignature")]
public class DocumentSignature : BaseEntity
{
    [Key]
    public int SignatureId { get; set; }

    [Required]
    public int DocumentId { get; set; }

    [Required]
    public int VersionId { get; set; }

    [Required]
    [MaxLength(128)]
    public string FileHash { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? ContentHash { get; set; }

    [MaxLength(255)]
    public string? SignerName { get; set; }

    [MaxLength(128)]
    public string? SignatureHash { get; set; }

    public bool? HasDigitalSignature { get; set; } = false;

    public bool? IsVerified { get; set; } = false;

    // Navigation properties
    [ForeignKey("DocumentId")]
    public virtual Document? Document { get; set; }

    [ForeignKey("VersionId")]
    public virtual DocumentVersion? Version { get; set; }
}
