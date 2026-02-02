using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// FirmSubscription entity - Represents a Law Firm subscription in the platform
/// Table: FirmSubscription (LawFirmDMS database - merged from Client in OwnerERP)
/// Note: Each FirmSubscription corresponds to a Firm for billing purposes
/// </summary>
[Table("FirmSubscription")]
public class FirmSubscription : BaseEntity
{
    [Key]
    public int SubscriptionID { get; set; }

    [Required]
    public int FirmID { get; set; }

    [MaxLength(150)]
    public string? SubscriptionName { get; set; }

    [MaxLength(100)]
    public string? ContactEmail { get; set; }

    [MaxLength(255)]
    public string? BillingAddress { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; }

    [MaxLength(50)]
    public string? PlanType { get; set; } // Basic, Standard, Premium

    [Column(TypeName = "date")]
    public DateTime? StartDate { get; set; }

    [Column(TypeName = "date")]
    public DateTime? EndDate { get; set; }

    // Navigation properties
    [ForeignKey("FirmID")]
    public virtual Firm? Firm { get; set; }

    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public virtual ICollection<Revenue> Revenues { get; set; } = new List<Revenue>();
}
