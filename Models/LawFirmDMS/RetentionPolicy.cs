using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// RetentionPolicy entity - Document retention rules
/// Table: Retention_Policy (LawFirmDMS database)
/// </summary>
[Table("Retention_Policy")]
public class RetentionPolicy
{
    [Key]
    public int PolicyID { get; set; }

    public int? FirmId { get; set; }

    [MaxLength(100)]
    public string? PolicyName { get; set; }

    [MaxLength(100)]
    public string? DocumentType { get; set; }

    public int? RetentionYears { get; set; }

    public int? RetentionMonths { get; set; }

    public int? RetentionDays { get; set; }

    public bool? IsDefault { get; set; } = false;

    public bool? IsActive { get; set; } = true;

    [MaxLength(500)]
    public string? Description { get; set; }

    public int? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    [ForeignKey("FirmId")]
    public virtual Firm? Firm { get; set; }

    [ForeignKey("CreatedBy")]
    public virtual User? Creator { get; set; }

    public virtual ICollection<DocumentRetention> DocumentRetentions { get; set; } = new List<DocumentRetention>();
}
