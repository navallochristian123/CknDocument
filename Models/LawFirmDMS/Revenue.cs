using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// Revenue entity - Revenue tracking for the platform
/// Table: Revenue (LawFirmDMS database - merged)
/// </summary>
[Table("Revenue")]
public class Revenue : BaseEntity
{
    [Key]
    public int RevenueID { get; set; }

    public int? SubscriptionID { get; set; }

    [MaxLength(100)]
    public string? Source { get; set; } // Subscription, Setup, Custom

    [Column(TypeName = "decimal(12,2)")]
    public decimal? Amount { get; set; }

    [Column(TypeName = "date")]
    public DateTime? RevenueDate { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    // Navigation properties
    [ForeignKey("SubscriptionID")]
    public virtual FirmSubscription? Subscription { get; set; }
}
