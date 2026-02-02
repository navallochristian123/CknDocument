using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// Firm entity - Law firm tenant
/// Table: Firm (LawFirmDMS database)
/// </summary>
[Table("Firm")]
public class Firm : BaseEntity
{
    [Key]
    public int FirmID { get; set; }

    [Required]
    [MaxLength(150)]
    public string FirmName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? ContactEmail { get; set; }

    [MaxLength(255)]
    public string? Address { get; set; }

    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; }

    [MaxLength(500)]
    public string? LogoUrl { get; set; }

    // Navigation properties
    public virtual ICollection<User> Users { get; set; } = new List<User>();
    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();
    public virtual ICollection<ClientFolder> ClientFolders { get; set; } = new List<ClientFolder>();
    public virtual ICollection<DocumentChecklistItem> ChecklistItems { get; set; } = new List<DocumentChecklistItem>();
    public virtual ICollection<FirmSubscription> Subscriptions { get; set; } = new List<FirmSubscription>();
}
