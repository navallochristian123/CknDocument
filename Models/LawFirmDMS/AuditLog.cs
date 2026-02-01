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

    [MaxLength(100)]
    public string? Action { get; set; }

    [MaxLength(100)]
    public string? EntityType { get; set; }

    public int? EntityID { get; set; }

    public DateTime? Timestamp { get; set; }

    [MaxLength(50)]
    public string? IPAddress { get; set; }

    // Navigation properties
    [ForeignKey("UserID")]
    public virtual User? User { get; set; }
}
