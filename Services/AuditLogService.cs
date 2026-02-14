using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CKNDocument.Services;

/// <summary>
/// Service for logging audit events throughout the application
/// </summary>
public class AuditLogService
{
    private readonly LawFirmDMSDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(
        LawFirmDMSDbContext context,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuditLogService> logger)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Log an audit event
    /// </summary>
    public async Task LogAsync(
        string action,
        string? entityType = null,
        int? entityId = null,
        string? description = null,
        string? oldValues = null,
        string? newValues = null,
        string? actionCategory = null,
        int? userId = null,
        int? superAdminId = null,
        int? firmId = null)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;

            // Try to get user info from claims if not provided
            if (httpContext?.User?.Identity?.IsAuthenticated == true)
            {
                var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var roleClaim = httpContext.User.FindFirst(ClaimTypes.Role)?.Value;
                var firmIdClaim = httpContext.User.FindFirst("FirmId")?.Value;

                if (string.IsNullOrEmpty(userIdClaim) == false && int.TryParse(userIdClaim, out int parsedUserId))
                {
                    if (roleClaim == "SuperAdmin")
                    {
                        superAdminId ??= parsedUserId;
                    }
                    else
                    {
                        userId ??= parsedUserId;
                    }
                }

                if (!string.IsNullOrEmpty(firmIdClaim) && int.TryParse(firmIdClaim, out int parsedFirmId))
                {
                    firmId ??= parsedFirmId;
                }
            }

            var ipAddress = GetClientIpAddress();
            var userAgent = httpContext?.Request?.Headers["User-Agent"].ToString();

            var auditLog = new AuditLog
            {
                UserID = userId,
                SuperAdminId = superAdminId,
                FirmID = firmId,
                Action = action,
                EntityType = entityType,
                EntityID = entityId,
                Description = description,
                OldValues = oldValues,
                NewValues = newValues,
                ActionCategory = actionCategory ?? DetermineCategory(action),
                Timestamp = DateTime.UtcNow,
                IPAddress = ipAddress,
                UserAgent = userAgent?.Length > 500 ? userAgent.Substring(0, 500) : userAgent
            };

