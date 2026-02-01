using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;
using System.Security.Claims;

namespace CKNDocument.Controllers.LawFirm;

/// <summary>
/// Audit log controller for Admin and Auditor
/// Displays system audit trail
/// </summary>
[Authorize(Roles = "Admin,Auditor")]
public class AuditController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public AuditController(LawFirmDMSDbContext context)
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
        return View(GetRoleViewPath("AuditLogs"));
    }

    public IActionResult Details(int id)
    {
        return View(GetRoleViewPath("AuditLogs"));
    }

    public IActionResult UserActivity(int userId)
    {
        return View(GetRoleViewPath("AuditLogs"));
    }

    public IActionResult DocumentActivity(int documentId)
    {
        return View(GetRoleViewPath("AuditLogs"));
    }

    public IActionResult Search()
    {
        return View(GetRoleViewPath("AuditLogs"));
    }

    public IActionResult Export()
    {
        return View(GetRoleViewPath("AuditLogs"));
    }
}
