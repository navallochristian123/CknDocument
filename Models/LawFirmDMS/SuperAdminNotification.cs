using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// SuperAdminNotification entity - Notifications for SuperAdmin activities only
/// Table: SuperAdminNotification (LawFirmDMS database)
/// </summary>
[Table("SuperAdminNotification")]
public class SuperAdminNotification : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int SuperAdminId { get; set; }

    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(1000)]
    public string Message { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string NotificationType { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? ActionUrl { get; set; }

    [MaxLength(50)]
    public string? Icon { get; set; }

    public bool IsRead { get; set; } = false;

    public DateTime? ReadAt { get; set; }

    // Navigation
    [ForeignKey("SuperAdminId")]
    public virtual SuperAdmin? SuperAdmin { get; set; }
}
