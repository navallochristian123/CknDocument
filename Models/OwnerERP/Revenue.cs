using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CKNDocument.Models.OwnerERP;

/// <summary>
/// Revenue entity - Revenue tracking for the platform
/// Table: Revenue (OwnerERP database)
/// </summary>
[Table("Revenue")]
public class Revenue
{
    [Key]
    public int RevenueID { get; set; }

    public int? ClientID { get; set; }

    [MaxLength(100)]
    public string? Source { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal? Amount { get; set; }

    [Column(TypeName = "date")]
    public DateTime? RevenueDate { get; set; }

    // Navigation properties
    [ForeignKey("ClientID")]
    public virtual Client? Client { get; set; }
}
