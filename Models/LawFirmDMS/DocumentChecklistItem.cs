using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// DocumentChecklistItem entity - Checklist template items
/// Table: DocumentChecklistItem (LawFirmDMS database)
/// </summary>
[Table("DocumentChecklistItem")]
public class DocumentChecklistItem : BaseEntity
{
    [Key]
    public int ChecklistItemId { get; set; }

    [Required]
    public int FirmId { get; set; }

    [Required]
    [MaxLength(255)]
    public string ItemName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool? IsRequired { get; set; } = true;

    public int? DisplayOrder { get; set; } = 0;

    public bool? IsActive { get; set; } = true;

    // Navigation properties
    [ForeignKey("FirmId")]
    public virtual Firm? Firm { get; set; }

    public virtual ICollection<DocumentChecklistResult> Results { get; set; } = new List<DocumentChecklistResult>();
}
