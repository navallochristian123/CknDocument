using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// AuditLog entity - System audit trail
/// Table: Audit_Log (LawFirmDMS database)
/// </summary>
[Table("Audit_Log")]
public class AuditLog
{
    [Key]
    public int AuditID { get; set; }

    public int? UserID { get; set; }

    public int? SuperAdminId { get; set; }

    public int? FirmID { get; set; }

    [Required]
    [MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? EntityType { get; set; }

    public int? EntityID { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(2000)]
    public string? OldValues { get; set; }

    [MaxLength(2000)]
    public string? NewValues { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [MaxLength(50)]
    public string? IPAddress { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    [MaxLength(50)]
    public string? ActionCategory { get; set; } // Authentication, UserManagement, DocumentManagement, SystemConfig

    // Navigation properties
    [ForeignKey("UserID")]
    public virtual User? User { get; set; }

    [ForeignKey("SuperAdminId")]
    public virtual SuperAdmin? SuperAdmin { get; set; }

    [ForeignKey("FirmID")]
    public virtual Firm? Firm { get; set; }
}