            _context.AuditLogs.Add(auditLog);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Audit log created: {Action} - {EntityType} - {Description}",
                action, entityType, description);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create audit log for action: {Action}", action);
            // Don't throw - audit logging should not break the main flow
        }
    }

    /// <summary>
    /// Log user login event
    /// </summary>
    public async Task LogLoginAsync(int? userId, int? superAdminId, string email, bool isSuccessful, string? failureReason = null, int? firmId = null)
    {
        var description = isSuccessful
            ? $"User {email} logged in successfully"
            : $"Failed login attempt for {email}: {failureReason}";

        await LogAsync(
            action: isSuccessful ? "Login" : "LoginFailed",
            entityType: superAdminId.HasValue ? "SuperAdmin" : "User",
            entityId: userId ?? superAdminId,
            description: description,
            actionCategory: "Authentication",
            userId: userId,
            superAdminId: superAdminId,
            firmId: firmId);
    }

    /// <summary>
    /// Log user logout event
    /// </summary>
    public async Task LogLogoutAsync(int? userId, int? superAdminId, string email, int? firmId = null)
    {
        await LogAsync(
            action: "Logout",
            entityType: superAdminId.HasValue ? "SuperAdmin" : "User",
            entityId: userId ?? superAdminId,
            description: $"User {email} logged out",
            actionCategory: "Authentication",
            userId: userId,
            superAdminId: superAdminId,
            firmId: firmId);
    }

    /// <summary>
    /// Log account creation event
    /// </summary>
    public async Task LogAccountCreatedAsync(int userId, string email, string role, int? createdByUserId = null, int? firmId = null)
    {
        await LogAsync(
            action: "AccountCreated",
            entityType: "User",
            entityId: userId,
            description: $"Account created for {email} with role {role}",
            actionCategory: "UserManagement",
            userId: createdByUserId,
            firmId: firmId);
    }

    /// <summary>
    /// Log user registration event (self-registration)
    /// </summary>
    public async Task LogRegistrationAsync(int userId, string email, int firmId)
    {
        await LogAsync(
            action: "Registration",
            entityType: "User",
            entityId: userId,
            description: $"New user registered: {email}",
            actionCategory: "Authentication",
            firmId: firmId);
    }

    /// <summary>
    /// Log password change event
    /// </summary>
    public async Task LogPasswordChangedAsync(int userId, string email)
    {
        await LogAsync(
            action: "PasswordChanged",
            entityType: "User",
            entityId: userId,
            description: $"Password changed for {email}",
            actionCategory: "Authentication",
            userId: userId);
    }

    /// <summary>
    /// Log account status change (activated, deactivated, locked, etc.)
    /// </summary>
    public async Task LogAccountStatusChangedAsync(int userId, string email, string oldStatus, string newStatus, int? changedByUserId = null)
    {
        await LogAsync(
            action: "AccountStatusChanged",
            entityType: "User",
            entityId: userId,
            description: $"Account status changed for {email}: {oldStatus} → {newStatus}",
            oldValues: $"Status: {oldStatus}",
            newValues: $"Status: {newStatus}",
            actionCategory: "UserManagement",
            userId: changedByUserId);
    }

    /// <summary>
    /// Log role assignment event
    /// </summary>
    public async Task LogRoleAssignedAsync(int userId, string email, string roleName, int? assignedByUserId = null)
    {
        await LogAsync(
            action: "RoleAssigned",
            entityType: "User",
            entityId: userId,
            description: $"Role '{roleName}' assigned to {email}",
            actionCategory: "UserManagement",
            userId: assignedByUserId);
    }

    /// <summary>
    /// Get client IP address
    /// </summary>
    private string? GetClientIpAddress()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null) return null;

        // Check for forwarded IP (behind proxy/load balancer)
        var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',').First().Trim();
        }

        // Direct connection
        return httpContext.Connection.RemoteIpAddress?.ToString();
    }

    /// <summary>
    /// Determine action category based on action name
    /// </summary>
    private string DetermineCategory(string action)
    {
        return action.ToLower() switch
        {
            "login" or "logout" or "loginfailed" or "registration" or "passwordchanged" or "passwordreset"
                => "Authentication",
            "accountcreated" or "accountupdated" or "accountdeleted" or "accountstatuschanged" or "roleassigned"
                => "UserManagement",
            "documentcreated" or "documentupdated" or "documentdeleted" or "documentdownloaded" or "documentviewed"
                => "DocumentManagement",
            _ => "General"
        };
    }

    /// <summary>
    /// Get audit logs with filtering and pagination
    /// </summary>
    public async Task<(List<AuditLog> Logs, int TotalCount)> GetAuditLogsAsync(
        int? firmId = null,
        int? userId = null,
        string? action = null,
        string? actionCategory = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int page = 1,
        int pageSize = 20)
    {
        var query = _context.AuditLogs
            .Include(a => a.User)
            .Include(a => a.SuperAdmin)
            .Include(a => a.Firm)
            .AsQueryable();

        // For firmId filtering, include logs that either match the firmId OR have null firmId (system-wide/auth logs)
        if (firmId.HasValue)
            query = query.Where(a => a.FirmID == firmId || a.FirmID == null);

        if (userId.HasValue)
            query = query.Where(a => a.UserID == userId);

        if (!string.IsNullOrEmpty(action))
            query = query.Where(a => a.Action == action);

        if (!string.IsNullOrEmpty(actionCategory))
            query = query.Where(a => a.ActionCategory == actionCategory);

        if (startDate.HasValue)
            query = query.Where(a => a.Timestamp >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(a => a.Timestamp <= endDate.Value);

        var totalCount = await query.CountAsync();

        var logs = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (logs, totalCount);
    }
}
