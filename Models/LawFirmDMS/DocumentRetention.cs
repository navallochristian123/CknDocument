using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// DocumentRetention entity - Retention policy assignment
/// Table: Document_Retention (LawFirmDMS database)
/// </summary>
[Table("Document_Retention")]
public class DocumentRetention
{
    [Key]
    public int RetentionID { get; set; }

    public int? DocumentID { get; set; }

    public int? PolicyID { get; set; }

    public int? FirmId { get; set; }

    /// <summary>
    /// When retention started (usually when document was approved)
    /// </summary>
    [Column(TypeName = "datetime")]
    public DateTime? RetentionStartDate { get; set; }

    /// <summary>
    /// When retention period expires and document should be archived
    /// </summary>
    [Column(TypeName = "datetime")]
    public DateTime? ExpiryDate { get; set; }

    /// <summary>
    /// Total retention period in years
    /// </summary>
    public int? RetentionYears { get; set; }

    /// <summary>
    /// Total retention period in months
    /// </summary>
    public int? RetentionMonths { get; set; }

    /// <summary>
    /// Total retention period in days
    /// </summary>
    public int? RetentionDays { get; set; }

    public bool? IsArchived { get; set; } = false;

    public bool? IsModified { get; set; } = false;

    [MaxLength(500)]
    public string? ModificationReason { get; set; }

    public int? ModifiedBy { get; set; }

    public DateTime? ModifiedAt { get; set; }

    public int? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    // Navigation properties
    [ForeignKey("DocumentID")]
    public virtual Document? Document { get; set; }

    [ForeignKey("PolicyID")]
    public virtual RetentionPolicy? Policy { get; set; }

    [ForeignKey("FirmId")]
    public virtual Firm? Firm { get; set; }

    [ForeignKey("ModifiedBy")]
    public virtual User? ModifiedByUser { get; set; }

    [ForeignKey("CreatedBy")]
    public virtual User? CreatedByUser { get; set; }
}
