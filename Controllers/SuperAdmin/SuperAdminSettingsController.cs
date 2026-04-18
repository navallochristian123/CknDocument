using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Services;
using System.Security.Claims;

namespace CKNDocument.Controllers.SuperAdmin;

/// <summary>
/// Settings controller for SuperAdmin
/// Handles profile, password change, and appearance settings
/// </summary>
[Authorize(Policy = "SuperAdminOnly")]
public class SuperAdminSettingsController : Controller
{
    private const string EmergencyDataHiddenKey = "EmergencyDataHidden";

    private readonly LawFirmDMSDbContext _context;
    private readonly AuditLogService _auditLogService;
    private readonly ILogger<SuperAdminSettingsController> _logger;

    public SuperAdminSettingsController(
        LawFirmDMSDbContext context,
        AuditLogService auditLogService,
        ILogger<SuperAdminSettingsController> logger)
    {
        _context = context;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View("~/Views/SuperAdmin/Settings.cshtml");
    }

    [HttpGet]
    public async Task<IActionResult> GetSuperAdminAccounts()
    {
        var currentSuperAdminId = GetCurrentSuperAdminId();

        var accounts = await _context.SuperAdmins
            .OrderBy(a => a.Username)
            .Select(a => new
            {
                a.SuperAdminId,
                a.Username,
                a.Email,
                fullName = a.FullName,
                a.Status,
                a.LastLoginAt,
                isCurrent = a.SuperAdminId == currentSuperAdminId,
                isBackup = a.Username.ToLower().Contains("backup") || a.Email.ToLower().Contains("backup")
            })
            .ToListAsync();

        return Json(new { success = true, accounts });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetSuperAdminStatus([FromBody] UpdateSuperAdminStatusDto request)
    {
        try
        {
            var currentSuperAdminId = GetCurrentSuperAdminId();
            if (currentSuperAdminId <= 0)
            {
                return Json(new { success = false, message = "Current SuperAdmin context is invalid." });
            }

            var isBackupAdmin = await IsBackupSuperAdminAsync(currentSuperAdminId);
            if (!isBackupAdmin)
            {
                return Json(new { success = false, message = "Only backup SuperAdmin can change SuperAdmin account status." });
            }

            var target = await _context.SuperAdmins.FirstOrDefaultAsync(a => a.SuperAdminId == request.SuperAdminId);
            if (target == null)
            {
                return Json(new { success = false, message = "SuperAdmin account not found." });
            }

            var normalizedStatus = (request.Status ?? string.Empty).Trim();
            if (!string.Equals(normalizedStatus, "Active", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalizedStatus, "Inactive", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { success = false, message = "Invalid status. Only Active or Inactive is allowed." });
            }

            if (target.SuperAdminId == currentSuperAdminId)
            {
                return Json(new { success = false, message = "You cannot change your own SuperAdmin status." });
            }

            var currentStatus = target.Status ?? "Inactive";
            var desiredStatus = string.Equals(normalizedStatus, "Active", StringComparison.OrdinalIgnoreCase) ? "Active" : "Inactive";

            if (string.Equals(currentStatus, desiredStatus, StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { success = true, message = $"Account is already {desiredStatus}." });
            }

            if (desiredStatus == "Inactive")
            {
                var activeCount = await _context.SuperAdmins.CountAsync(a => a.Status == "Active");
                if (activeCount <= 1)
                {
                    return Json(new { success = false, message = "Cannot deactivate the last active SuperAdmin account." });
                }
            }

            target.Status = desiredStatus;
            target.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                action: "SuperAdminStatusChanged",
                entityType: "SuperAdmin",
                entityId: target.SuperAdminId,
                description: $"SuperAdmin status changed for {target.Email}: {currentStatus} -> {desiredStatus}",
                oldValues: $"Status: {currentStatus}",
                newValues: $"Status: {desiredStatus}",
                actionCategory: "UserManagement",
                superAdminId: currentSuperAdminId);

            await SuperAdminNotificationController.CreateNotification(
                _context,
                target.SuperAdminId,
                "Account Status Updated",
                $"Your SuperAdmin account status was changed to {desiredStatus}.",
                "Account",
                "/SuperAdminSettings",
                "bi-person-gear");

            _logger.LogInformation(
                "SuperAdmin {ActorId} changed status of SuperAdmin {TargetId} to {Status}",
                currentSuperAdminId,
                target.SuperAdminId,
                desiredStatus);

            return Json(new { success = true, message = $"SuperAdmin account set to {desiredStatus}." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update SuperAdmin status for {SuperAdminId}", request.SuperAdminId);
            return Json(new { success = false, message = "An error occurred while updating status." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetRecoveryStatus()
    {
        var currentSuperAdminId = GetCurrentSuperAdminId();
        if (currentSuperAdminId <= 0)
        {
            return Json(new { success = false, message = "Current SuperAdmin context is invalid." });
        }

        var isBackupAdmin = await IsBackupSuperAdminAsync(currentSuperAdminId);
        var emergencyDataHidden = await GetEmergencyDataHiddenAsync();

        return Json(new
        {
            success = true,
            isBackupAdmin,
            emergencyDataHidden
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetEmergencyDataHidden([FromBody] SetEmergencyDataHiddenDto request)
    {
        try
        {
            var currentSuperAdminId = GetCurrentSuperAdminId();
            if (currentSuperAdminId <= 0)
            {
                return Json(new { success = false, message = "Current SuperAdmin context is invalid." });
            }

            var isBackupAdmin = await IsBackupSuperAdminAsync(currentSuperAdminId);
            if (!isBackupAdmin)
            {
                return Json(new { success = false, message = "Only backup SuperAdmin can toggle emergency data hide mode." });
            }

            var newValue = request.Enabled ? "true" : "false";
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
IF EXISTS (SELECT 1 FROM [AppSecuritySetting] WHERE [SettingKey] = {EmergencyDataHiddenKey})
    UPDATE [AppSecuritySetting]
    SET [SettingValue] = {newValue}, [UpdatedAt] = {DateTime.UtcNow}
    WHERE [SettingKey] = {EmergencyDataHiddenKey};
ELSE
    INSERT INTO [AppSecuritySetting] ([SettingKey], [SettingValue], [UpdatedAt])
    VALUES ({EmergencyDataHiddenKey}, {newValue}, {DateTime.UtcNow});
");

            await _auditLogService.LogAsync(
                action: "EmergencyDataHiddenToggled",
                entityType: "System",
                description: request.Enabled
                    ? "Backup SuperAdmin enabled emergency data hide mode"
                    : "Backup SuperAdmin disabled emergency data hide mode",
                actionCategory: "Security",
                superAdminId: currentSuperAdminId);

            await SuperAdminNotificationController.NotifyAllSuperAdmins(
                _context,
                "Emergency Data Shield",
                request.Enabled
                    ? "Emergency data hide mode has been enabled by backup SuperAdmin."
                    : "Emergency data hide mode has been disabled by backup SuperAdmin.",
                "Security",
                "/SuperAdminSettings",
                "bi-shield-lock");

            return Json(new
            {
                success = true,
                message = request.Enabled
                    ? "Emergency data hide mode enabled."
                    : "Emergency data hide mode disabled."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update emergency data hide mode");
            return Json(new { success = false, message = "An error occurred while updating emergency data hide mode." });
        }
    }

    private int GetCurrentSuperAdminId()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(id, out var parsed) ? parsed : 0;
    }

    private async Task<bool> IsBackupSuperAdminAsync(int superAdminId)
    {
        var admin = await _context.SuperAdmins
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.SuperAdminId == superAdminId);

        if (admin == null)
        {
            return false;
        }

        var username = admin.Username?.ToLower() ?? string.Empty;
        var email = admin.Email?.ToLower() ?? string.Empty;
        return username.Contains("backup") || email.Contains("backup");
    }

    private async Task<bool> GetEmergencyDataHiddenAsync()
    {
        var value = await _context.Database
            .SqlQueryRaw<string>($"SELECT TOP 1 [SettingValue] AS [Value] FROM [AppSecuritySetting] WHERE [SettingKey] = '{EmergencyDataHiddenKey}'")
            .FirstOrDefaultAsync();

        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}

public class UpdateSuperAdminStatusDto
{
    public int SuperAdminId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class SetEmergencyDataHiddenDto
{
    public bool Enabled { get; set; }
}
