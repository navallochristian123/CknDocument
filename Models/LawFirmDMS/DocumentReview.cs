using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// DocumentReview entity - Review workflow tracking
/// Table: DocumentReview (LawFirmDMS database)
/// </summary>
[Table("DocumentReview")]
public class DocumentReview : BaseEntity
{
    [Key]
    public int ReviewId { get; set; }

    public int? DocumentId { get; set; }

    public int? ReviewedBy { get; set; }

    [MaxLength(50)]
    public string? ReviewStatus { get; set; }

    public string? Remarks { get; set; }

    public DateTime? ReviewedAt { get; set; }

    [MaxLength(50)]
    public string? ReviewerRole { get; set; }

    public string? InternalNotes { get; set; }

    public bool? IsChecklistComplete { get; set; } = false;

    public int? ChecklistScore { get; set; }

    // Navigation properties
    [ForeignKey("DocumentId")]
    public virtual Document? Document { get; set; }

    [ForeignKey("ReviewedBy")]
    public virtual User? Reviewer { get; set; }

    public virtual ICollection<DocumentChecklistResult> ChecklistResults { get; set; } = new List<DocumentChecklistResult>();
}
