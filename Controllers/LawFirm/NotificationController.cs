using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;
using System.Security.Claims;

namespace CKNDocument.Controllers.LawFirm;

/// <summary>
/// Notification controller for all users
/// Handles user notifications
/// </summary>
[Authorize(Policy = "FirmMember")]
public class NotificationController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public NotificationController(LawFirmDMSDbContext context)
    {
        _context = context;
    }

    private string GetRoleViewPath(string viewName)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Client";
        return $"~/Views/{role}/{viewName}.cshtml";
    }

    public IActionResult Index()
    {
        return View(GetRoleViewPath("Notifications"));
    }

    public IActionResult Unread()
    {
        return View(GetRoleViewPath("Notifications"));
    }

    public IActionResult MarkAsRead(int id)
    {
        return RedirectToAction("Index");
    }

    public IActionResult MarkAllAsRead()
    {
        return RedirectToAction("Index");
    }

    public IActionResult Delete(int id)
    {
        return RedirectToAction("Index");
    }

    public IActionResult Settings()
    {
        return View(GetRoleViewPath("Notifications"));
    }
}
