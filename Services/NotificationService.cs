using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;
using Microsoft.EntityFrameworkCore;

namespace CKNDocument.Services;

/// <summary>
/// Service for managing in-app notifications
/// </summary>
public class NotificationService
{
    private readonly LawFirmDMSDbContext _context;
    private readonly ILogger<NotificationService> _logger;

    // Notification Types
    public const string TYPE_DOCUMENT_UPLOADED = "DocumentUploaded";
    public const string TYPE_DOCUMENT_PENDING_REVIEW = "PendingReview";
    public const string TYPE_STAFF_APPROVED = "StaffApproved";
    public const string TYPE_STAFF_REJECTED = "StaffRejected";
    public const string TYPE_ADMIN_APPROVED = "AdminApproved";
    public const string TYPE_ADMIN_REJECTED = "AdminRejected";
    public const string TYPE_DOCUMENT_VERSIONED = "DocumentVersioned";
    public const string TYPE_DOCUMENT_ARCHIVED = "DocumentArchived";
    public const string TYPE_FOLDER_CREATED = "FolderCreated";
    public const string TYPE_GENERAL = "General";

    public NotificationService(LawFirmDMSDbContext context, ILogger<NotificationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Create a notification for a user
    /// </summary>
    public async Task<Notification> NotifyAsync(
        int userId,
        string title,
        string message,
        string notificationType,
        int? documentId = null,
        string? actionUrl = null)
    {
        var notification = new Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            NotificationType = notificationType,
            DocumentId = documentId,
            ActionUrl = actionUrl,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Notification created for user {UserId}: {Title}", userId, title);

        return notification;
    }

    /// <summary>
    /// Notify all staff members of a firm
    /// </summary>
    public async Task NotifyAllStaffAsync(
        int firmId,
        string title,
        string message,
        string notificationType,
        int? documentId = null,
        string? actionUrl = null)
    {
        var staffMembers = await _context.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .Where(u => u.FirmID == firmId &&
                        u.Status == "Active" &&
                        u.UserRoles.Any(ur => ur.Role != null && ur.Role.RoleName == "Staff"))
            .Select(u => u.UserID)
            .ToListAsync();

        foreach (var staffId in staffMembers)
        {
            await NotifyAsync(staffId, title, message, notificationType, documentId, actionUrl);
        }

        _logger.LogInformation("Notified {Count} staff members for firm {FirmId}", staffMembers.Count, firmId);
    }

    /// <summary>
    /// Notify all admin members of a firm
    /// </summary>
    public async Task NotifyAllAdminAsync(
        int firmId,
        string title,
        string message,
        string notificationType,
        int? documentId = null,
        string? actionUrl = null)
    {
        var adminMembers = await _context.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .Where(u => u.FirmID == firmId &&
                        u.Status == "Active" &&
                        u.UserRoles.Any(ur => ur.Role != null && ur.Role.RoleName == "Admin"))
            .Select(u => u.UserID)
            .ToListAsync();

        foreach (var adminId in adminMembers)
        {
            await NotifyAsync(adminId, title, message, notificationType, documentId, actionUrl);
        }

        _logger.LogInformation("Notified {Count} admin members for firm {FirmId}", adminMembers.Count, firmId);
    }

    /// <summary>
    /// Get notifications for a user
    /// </summary>
    public async Task<List<Notification>> GetUserNotificationsAsync(int userId, bool unreadOnly = false, int take = 50)
    {
        var query = _context.Notifications
            .Include(n => n.Document)
            .Where(n => n.UserId == userId);

        if (unreadOnly)
        {
            query = query.Where(n => n.IsRead != true);
        }

        return await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync();
    }

    /// <summary>
    /// Get unread notification count for a user
    /// </summary>
    public async Task<int> GetUnreadCountAsync(int userId)
    {
        return await _context.Notifications
            .CountAsync(n => n.UserId == userId && n.IsRead != true);
    }

    /// <summary>
    /// Mark notification as read
    /// </summary>
    public async Task MarkAsReadAsync(int notificationId, int userId)
    {
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.NotificationId == notificationId && n.UserId == userId);

        if (notification != null)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Mark all notifications as read for a user
    /// </summary>
    public async Task MarkAllAsReadAsync(int userId)
    {
        var notifications = await _context.Notifications
            .Where(n => n.UserId == userId && n.IsRead != true)
            .ToListAsync();

        foreach (var notification in notifications)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Marked {Count} notifications as read for user {UserId}", notifications.Count, userId);
    }

    /// <summary>
    /// Delete a notification
    /// </summary>
    public async Task DeleteAsync(int notificationId, int userId)
    {
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.NotificationId == notificationId && n.UserId == userId);

        if (notification != null)
        {
            _context.Notifications.Remove(notification);
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Delete old notifications (cleanup)
    /// </summary>
    public async Task CleanupOldNotificationsAsync(int daysToKeep = 90)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
        var oldNotifications = await _context.Notifications
            .Where(n => n.CreatedAt < cutoffDate && n.IsRead == true)
            .ToListAsync();

        _context.Notifications.RemoveRange(oldNotifications);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Cleaned up {Count} old notifications", oldNotifications.Count);
    }
}
