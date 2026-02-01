using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;
using System.Security.Claims;

namespace CKNDocument.Controllers;

/// <summary>
/// Account controller for profile management
/// </summary>
[Authorize]
public class AccountController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public AccountController(LawFirmDMSDbContext context)
    {
        _context = context;
    }

    private string GetRoleViewPath(string viewName)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Client";
        return $"~/Views/{role}/{viewName}.cshtml";
    }

    public IActionResult Profile()
    {
        return View(GetRoleViewPath("Profile"));
    }

    public IActionResult EditProfile()
    {
        return View(GetRoleViewPath("EditProfile"));
    }

    public IActionResult ChangePassword()
    {
        return View(GetRoleViewPath("ChangePassword"));
    }

    public IActionResult Settings()
    {
        return View(GetRoleViewPath("Settings"));
    }
}
