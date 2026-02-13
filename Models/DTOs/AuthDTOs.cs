using System.ComponentModel.DataAnnotations;

namespace CKNDocument.Models.DTOs;

#region Login DTOs

/// <summary>
/// Login request DTO - supports email or username
/// </summary>
public class LoginRequestDto
{
    [Required(ErrorMessage = "Email or Username is required")]
    [Display(Name = "Email or Username")]
    public string EmailOrUsername { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; } = false;
}

/// <summary>
/// Login response DTO
/// </summary>
public class LoginResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? RedirectUrl { get; set; }
    public UserInfoDto? User { get; set; }
}

/// <summary>
/// User info returned after login
/// </summary>
public class UserInfoDto
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? ProfilePicture { get; set; }
}

#endregion

#region Registration DTOs

/// <summary>
/// Client registration request DTO
/// </summary>
public class ClientRegisterRequestDto
{
    [Required(ErrorMessage = "First name is required")]
    [StringLength(100, ErrorMessage = "First name cannot exceed 100 characters")]
    [Display(Name = "First Name")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Middle name is required")]
    [StringLength(100, ErrorMessage = "Middle name cannot exceed 100 characters")]
    [Display(Name = "Middle Name")]
    public string MiddleName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required")]
    [StringLength(100, ErrorMessage = "Last name cannot exceed 100 characters")]
    [Display(Name = "Last Name")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    [StringLength(100, ErrorMessage = "Email cannot exceed 100 characters")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Username is required")]
    [StringLength(100, MinimumLength = 4, ErrorMessage = "Username must be between 4 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Username can only contain letters, numbers, and underscores")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 12, ErrorMessage = "Password must be at least 12 characters")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please confirm your password")]
    [Compare("Password", ErrorMessage = "Passwords do not match")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required")]
    [StringLength(11, MinimumLength = 10, ErrorMessage = "Phone number must be 10-11 digits")]
    [RegularExpression(@"^[0-9]+$", ErrorMessage = "Phone number can only contain digits")]
    [Display(Name = "Phone Number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Date of birth is required")]
    [DataType(DataType.Date)]
    [Display(Name = "Date of Birth")]
    public DateTime DateOfBirth { get; set; }

    [Required(ErrorMessage = "Street address is required")]
    [StringLength(255, ErrorMessage = "Street address cannot exceed 255 characters")]
    [Display(Name = "Street Address")]
    public string Street { get; set; } = string.Empty;

    [Required(ErrorMessage = "City is required")]
    [StringLength(100, ErrorMessage = "City cannot exceed 100 characters")]
    public string City { get; set; } = string.Empty;

    [Required(ErrorMessage = "Province is required")]
    [StringLength(100, ErrorMessage = "Province cannot exceed 100 characters")]
    public string Province { get; set; } = string.Empty;

    [StringLength(10, ErrorMessage = "Zip code cannot exceed 10 characters")]
    [Display(Name = "Zip Code")]
    public string? ZipCode { get; set; }

    [Required(ErrorMessage = "Please select a law firm")]
    [Display(Name = "Law Firm")]
    public int FirmId { get; set; }

    [Required(ErrorMessage = "Firm verification code is required")]
    [StringLength(20, ErrorMessage = "Firm code cannot exceed 20 characters")]
    [Display(Name = "Firm Verification Code")]
    public string FirmCode { get; set; } = string.Empty;

    // New fields for pending verification
    [StringLength(200, ErrorMessage = "Company/Organization name cannot exceed 200 characters")]
    [Display(Name = "Company/Organization")]
    public string? CompanyName { get; set; }

    [Required(ErrorMessage = "Please provide a reason for registration")]
    [StringLength(1000, ErrorMessage = "Purpose cannot exceed 1000 characters")]
    [Display(Name = "Purpose/Reason for Account")]
    public string Purpose { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "Barangay cannot exceed 100 characters")]
    [Display(Name = "Barangay")]
    public string? Barangay { get; set; }
}

/// <summary>
/// Registration response DTO
/// </summary>
public class RegisterResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
}

#endregion

#region Staff/Admin Creation DTOs (for Admin use)

/// <summary>
/// Create staff/auditor request DTO (Admin creates these)
/// </summary>
public class CreateStaffRequestDto
{
    [Required(ErrorMessage = "First name is required")]
    [StringLength(100)]
    [Display(Name = "First Name")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Middle name is required")]
    [StringLength(100)]
    [Display(Name = "Middle Name")]
    public string MiddleName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required")]
    [StringLength(100)]
    [Display(Name = "Last Name")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Username is required")]
    [StringLength(100, MinimumLength = 4)]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 12)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required")]
    [StringLength(11)]
    [Display(Name = "Phone Number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Date of Birth")]
    public DateTime DateOfBirth { get; set; }

    [Required]
    public string Street { get; set; } = string.Empty;

    [Required]
    public string City { get; set; } = string.Empty;

    [Required]
    public string Province { get; set; } = string.Empty;

    public string? ZipCode { get; set; }

    [Required(ErrorMessage = "Role is required")]
    public string Role { get; set; } = string.Empty; // "Staff" or "Auditor"

    // Staff-specific fields
    [StringLength(50)]
    [Display(Name = "Bar Number")]
    public string? BarNumber { get; set; }

    [StringLength(50)]
    [Display(Name = "License Number")]
    public string? LicenseNumber { get; set; }

    [StringLength(100)]
    public string? Department { get; set; }

    [StringLength(100)]
    public string? Position { get; set; }
}

#endregion

#region Password Change DTOs

/// <summary>
/// Change password request DTO
/// </summary>
public class ChangePasswordDto
{
    [Required(ErrorMessage = "Current password is required")]
    [DataType(DataType.Password)]
    [Display(Name = "Current Password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "New password is required")]
    [StringLength(100, MinimumLength = 12, ErrorMessage = "Password must be at least 12 characters")]
    [DataType(DataType.Password)]
    [Display(Name = "New Password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please confirm your new password")]
    [Compare("NewPassword", ErrorMessage = "Passwords do not match")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm New Password")]
    public string ConfirmNewPassword { get; set; } = string.Empty;
}

#endregion

#region Firm DTOs

/// <summary>
/// Law firm dropdown item
/// </summary>
public class FirmDropdownDto
{
    public int FirmId { get; set; }
    public string FirmName { get; set; } = string.Empty;
}

#endregion
