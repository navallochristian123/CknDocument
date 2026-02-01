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

    [MaxLength(100)]
    public string? PolicyName { get; set; }

    public int? RetentionMonths { get; set; }

    public int? CreatedBy { get; set; }

    // Navigation properties
    [ForeignKey("CreatedBy")]
    public virtual User? Creator { get; set; }

    public virtual ICollection<DocumentRetention> DocumentRetentions { get; set; } = new List<DocumentRetention>();
}
