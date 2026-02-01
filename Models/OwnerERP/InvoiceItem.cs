using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CKNDocument.Models.OwnerERP;

/// <summary>
/// Invoice Item entity - Line items for invoices
/// Table: Invoice_Item (OwnerERP database)
/// </summary>
[Table("Invoice_Item")]
public class InvoiceItem
{
    [Key]
    public int ItemID { get; set; }

    public int? InvoiceID { get; set; }

    [MaxLength(255)]
    public string? Description { get; set; }

    public int? Quantity { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal? UnitPrice { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal? SubTotal { get; set; }

    // Navigation properties
    [ForeignKey("InvoiceID")]
    public virtual Invoice? Invoice { get; set; }
}
