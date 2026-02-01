using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// Notification entity - User notifications
/// Table: Notification (LawFirmDMS database)
/// </summary>
[Table("Notification")]
public class Notification : BaseEntity
{
    [Key]
    public int NotificationId { get; set; }

    [Required]
    public int UserId { get; set; }

    public int? DocumentId { get; set; }

    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Message { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string NotificationType { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? ActionUrl { get; set; }

    public bool? IsRead { get; set; } = false;

    public DateTime? ReadAt { get; set; }

    // Navigation properties
    [ForeignKey("UserId")]
    public virtual User? User { get; set; }

    [ForeignKey("DocumentId")]
    public virtual Document? Document { get; set; }
}
