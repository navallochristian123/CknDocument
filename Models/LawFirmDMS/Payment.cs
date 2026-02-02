using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// Payment entity - Payment records from law firms
/// Table: Payment (LawFirmDMS database - merged)
/// </summary>
[Table("Payment")]
public class Payment : BaseEntity
{
    [Key]
    public int PaymentID { get; set; }

    public int? SubscriptionID { get; set; }

    public int? InvoiceID { get; set; }

    [MaxLength(50)]
    public string? PaymentReference { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal? Amount { get; set; }

    [MaxLength(50)]
    public string? PaymentMethod { get; set; } // Cash, BankTransfer, CreditCard, GCash, Maya

    [Column(TypeName = "date")]
    public DateTime? PaymentDate { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; } // Pending, Completed, Failed, Refunded

    [MaxLength(500)]
    public string? Notes { get; set; }

    // Navigation properties
    [ForeignKey("SubscriptionID")]
    public virtual FirmSubscription? Subscription { get; set; }

    [ForeignKey("InvoiceID")]
    public virtual Invoice? Invoice { get; set; }
}
