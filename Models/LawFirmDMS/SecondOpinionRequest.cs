using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// Tracks 2nd opinion requests between lawyers for high-risk documents.
/// Each record represents one assignment from a 1st lawyer to a 2nd lawyer.
/// Supports bouncing back and forth until resolved.
/// </summary>
[Table("SecondOpinionRequest")]
public class SecondOpinionRequest : BaseEntity
{
    [Key]
    public int RequestId { get; set; }

    [Required]
    public int DocumentId { get; set; }

    [Required]
    public int FirmId { get; set; }

    /// <summary>
    /// The lawyer who requested the 2nd opinion (1st lawyer)
    /// </summary>
    [Required]
    public int RequestedByLawyerId { get; set; }

    /// <summary>
    /// The lawyer assigned to give the 2nd opinion
    /// </summary>
    [Required]
    public int AssignedToLawyerId { get; set; }

    /// <summary>
    /// Remarks from the requesting lawyer explaining why 2nd opinion is needed
    /// </summary>
    public string? RequestRemarks { get; set; }

    /// <summary>
    /// Response remarks from the 2nd lawyer (approval or return reason)
    /// </summary>
    public string? ResponseRemarks { get; set; }

    /// <summary>
    /// Status: Pending, Approved, Returned
    /// </summary>
    [MaxLength(50)]
    public string Status { get; set; } = "Pending";

    public DateTime? RespondedAt { get; set; }

    // Navigation properties
    [ForeignKey("DocumentId")]
    public virtual Document? Document { get; set; }

    [ForeignKey("FirmId")]
    public virtual Firm? Firm { get; set; }

    [ForeignKey("RequestedByLawyerId")]
    public virtual User? RequestedByLawyer { get; set; }

    [ForeignKey("AssignedToLawyerId")]
    public virtual User? AssignedToLawyer { get; set; }
}
