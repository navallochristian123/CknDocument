using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;
using System.Security.Claims;

namespace CKNDocument.Controllers.LawFirm;

/// <summary>
/// Archive management controller for Admin
/// Handles document archiving and restoration
/// </summary>
[Authorize(Policy = "AdminOnly")]
public class ArchiveController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public ArchiveController(LawFirmDMSDbContext context)
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
        return View(GetRoleViewPath("Archive"));
    }

    public IActionResult Archive(int documentId)
    {
        return View(GetRoleViewPath("ArchiveDocument"));
    }

    public IActionResult Restore(int archiveId)
    {
        return View(GetRoleViewPath("RestoreDocument"));
    }

    public IActionResult Details(int id)
    {
        return View(GetRoleViewPath("ArchiveDetails"));
    }

    public IActionResult PermanentDelete(int id)
    {
        return View(GetRoleViewPath("PermanentDelete"));
    }
}
