using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;
using System.Security.Claims;

namespace CKNDocument.Controllers.LawFirm;

/// <summary>
/// Archive management controller
/// Handles document archiving and restoration for all roles
/// </summary>
[Authorize(Policy = "FirmMember")]
public class ArchiveController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public ArchiveController(LawFirmDMSDbContext context)
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
        return View(GetRoleViewPath("Archive"));
    }

    [Authorize(Policy = "AdminOnly")]
    public IActionResult Archive(int documentId)
    {
        return View("~/Views/Admin/ArchiveDocument.cshtml");
    }

    public IActionResult Restore(int archiveId)
    {
        return View(GetRoleViewPath("RestoreDocument"));
    }

    [Authorize(Policy = "AdminOnly")]
    public IActionResult Details(int id)
    {
        return View("~/Views/Admin/ArchiveDetails.cshtml");
    }

    [Authorize(Policy = "AdminOnly")]
    public IActionResult PermanentDelete(int id)
    {
        return View("~/Views/Admin/PermanentDelete.cshtml");
    }
}
