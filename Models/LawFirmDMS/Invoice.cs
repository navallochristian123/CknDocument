using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// Invoice entity - Billing invoices for law firms
/// Table: Invoice (LawFirmDMS database - merged)
/// </summary>
[Table("Invoice")]
public class Invoice : BaseEntity
{
    [Key]
    public int InvoiceID { get; set; }

    public int? SubscriptionID { get; set; }

    [MaxLength(50)]
    public string? InvoiceNumber { get; set; }

    [Column(TypeName = "date")]
    public DateTime? InvoiceDate { get; set; }

    [Column(TypeName = "date")]
    public DateTime? DueDate { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal? TotalAmount { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal? PaidAmount { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; } // Pending, Paid, Overdue, Cancelled

    [MaxLength(500)]
    public string? Notes { get; set; }

    // Navigation properties
    [ForeignKey("SubscriptionID")]
    public virtual FirmSubscription? Subscription { get; set; }

    public virtual ICollection<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();
    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
