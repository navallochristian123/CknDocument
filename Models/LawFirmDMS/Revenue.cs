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

    public int? PaymentID { get; set; }

    [MaxLength(100)]
    public string? Source { get; set; } // Subscription, Setup, Custom

    [Column(TypeName = "decimal(12,2)")]
    public decimal? GrossAmount { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal? TaxAmount { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal? NetAmount { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal? TaxRate { get; set; } // e.g. 12.00 for 12% VAT

    [Column(TypeName = "decimal(12,2)")]
    public decimal? Amount { get; set; }

    [Column(TypeName = "date")]
    public DateTime? RevenueDate { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(50)]
    public string? Category { get; set; } // Monthly, Annual, Setup, Upgrade

    // Navigation properties
    [ForeignKey("SubscriptionID")]
    public virtual FirmSubscription? Subscription { get; set; }

    [ForeignKey("PaymentID")]
    public virtual Payment? Payment { get; set; }
}
