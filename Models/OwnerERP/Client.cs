using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.OwnerERP;

/// <summary>
/// Client entity - Represents a Law Firm subscription in the platform
/// Table: Client (OwnerERP database)
/// Note: Each Client here corresponds to a Firm in LawFirmDMS
/// </summary>
[Table("Client")]
public class Client : BaseEntity
{
    [Key]
    public int ClientID { get; set; }

    [Required]
    public int FirmID { get; set; }

    [MaxLength(150)]
    public string? ClientName { get; set; }

    [MaxLength(100)]
    public string? ContactEmail { get; set; }

    [MaxLength(255)]
    public string? Address { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; }

    // Navigation properties
    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public virtual ICollection<Revenue> Revenues { get; set; } = new List<Revenue>();
}
