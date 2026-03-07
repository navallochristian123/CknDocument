using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;
using System.Security.Claims;

namespace CKNDocument.Controllers.SuperAdmin;

/// <summary>
/// Manages SuperAdmin-only notifications
/// </summary>
[Authorize(Policy = "SuperAdminOnly")]
public class SuperAdminNotificationController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public SuperAdminNotificationController(LawFirmDMSDbContext context)
    {
        _context = context;
    }

    private int GetSuperAdminId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out int id) ? id : 0;
    }

    public IActionResult Index()
    {
        return View("~/Views/SuperAdmin/Notifications.cshtml");
    }

    /// <summary>
    /// Get recent notifications for the dropdown bell icon
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetNotifications(int take = 20)
    {
        var adminId = GetSuperAdminId();
        var notifications = await _context.SuperAdminNotifications
            .Where(n => n.SuperAdminId == adminId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .Select(n => new
            {
                n.Id,
                n.Title,
                n.Message,
                n.NotificationType,
                n.ActionUrl,
                n.Icon,
                n.IsRead,
                n.CreatedAt
            })
            .ToListAsync();

        return Json(notifications);
    }

    /// <summary>
    /// Get unread notification count for badge
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetUnreadCount()
    {
        var adminId = GetSuperAdminId();
        var count = await _context.SuperAdminNotifications
            .CountAsync(n => n.SuperAdminId == adminId && !n.IsRead);
        return Json(new { count });
    }

    /// <summary>
    /// Mark a single notification as read
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var adminId = GetSuperAdminId();
        var notification = await _context.SuperAdminNotifications
            .FirstOrDefaultAsync(n => n.Id == id && n.SuperAdminId == adminId);

        if (notification == null)
            return Json(new { success = false });

        notification.IsRead = true;
        notification.ReadAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Json(new { success = true });
    }

    /// <summary>
    /// Mark all notifications as read
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var adminId = GetSuperAdminId();
        var unread = await _context.SuperAdminNotifications
            .Where(n => n.SuperAdminId == adminId && !n.IsRead)
            .ToListAsync();

        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return Json(new { success = true, count = unread.Count });
    }

    /// <summary>
    /// Helper: Create a SuperAdmin notification (called from other controllers/services)
    /// </summary>
    public static async Task CreateNotification(
        LawFirmDMSDbContext context,
        int superAdminId,
        string title,
        string message,
        string notificationType,
        string? actionUrl = null,
        string? icon = null)
    {
        context.SuperAdminNotifications.Add(new SuperAdminNotification
        {
            SuperAdminId = superAdminId,
            Title = title,
            Message = message,
            NotificationType = notificationType,
            ActionUrl = actionUrl,
            Icon = icon ?? GetIconForType(notificationType),
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Notify all super admins
    /// </summary>
    public static async Task NotifyAllSuperAdmins(
        LawFirmDMSDbContext context,
        string title,
        string message,
        string notificationType,
        string? actionUrl = null,
        string? icon = null)
    {
        var adminIds = await context.SuperAdmins
            .Where(a => a.Status == "Active")
            .Select(a => a.SuperAdminId)
            .ToListAsync();

        foreach (var adminId in adminIds)
        {
            context.SuperAdminNotifications.Add(new SuperAdminNotification
            {
                SuperAdminId = adminId,
                Title = title,
                Message = message,
                NotificationType = notificationType,
                ActionUrl = actionUrl,
                Icon = icon ?? GetIconForType(notificationType),
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync();
    }

    private static string GetIconForType(string type) => type switch
    {
        "PaymentApproved" => "bi-check-circle-fill",
        "PaymentRejected" => "bi-x-circle-fill",
        "PaymentReceived" => "bi-cash-stack",
        "FirmCreated" => "bi-buildings",
        "FirmDeactivated" => "bi-building-slash",
        "FirmActivated" => "bi-building-check",
        "SubscriptionExpired" => "bi-clock-history",
        "InvoiceCreated" => "bi-receipt",
        "LoginActivity" => "bi-box-arrow-in-right",
        "SettingsChanged" => "bi-gear",
        "ExpenseAdded" => "bi-graph-down-arrow",
        _ => "bi-bell"
    };
}
