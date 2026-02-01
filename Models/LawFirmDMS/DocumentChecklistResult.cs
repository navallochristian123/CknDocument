using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// DocumentChecklistResult entity - Review checklist results
/// Table: DocumentChecklistResult (LawFirmDMS database)
/// </summary>
[Table("DocumentChecklistResult")]
public class DocumentChecklistResult
{
    [Key]
    public int ResultId { get; set; }

    [Required]
    public int ReviewId { get; set; }

    [Required]
    public int ChecklistItemId { get; set; }

    public bool? IsPassed { get; set; } = false;

    [MaxLength(500)]
    public string? Remarks { get; set; }

    public DateTime? CheckedAt { get; set; }

    // Navigation properties
    [ForeignKey("ReviewId")]
    public virtual DocumentReview? Review { get; set; }

    [ForeignKey("ChecklistItemId")]
    public virtual DocumentChecklistItem? ChecklistItem { get; set; }
}
