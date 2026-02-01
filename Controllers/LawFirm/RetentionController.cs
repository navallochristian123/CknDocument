using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;
using System.Security.Claims;

namespace CKNDocument.Controllers.LawFirm;

/// <summary>
/// Document retention policy controller for Admin
/// Manages retention policies and document expiry
/// </summary>
[Authorize(Policy = "AdminOnly")]
public class RetentionController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public RetentionController(LawFirmDMSDbContext context)
    {
        _context = context;
    }

    private string GetRoleViewPath(string viewName)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Admin";
        return $"~/Views/{role}/{viewName}.cshtml";
    }

    public IActionResult Index()
    {
        return View(GetRoleViewPath("Retention"));
    }

    public IActionResult Policies()
    {
        return View(GetRoleViewPath("RetentionPolicies"));
    }

    public IActionResult CreatePolicy()
    {
        return View(GetRoleViewPath("CreateRetentionPolicy"));
    }

    public IActionResult EditPolicy(int id)
    {
        return View(GetRoleViewPath("EditRetentionPolicy"));
    }

    public IActionResult DeletePolicy(int id)
    {
        return View(GetRoleViewPath("DeleteRetentionPolicy"));
    }

    public IActionResult ExpiringDocuments()
    {
        return View(GetRoleViewPath("ExpiringDocuments"));
    }

    public IActionResult ApplyPolicy(int documentId)
    {
        return View(GetRoleViewPath("ApplyRetentionPolicy"));
    }
}
