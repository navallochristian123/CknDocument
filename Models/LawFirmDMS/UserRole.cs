using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// UserRole entity - Many-to-many relationship between User and Role
/// Table: User_Role (LawFirmDMS database)
/// </summary>
[Table("User_Role")]
public class UserRole
{
    [Key]
    public int UserRoleID { get; set; }

    public int? UserID { get; set; }

    public int? RoleID { get; set; }

    public DateTime? AssignedAt { get; set; }

    // Navigation properties
    [ForeignKey("UserID")]
    public virtual User? User { get; set; }

    [ForeignKey("RoleID")]
    public virtual Role? Role { get; set; }
}
