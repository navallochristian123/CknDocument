using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;
using CKNDocument.Services;
using System.Security.Claims;
using System.ComponentModel.DataAnnotations;

namespace CKNDocument.Controllers.LawFirm;

/// <summary>
/// User management controller for Law Firm Admin
/// Manages staff, clients, and auditors
/// </summary>
[Authorize(Policy = "AdminOnly")]
public class UserController : Controller
{
    private readonly LawFirmDMSDbContext _context;
    private readonly AuditLogService _auditLogService;
    private readonly ILogger<UserController> _logger;

    public UserController(
        LawFirmDMSDbContext context,
        AuditLogService auditLogService,
        ILogger<UserController> logger)
    {
        _context = context;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out int userId) ? userId : 0;
    }

    private int GetCurrentFirmId()
    {
        var firmIdClaim = User.FindFirst("FirmId")?.Value;
        return int.TryParse(firmIdClaim, out int firmId) ? firmId : 0;
    }

    private string GetRoleViewPath(string viewName)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Admin";
        return $"~/Views/{role}/{viewName}.cshtml";
    }

    #region Views

    /// <summary>
    /// Display all users
    /// </summary>
    public async Task<IActionResult> Index()
    {
        var firmId = GetCurrentFirmId();
        var users = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Where(u => u.FirmID == firmId)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

        ViewData["Roles"] = await _context.Roles.ToListAsync();
        return View(GetRoleViewPath("Users"), users);
    }

    /// <summary>
    /// Display staff only
    /// </summary>
    public async Task<IActionResult> Staff()
    {
        var firmId = GetCurrentFirmId();
        var staff = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Where(u => u.FirmID == firmId && u.UserRoles.Any(ur => ur.Role!.RoleName == "Staff"))
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

        return View(GetRoleViewPath("Staff"), staff);
    }

    /// <summary>
    /// Display clients only
    /// </summary>
    public async Task<IActionResult> Clients()
    {
        var firmId = GetCurrentFirmId();
        var clients = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Where(u => u.FirmID == firmId && u.UserRoles.Any(ur => ur.Role!.RoleName == "Client"))
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

        return View(GetRoleViewPath("ClientManagement"), clients);
    }

    /// <summary>
    /// Display auditors only
    /// </summary>
    public async Task<IActionResult> Auditors()
    {
        var firmId = GetCurrentFirmId();
        var auditors = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Where(u => u.FirmID == firmId && u.UserRoles.Any(ur => ur.Role!.RoleName == "Auditor"))
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

        return View(GetRoleViewPath("Auditors"), auditors);
    }

    /// <summary>
    /// Create user form - redirects to Index since we use modal
    /// </summary>
    public IActionResult Create(string? type = null)
    {
        // Modal is in Users.cshtml, redirect to Index
        return RedirectToAction("Index");
    }

    /// <summary>
    /// Get user for editing (JSON for modal)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var firmId = GetCurrentFirmId();
        var user = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.UserID == id && u.FirmID == firmId);

        if (user == null)
        {
            return Json(new { success = false, message = "User not found." });
        }

        var roles = await _context.Roles
            .Where(r => r.RoleName != "Admin" && r.RoleName != "SuperAdmin")
            .ToListAsync();

        return Json(new
        {
            success = true,
            data = new
            {
                user.UserID,
                user.FirstName,
                user.MiddleName,
                user.LastName,
                user.Email,
                user.Username,
                user.PhoneNumber,
                DateOfBirth = user.DateOfBirth?.ToString("yyyy-MM-dd"),
                user.Street,
                user.City,
                user.Province,
                user.ZipCode,
                user.Department,
                user.Position,
                user.BarNumber,
                user.LicenseNumber,
                user.Status,
                RoleId = user.UserRoles.FirstOrDefault()?.RoleID ?? 0,
                RoleName = user.UserRoles.FirstOrDefault()?.Role?.RoleName
            },
            roles = roles.Select(r => new { r.RoleID, r.RoleName })
        });
    }

    /// <summary>
    /// Get user details (JSON for modal)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var firmId = GetCurrentFirmId();
        var user = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.UserID == id && u.FirmID == firmId);

        if (user == null)
        {
            return Json(new { success = false, message = "User not found." });
        }

        return Json(new
        {
            success = true,
            data = new
            {
                user.UserID,
                user.FirstName,
                user.MiddleName,
                user.LastName,
                FullName = user.FullName,
                user.Email,
                user.Username,
                user.PhoneNumber,
                DateOfBirth = user.DateOfBirth?.ToString("MMM dd, yyyy"),
                user.Street,
                user.City,
                user.Province,
                user.ZipCode,
                user.Department,
                user.Position,
                user.BarNumber,
                user.LicenseNumber,
                user.Status,
                user.EmailConfirmed,
                CreatedAt = user.CreatedAt?.ToString("MMM dd, yyyy HH:mm"),
                LastLoginAt = user.LastLoginAt?.ToString("MMM dd, yyyy HH:mm"),
                RoleName = user.UserRoles.FirstOrDefault()?.Role?.RoleName ?? "No Role"
            }
        });
    }

    #endregion

    #region API Endpoints

    /// <summary>
    /// API: Create new user
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromForm] CreateUserDto request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Please fill in all required fields correctly.";
                return RedirectToAction("Index");
            }

            var firmId = GetCurrentFirmId();
            var currentUserId = GetCurrentUserId();

            // Check if email already exists
            if (await _context.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == request.Email.ToLower()))
            {
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Email already registered.";
                return RedirectToAction("Index");
            }

            // Check if username already exists
            if (await _context.Users.AnyAsync(u => u.Username != null && u.Username.ToLower() == request.Username.ToLower()))
            {
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Username already taken.";
                return RedirectToAction("Index");
            }

            // Validate password
            var passwordValidation = PasswordHelper.ValidatePassword(request.Password);
            if (!passwordValidation.IsValid)
            {
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Password does not meet requirements.";
                return RedirectToAction("Index");
            }

            // Get the role - prevent creating Admin or SuperAdmin
            var role = await _context.Roles.FindAsync(request.RoleId);
            if (role == null || role.RoleName == "Admin" || role.RoleName == "SuperAdmin")
            {
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Invalid role selected.";
                return RedirectToAction("Index");
            }

            // Create user
            var user = new User
            {
                FirmID = firmId,
                FirstName = request.FirstName.Trim(),
                MiddleName = request.MiddleName?.Trim(),
                LastName = request.LastName.Trim(),
                Email = request.Email.Trim().ToLower(),
                Username = request.Username.Trim().ToLower(),
                PasswordHash = PasswordHelper.HashPassword(request.Password),
                PhoneNumber = request.PhoneNumber?.Trim(),
                DateOfBirth = request.DateOfBirth,
                Street = request.Street?.Trim(),
                City = request.City?.Trim(),
                Province = request.Province?.Trim(),
                ZipCode = request.ZipCode?.Trim(),
                Department = request.Department?.Trim(),
                Position = request.Position?.Trim(),
                BarNumber = request.BarNumber?.Trim(),
                LicenseNumber = request.LicenseNumber?.Trim(),
                Status = "Active",
                EmailConfirmed = true, // Admin-created users are auto-confirmed
                FailedLoginAttempts = 0,
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Assign role
            var userRole = new UserRole
            {
                UserID = user.UserID,
                RoleID = request.RoleId,
                AssignedAt = DateTime.UtcNow
            };
            _context.UserRoles.Add(userRole);
            await _context.SaveChangesAsync();

            // Log account creation
            await _auditLogService.LogAccountCreatedAsync(user.UserID, user.Email, role.RoleName ?? "Unknown", currentUserId, firmId);

            _logger.LogInformation("User {Email} created with role {Role} by admin {AdminId}", user.Email, role.RoleName, currentUserId);

            TempData["ToastType"] = "success";
            TempData["ToastMessage"] = $"User {user.FullName} created successfully.";

            return RedirectToAction("Index");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user: {Message}", ex.Message);
            TempData["ToastType"] = "error";
            TempData["ToastMessage"] = "An error occurred while creating the user.";
            return RedirectToAction("Index");
        }
    }

    /// <summary>
    /// API: Update user
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [FromForm] UpdateUserDto request)
    {
        try
        {
            var firmId = GetCurrentFirmId();
            var currentUserId = GetCurrentUserId();

            var user = await _context.Users
                .Include(u => u.UserRoles)
                .FirstOrDefaultAsync(u => u.UserID == id && u.FirmID == firmId);

            if (user == null)
            {
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "User not found.";
                return RedirectToAction("Index");
            }

            // Check if email already exists (for other users)
            if (await _context.Users.AnyAsync(u => u.UserID != id && u.Email != null && u.Email.ToLower() == request.Email.ToLower()))
            {
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Email already registered by another user.";
                return RedirectToAction("Index");
            }

            // Track changes for audit
            var oldEmail = user.Email;
            var oldStatus = user.Status;

            // Update user
            user.FirstName = request.FirstName.Trim();
            user.MiddleName = request.MiddleName?.Trim();
            user.LastName = request.LastName.Trim();
            user.Email = request.Email.Trim().ToLower();
            user.PhoneNumber = request.PhoneNumber?.Trim();
            user.DateOfBirth = request.DateOfBirth;
            user.Street = request.Street?.Trim();
            user.City = request.City?.Trim();
            user.Province = request.Province?.Trim();
            user.ZipCode = request.ZipCode?.Trim();
            user.Department = request.Department?.Trim();
            user.Position = request.Position?.Trim();
            user.BarNumber = request.BarNumber?.Trim();
            user.LicenseNumber = request.LicenseNumber?.Trim();
            user.Status = request.Status;
            user.UpdatedAt = DateTime.UtcNow;

            // Update role if changed
            if (request.RoleId > 0)
            {
                var currentRole = user.UserRoles.FirstOrDefault();
                if (currentRole?.RoleID != request.RoleId)
                {
                    // Remove old role
                    if (currentRole != null)
                    {
                        _context.UserRoles.Remove(currentRole);
                    }

                    // Add new role
                    var newRole = await _context.Roles.FindAsync(request.RoleId);
                    if (newRole != null)
                    {
                        _context.UserRoles.Add(new UserRole
                        {
                            UserID = user.UserID,
                            RoleID = request.RoleId,
                            AssignedAt = DateTime.UtcNow
                        });

                        await _auditLogService.LogRoleAssignedAsync(user.UserID, user.Email ?? "", newRole.RoleName ?? "Unknown", currentUserId);
                    }
                }
            }

            // Update password if provided
            if (!string.IsNullOrEmpty(request.NewPassword))
            {
                var passwordValidation = PasswordHelper.ValidatePassword(request.NewPassword);
                if (!passwordValidation.IsValid)
                {
                    TempData["ToastType"] = "error";
                    TempData["ToastMessage"] = "Password does not meet requirements.";
                    return RedirectToAction("Index");
                }

                user.PasswordHash = PasswordHelper.HashPassword(request.NewPassword);
                await _auditLogService.LogPasswordChangedAsync(user.UserID, user.Email ?? "");
            }

            await _context.SaveChangesAsync();

            // Log status change if changed
            if (oldStatus != request.Status)
            {
                await _auditLogService.LogAccountStatusChangedAsync(user.UserID, user.Email ?? "", oldStatus ?? "Unknown", request.Status, currentUserId);
            }

            _logger.LogInformation("User {UserId} updated by admin {AdminId}", user.UserID, currentUserId);

            TempData["ToastType"] = "success";
            TempData["ToastMessage"] = $"User {user.FullName} updated successfully.";

            return RedirectToAction("Index");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user {UserId}: {Message}", id, ex.Message);
            TempData["ToastType"] = "error";
            TempData["ToastMessage"] = "An error occurred while updating the user.";
            return RedirectToAction("Index");
        }
    }

    /// <summary>
    /// API: Delete/Deactivate user
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var firmId = GetCurrentFirmId();
            var currentUserId = GetCurrentUserId();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == id && u.FirmID == firmId);

            if (user == null)
            {
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "User not found.";
                return RedirectToAction("Index");
            }

            // Don't delete, just deactivate
            var oldStatus = user.Status;
            user.Status = "Inactive";
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _auditLogService.LogAccountStatusChangedAsync(user.UserID, user.Email ?? "", oldStatus ?? "Active", "Inactive", currentUserId);

            _logger.LogInformation("User {UserId} deactivated by admin {AdminId}", user.UserID, currentUserId);

            TempData["ToastType"] = "success";
            TempData["ToastMessage"] = $"User {user.FullName} has been deactivated.";

            return RedirectToAction("Index");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating user {UserId}: {Message}", id, ex.Message);
            TempData["ToastType"] = "error";
            TempData["ToastMessage"] = "An error occurred while deactivating the user.";
            return RedirectToAction("Index");
        }
    }

    /// <summary>
    /// API: Reactivate user
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(int id)
    {
        try
        {
            var firmId = GetCurrentFirmId();
            var currentUserId = GetCurrentUserId();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == id && u.FirmID == firmId);

            if (user == null)
            {
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "User not found.";
                return RedirectToAction("Index");
            }

            var oldStatus = user.Status;
            user.Status = "Active";
            user.LockoutEnd = null;
            user.FailedLoginAttempts = 0;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _auditLogService.LogAccountStatusChangedAsync(user.UserID, user.Email ?? "", oldStatus ?? "Inactive", "Active", currentUserId);

            _logger.LogInformation("User {UserId} activated by admin {AdminId}", user.UserID, currentUserId);

            TempData["ToastType"] = "success";
            TempData["ToastMessage"] = $"User {user.FullName} has been activated.";

            return RedirectToAction("Index");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating user {UserId}: {Message}", id, ex.Message);
            TempData["ToastType"] = "error";
            TempData["ToastMessage"] = "An error occurred while activating the user.";
            return RedirectToAction("Index");
        }
    }

    /// <summary>
    /// API: Reset user password
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(int id, string newPassword)
    {
        try
        {
            var firmId = GetCurrentFirmId();
            var currentUserId = GetCurrentUserId();

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == id && u.FirmID == firmId);

            if (user == null)
            {
                return Json(new { success = false, message = "User not found." });
            }

            var passwordValidation = PasswordHelper.ValidatePassword(newPassword);
            if (!passwordValidation.IsValid)
            {
                return Json(new { success = false, message = string.Join(", ", passwordValidation.Errors) });
            }

            user.PasswordHash = PasswordHelper.HashPassword(newPassword);
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _auditLogService.LogPasswordChangedAsync(user.UserID, user.Email ?? "");

            _logger.LogInformation("Password reset for user {UserId} by admin {AdminId}", user.UserID, currentUserId);

            return Json(new { success = true, message = "Password has been reset successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting password for user {UserId}: {Message}", id, ex.Message);
            return Json(new { success = false, message = "An error occurred while resetting the password." });
        }
    }

    /// <summary>
    /// API: Get users list (for AJAX)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetUsers(string? role = null, string? status = null, string? search = null)
    {
        var firmId = GetCurrentFirmId();

        var query = _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Where(u => u.FirmID == firmId);

        if (!string.IsNullOrEmpty(role))
        {
            query = query.Where(u => u.UserRoles.Any(ur => ur.Role!.RoleName == role));
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(u => u.Status == status);
        }

        if (!string.IsNullOrEmpty(search))
        {
            search = search.ToLower();
            query = query.Where(u =>
                (u.FirstName != null && u.FirstName.ToLower().Contains(search)) ||
                (u.LastName != null && u.LastName.ToLower().Contains(search)) ||
                (u.Email != null && u.Email.ToLower().Contains(search)) ||
                (u.Username != null && u.Username.ToLower().Contains(search)));
        }

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new
            {
                u.UserID,
                u.FirstName,
                u.LastName,
                u.Email,
                u.Username,
                u.PhoneNumber,
                u.Status,
                Role = u.UserRoles.FirstOrDefault() != null ? u.UserRoles.FirstOrDefault()!.Role!.RoleName : "No Role",
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt
            })
            .ToListAsync();

        return Json(users);
    }

    #endregion
}

