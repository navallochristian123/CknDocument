using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// User entity - Law firm users (Admin, Staff, Client, Auditor)
/// Table: User (LawFirmDMS database)
/// </summary>
[Table("User")]
public class User : BaseEntity
{
    [Key]
    public int UserID { get; set; }

    [Required]
    public int FirmID { get; set; }

    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    [MaxLength(100)]
    public string? MiddleName { get; set; }

    [MaxLength(100)]
    public string? Email { get; set; }

    [MaxLength(255)]
    public string? PasswordHash { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; }

    [MaxLength(100)]
    public string? Username { get; set; }

    [MaxLength(11)]
    public string? PhoneNumber { get; set; }

    [Column(TypeName = "date")]
    public DateTime? DateOfBirth { get; set; }

    [MaxLength(255)]
    public string? Street { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? Province { get; set; }

    [MaxLength(10)]
    public string? ZipCode { get; set; }

    [MaxLength(500)]
    public string? ProfilePicture { get; set; }

    [MaxLength(50)]
    public string? BarNumber { get; set; }

    [MaxLength(50)]
    public string? LicenseNumber { get; set; }

    [MaxLength(100)]
    public string? Department { get; set; }

    [MaxLength(100)]
    public string? Position { get; set; }

    /// <summary>
    /// Company or Organization name for clients
    /// </summary>
    [MaxLength(200)]
    public string? CompanyName { get; set; }

    /// <summary>
    /// Purpose/reason for document management account (for pending verification)
    /// </summary>
    [MaxLength(1000)]
    public string? Purpose { get; set; }

    /// <summary>
    /// Barangay for Philippine addresses
    /// </summary>
    [MaxLength(100)]
    public string? Barangay { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public int? FailedLoginAttempts { get; set; } = 0;

    public DateTime? LockoutEnd { get; set; }

    public bool? EmailConfirmed { get; set; } = false;

    /// <summary>
    /// Path to the user's signature image (for AI signature verification)
    /// </summary>
    [MaxLength(500)]
    public string? SignaturePath { get; set; }

    /// <summary>
    /// User's full name as it appears on their signature (for AI matching)
    /// </summary>
    [MaxLength(200)]
    public string? SignatureName { get; set; }

    // Computed property
    [NotMapped]
    public string FullName => $"{FirstName} {MiddleName} {LastName}".Trim();

    // Navigation properties
    [ForeignKey("FirmID")]
    public virtual Firm? Firm { get; set; }

    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public virtual ICollection<Document> UploadedDocuments { get; set; } = new List<Document>();
    public virtual ICollection<Document> AssignedStaffDocuments { get; set; } = new List<Document>();
    public virtual ICollection<Document> AssignedAdminDocuments { get; set; } = new List<Document>();
    public virtual ICollection<DocumentAccess> DocumentAccesses { get; set; } = new List<DocumentAccess>();
    public virtual ICollection<DocumentReview> DocumentReviews { get; set; } = new List<DocumentReview>();
    public virtual ICollection<DocumentVersion> DocumentVersions { get; set; } = new List<DocumentVersion>();
    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public virtual ICollection<Report> Reports { get; set; } = new List<Report>();
    public virtual ICollection<RetentionPolicy> RetentionPolicies { get; set; } = new List<RetentionPolicy>();
    public virtual ICollection<ClientFolder> ClientFolders { get; set; } = new List<ClientFolder>();
}
