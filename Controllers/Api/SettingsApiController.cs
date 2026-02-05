using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Services;
using System.Security.Claims;

namespace CKNDocument.Controllers.Api;

/// <summary>
/// API Controller for User Settings operations
/// Handles profile updates, password changes for all roles
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SettingsApiController : ControllerBase
{
    private readonly LawFirmDMSDbContext _context;
    private readonly AuditLogService _auditLogService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<SettingsApiController> _logger;

    public SettingsApiController(
        LawFirmDMSDbContext context,
        AuditLogService auditLogService,
        IWebHostEnvironment environment,
        ILogger<SettingsApiController> logger)
    {
        _context = context;
        _auditLogService = auditLogService;
        _environment = environment;
        _logger = logger;
    }

    private int GetCurrentUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    private int GetFirmId() => int.Parse(User.FindFirst("FirmId")?.Value ?? "0");
    private string GetUserRole() => User.FindFirst(ClaimTypes.Role)?.Value ?? "Client";

    /// <summary>
    /// Get current user's profile
    /// </summary>
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        var userId = GetCurrentUserId();

        var user = await _context.Users
            .Include(u => u.Firm)
            .FirstOrDefaultAsync(u => u.UserID == userId);

        if (user == null)
            return NotFound(new { success = false, message = "User not found" });

        return Ok(new
        {
            success = true,
            profile = new
            {
                id = user.UserID,
                firstName = user.FirstName,
                middleName = user.MiddleName,
                lastName = user.LastName,
                fullName = user.FullName,
                email = user.Email,
                username = user.Username,
                phoneNumber = user.PhoneNumber,
                dateOfBirth = user.DateOfBirth,
                street = user.Street,
                city = user.City,
                province = user.Province,
                zipCode = user.ZipCode,
                profilePicture = user.ProfilePicture,
                firmName = user.Firm?.FirmName,
                role = GetUserRole(),
                lastLoginAt = user.LastLoginAt
            }
        });
    }

    /// <summary>
    /// Update user profile
    /// </summary>
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
    {
        var userId = GetCurrentUserId();

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return NotFound(new { success = false, message = "User not found" });

        // Validate username uniqueness if changed
        if (!string.IsNullOrWhiteSpace(dto.Username) && dto.Username != user.Username)
        {
            var existingUsername = await _context.Users
                .AnyAsync(u => u.Username == dto.Username && u.UserID != userId);
            if (existingUsername)
                return BadRequest(new { success = false, message = "Username is already taken" });
            user.Username = dto.Username.Trim();
        }

        // Validate email uniqueness if changed
        if (!string.IsNullOrWhiteSpace(dto.Email) && dto.Email != user.Email)
        {
            var existingEmail = await _context.Users
                .AnyAsync(u => u.Email == dto.Email && u.UserID != userId);
            if (existingEmail)
                return BadRequest(new { success = false, message = "Email is already registered" });
            user.Email = dto.Email.Trim();
        }

        // Update other fields
        if (!string.IsNullOrWhiteSpace(dto.FirstName))
            user.FirstName = dto.FirstName.Trim();
        
        if (!string.IsNullOrWhiteSpace(dto.MiddleName))
            user.MiddleName = dto.MiddleName.Trim();
        
        if (!string.IsNullOrWhiteSpace(dto.LastName))
            user.LastName = dto.LastName.Trim();
        
        if (!string.IsNullOrWhiteSpace(dto.PhoneNumber))
            user.PhoneNumber = dto.PhoneNumber.Trim();
        
        if (dto.DateOfBirth.HasValue)
            user.DateOfBirth = dto.DateOfBirth;
        
        if (dto.Street != null)
            user.Street = dto.Street.Trim();
        
        if (dto.City != null)
            user.City = dto.City.Trim();
        
        if (dto.Province != null)
            user.Province = dto.Province.Trim();
        
        if (dto.ZipCode != null)
            user.ZipCode = dto.ZipCode.Trim();

        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "ProfileUpdate",
            "User",
            userId,
            $"Profile updated by {user.FullName}",
            null,
            null,
            "AccountManagement");

        return Ok(new { success = true, message = "Profile updated successfully" });
    }

    /// <summary>
    /// Upload profile picture
    /// </summary>
    [HttpPost("profile-picture")]
    public async Task<IActionResult> UploadProfilePicture([FromForm] IFormFile file)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        if (file == null || file.Length == 0)
            return BadRequest(new { success = false, message = "No file provided" });

        // Validate file type
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        var extension = Path.GetExtension(file.FileName).ToLower();
        if (!allowedExtensions.Contains(extension))
            return BadRequest(new { success = false, message = "Invalid file type. Allowed: jpg, jpeg, png, gif, webp" });

        // Validate file size (max 5MB)
        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(new { success = false, message = "File size must be less than 5MB" });

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return NotFound(new { success = false, message = "User not found" });

        try
        {
            // Create profile pictures directory
            var profilePicsPath = Path.Combine(_environment.WebRootPath, "images", "profiles");
            Directory.CreateDirectory(profilePicsPath);

            // Delete old profile picture if exists
            if (!string.IsNullOrEmpty(user.ProfilePicture))
            {
                var oldPath = Path.Combine(_environment.WebRootPath, user.ProfilePicture.TrimStart('/'));
                if (System.IO.File.Exists(oldPath))
                    System.IO.File.Delete(oldPath);
            }

            // Save new file
            var fileName = $"{userId}_{DateTime.UtcNow:yyyyMMddHHmmss}{extension}";
            var filePath = Path.Combine(profilePicsPath, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Update user
            user.ProfilePicture = $"/images/profiles/{fileName}";
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Profile picture updated",
                profilePicture = user.ProfilePicture
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading profile picture for user {UserId}", userId);
            return StatusCode(500, new { success = false, message = "Error uploading profile picture" });
        }
    }

    /// <summary>
    /// Change password
    /// </summary>
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var userId = GetCurrentUserId();

        if (string.IsNullOrWhiteSpace(dto.CurrentPassword))
            return BadRequest(new { success = false, message = "Current password is required" });

        if (string.IsNullOrWhiteSpace(dto.NewPassword))
            return BadRequest(new { success = false, message = "New password is required" });

        if (dto.NewPassword.Length < 12)
            return BadRequest(new { success = false, message = "Password must be at least 12 characters" });

        if (dto.NewPassword != dto.ConfirmPassword)
            return BadRequest(new { success = false, message = "Passwords do not match" });

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return NotFound(new { success = false, message = "User not found" });

        // Verify current password
        if (!PasswordHelper.VerifyPassword(dto.CurrentPassword, user.PasswordHash ?? ""))
            return BadRequest(new { success = false, message = "Current password is incorrect" });

        // Hash and save new password
        user.PasswordHash = PasswordHelper.HashPassword(dto.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "PasswordChange",
            "User",
            userId,
            $"Password changed by {user.FullName}",
            null,
            null,
            "AccountManagement");

        return Ok(new { success = true, message = "Password changed successfully" });
    }

    /// <summary>
    /// Delete profile picture
    /// </summary>
    [HttpDelete("profile-picture")]
    public async Task<IActionResult> DeleteProfilePicture()
    {
        var userId = GetCurrentUserId();

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return NotFound(new { success = false, message = "User not found" });

        if (!string.IsNullOrEmpty(user.ProfilePicture))
        {
            var filePath = Path.Combine(_environment.WebRootPath, user.ProfilePicture.TrimStart('/'));
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);

            user.ProfilePicture = null;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return Ok(new { success = true, message = "Profile picture removed" });
    }
}

// DTOs
public class UpdateProfileDto
{
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Username { get; set; }
    public string? PhoneNumber { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? Province { get; set; }
    public string? ZipCode { get; set; }
}

public class ChangePasswordDto
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}
