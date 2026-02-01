using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;
using System.Security.Claims;

namespace CKNDocument.Controllers.LawFirm;

/// <summary>
/// Document version management controller
/// Handles version history, comparison, rollback
/// </summary>
[Authorize(Policy = "FirmMember")]
public class DocumentVersionController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public DocumentVersionController(LawFirmDMSDbContext context)
    {
        _context = context;
    }

    private string GetRoleViewPath(string viewName)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Client";
        return $"~/Views/{role}/{viewName}.cshtml";
    }

    public IActionResult Index(int documentId)
    {
        return View(GetRoleViewPath("DocumentVersions"));
    }

    public IActionResult Details(int id)
    {
        return View(GetRoleViewPath("VersionDetails"));
    }

    public IActionResult Compare(int versionId1, int versionId2)
    {
        return View(GetRoleViewPath("CompareVersions"));
    }

    public IActionResult Rollback(int versionId)
    {
        return View(GetRoleViewPath("RollbackVersion"));
    }

    public IActionResult Download(int id)
    {
        return View(GetRoleViewPath("DownloadVersion"));
    }
}