#region DTOs

public class CreateUserDto
{
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? MiddleName { get; set; }

    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public int RoleId { get; set; }

    [MaxLength(11)]
    public string? PhoneNumber { get; set; }

    public DateTime? DateOfBirth { get; set; }

    [MaxLength(255)]
    public string? Street { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? Province { get; set; }

    [MaxLength(10)]
    public string? ZipCode { get; set; }

    [MaxLength(100)]
    public string? Department { get; set; }

    [MaxLength(100)]
    public string? Position { get; set; }

    [MaxLength(50)]
    public string? BarNumber { get; set; }

    [MaxLength(50)]
    public string? LicenseNumber { get; set; }
}

public class UpdateUserDto
{
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? MiddleName { get; set; }

    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    public int RoleId { get; set; }

    [MaxLength(11)]
    public string? PhoneNumber { get; set; }

    public DateTime? DateOfBirth { get; set; }

    [MaxLength(255)]
    public string? Street { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? Province { get; set; }

    [MaxLength(10)]
    public string? ZipCode { get; set; }

    [MaxLength(100)]
    public string? Department { get; set; }

    [MaxLength(100)]
    public string? Position { get; set; }

    [MaxLength(50)]
    public string? BarNumber { get; set; }

    [MaxLength(50)]
    public string? LicenseNumber { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "Active";

    public string? NewPassword { get; set; }
}

#endregion
