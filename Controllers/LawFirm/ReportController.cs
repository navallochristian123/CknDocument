using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;
using System.Security.Claims;

namespace CKNDocument.Controllers.LawFirm;

/// <summary>
/// Report controller for Admin and Auditor
/// Generates various reports
/// </summary>
[Authorize(Roles = "Admin,Auditor")]
public class ReportController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public ReportController(LawFirmDMSDbContext context)
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
        return View(GetRoleViewPath("Reports"));
    }

    public IActionResult DocumentReport()
    {
        return View(GetRoleViewPath("DocumentReport"));
    }

    public IActionResult UserActivityReport()
    {
        return View(GetRoleViewPath("UserActivityReport"));
    }

    public IActionResult ComplianceReport()
    {
        return View(GetRoleViewPath("ComplianceReport"));
    }

    public IActionResult RetentionReport()
    {
        return View(GetRoleViewPath("RetentionReport"));
    }

    public IActionResult Generate(string reportType)
    {
        return View(GetRoleViewPath("GenerateReport"));
    }

    public IActionResult Download(int id)
    {
        return View(GetRoleViewPath("DownloadReport"));
    }

    public IActionResult History()
    {
        return View(GetRoleViewPath("ReportHistory"));
    }
}
