using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.OwnerERP;

/// <summary>
/// Invoice entity - Billing invoices for law firms
/// Table: Invoice (OwnerERP database)
/// </summary>
[Table("Invoice")]
public class Invoice : BaseEntity
{
    [Key]
    public int InvoiceID { get; set; }

    public int? ClientID { get; set; }

    [Column(TypeName = "date")]
    public DateTime? InvoiceDate { get; set; }

    [Column(TypeName = "date")]
    public DateTime? DueDate { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal? TotalAmount { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; }

    // Navigation properties
    [ForeignKey("ClientID")]
    public virtual Client? Client { get; set; }

    public virtual ICollection<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();
    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
