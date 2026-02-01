using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// Report entity - Generated reports
/// Table: Report (LawFirmDMS database)
/// </summary>
[Table("Report")]
public class Report
{
    [Key]
    public int ReportID { get; set; }

    public int? GeneratedBy { get; set; }

    [MaxLength(100)]
    public string? ReportType { get; set; }

    public DateTime? GeneratedAt { get; set; }

    [MaxLength(255)]
    public string? FilePath { get; set; }

    // Navigation properties
    [ForeignKey("GeneratedBy")]
    public virtual User? Generator { get; set; }
}
