using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// SuperAdmin entity - Platform owner/administrator
/// Table: SuperAdmin (LawFirmDMS database - merged)
/// </summary>
[Table("SuperAdmin")]
public class SuperAdmin : BaseEntity
{
    [Key]
    public int SuperAdminId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    [MaxLength(11)]
    public string? PhoneNumber { get; set; }

    [MaxLength(20)]
    public string? Status { get; set; } = "Active";

    public DateTime? LastLoginAt { get; set; }

    // Computed property
    [NotMapped]
    public string FullName => $"{FirstName} {LastName}".Trim();
}
