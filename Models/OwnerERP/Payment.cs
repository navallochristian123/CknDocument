using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CKNDocument.Models.OwnerERP;

/// <summary>
/// Payment entity - Payment records from law firms
/// Table: Payment (OwnerERP database)
/// </summary>
[Table("Payment")]
public class Payment
{
    [Key]
    public int PaymentID { get; set; }

    public int? ClientID { get; set; }

    public int? InvoiceID { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal? Amount { get; set; }

    [MaxLength(50)]
    public string? PaymentMethod { get; set; }

    [Column(TypeName = "date")]
    public DateTime? PaymentDate { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; }

    // Navigation properties
    [ForeignKey("ClientID")]
    public virtual Client? Client { get; set; }

    [ForeignKey("InvoiceID")]
    public virtual Invoice? Invoice { get; set; }
}
