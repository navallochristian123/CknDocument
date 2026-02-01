using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// Role entity - User roles (Admin, Staff, Client, Auditor)
/// Table: Role (LawFirmDMS database)
/// </summary>
[Table("Role")]
public class Role
{
    [Key]
    public int RoleID { get; set; }

    [MaxLength(50)]
    public string? RoleName { get; set; }

    [MaxLength(255)]
    public string? Description { get; set; }

    // Navigation properties
    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
