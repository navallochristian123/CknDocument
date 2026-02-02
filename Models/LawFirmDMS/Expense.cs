using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// Expense entity - Platform operating expenses
/// Table: Expense (LawFirmDMS database - merged)
/// </summary>
[Table("Expense")]
public class Expense : BaseEntity
{
    [Key]
    public int ExpenseID { get; set; }

    [MaxLength(255)]
    public string? Description { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal? Amount { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; } // Operations, Marketing, Salaries, Hosting, Other

    [Column(TypeName = "date")]
    public DateTime? ExpenseDate { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; } // Pending, Approved, Rejected
}
