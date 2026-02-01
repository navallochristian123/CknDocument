using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CKNDocument.Models.OwnerERP;

/// <summary>
/// Expense entity - Platform operating expenses
/// Table: Expense (OwnerERP database)
/// </summary>
[Table("Expense")]
public class Expense
{
    [Key]
    public int ExpenseID { get; set; }

    [MaxLength(255)]
    public string? Description { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal? Amount { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }

    [Column(TypeName = "date")]
    public DateTime? ExpenseDate { get; set; }
}
