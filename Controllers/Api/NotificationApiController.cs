using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Services;
using System.Security.Claims;

namespace CKNDocument.Controllers.Api;

/// <summary>
/// API Controller for Notification operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "FirmMember")]
public class NotificationApiController : ControllerBase
{
    private readonly LawFirmDMSDbContext _context;
    private readonly NotificationService _notificationService;
    private readonly ILogger<NotificationApiController> _logger;

    public NotificationApiController(
        LawFirmDMSDbContext context,
        NotificationService notificationService,
        ILogger<NotificationApiController> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _logger = logger;
    }

    private int GetCurrentUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

    /// <summary>
    /// Get all notifications for current user
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetNotifications([FromQuery] bool unreadOnly = false, [FromQuery] int take = 50)
    {
        var userId = GetCurrentUserId();
        var notifications = await _notificationService.GetUserNotificationsAsync(userId, unreadOnly, take);

        var result = notifications.Select(n => new
        {
            id = n.NotificationId,
            title = n.Title,
            message = n.Message,
            notificationType = n.NotificationType,
            actionUrl = n.ActionUrl,
            documentId = n.DocumentId,
            isRead = n.IsRead ?? false,
            readAt = n.ReadAt,
            createdAt = n.CreatedAt
        });

        return Ok(new { success = true, notifications = result });
    }

    /// <summary>
    /// Get unread count
    /// </summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        var userId = GetCurrentUserId();
        var count = await _notificationService.GetUnreadCountAsync(userId);
        return Ok(new { success = true, count });
    }

    /// <summary>
    /// Mark notification as read
    /// </summary>
    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var userId = GetCurrentUserId();
        await _notificationService.MarkAsReadAsync(id, userId);
        return Ok(new { success = true, message = "Notification marked as read" });
    }

    /// <summary>
    /// Mark all notifications as read
    /// </summary>
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var userId = GetCurrentUserId();
        await _notificationService.MarkAllAsReadAsync(userId);
        return Ok(new { success = true, message = "All notifications marked as read" });
    }

    /// <summary>
    /// Delete notification
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = GetCurrentUserId();
        await _notificationService.DeleteAsync(id, userId);
        return Ok(new { success = true, message = "Notification deleted" });
    }

    /// <summary>
    /// Get notification by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetNotification(int id)
    {
        var userId = GetCurrentUserId();

        var notification = await _context.Notifications
            .Include(n => n.Document)
            .FirstOrDefaultAsync(n => n.NotificationId == id && n.UserId == userId);

        if (notification == null)
            return NotFound(new { success = false, message = "Notification not found" });

        // Mark as read when viewed
        if (notification.IsRead != true)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return Ok(new
        {
            success = true,
            notification = new
            {
                id = notification.NotificationId,
                title = notification.Title,
                message = notification.Message,
                notificationType = notification.NotificationType,
                actionUrl = notification.ActionUrl,
                documentId = notification.DocumentId,
                documentTitle = notification.Document?.Title,
                isRead = notification.IsRead,
                readAt = notification.ReadAt,
                createdAt = notification.CreatedAt
            }
        });
    }

    /// <summary>
    /// Get recent notifications (for header dropdown)
    /// </summary>
    [HttpGet("recent")]
    public async Task<IActionResult> GetRecentNotifications([FromQuery] int take = 5)
    {
        var userId = GetCurrentUserId();

        var notifications = await _context.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .Select(n => new
            {
                id = n.NotificationId,
                title = n.Title,
                message = n.Message,
                notificationType = n.NotificationType,
                actionUrl = n.ActionUrl,
                isRead = n.IsRead ?? false,
                createdAt = n.CreatedAt
            })
            .ToListAsync();

        var unreadCount = await _notificationService.GetUnreadCountAsync(userId);

        return Ok(new { success = true, notifications, unreadCount });
    }
}
