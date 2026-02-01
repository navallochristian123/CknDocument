using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;
using System.Security.Claims;

namespace CKNDocument.Controllers.LawFirm;

/// <summary>
/// Folder management controller
/// Handles client folder organization
/// </summary>
[Authorize(Policy = "FirmMember")]
public class FolderController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public FolderController(LawFirmDMSDbContext context)
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
        return View(GetRoleViewPath("Folders"));
    }

    public IActionResult Create()
    {
        return View(GetRoleViewPath("Folders"));
    }

    public IActionResult Edit(int id)
    {
        return View(GetRoleViewPath("Folders"));
    }

    public IActionResult Delete(int id)
    {
        return View(GetRoleViewPath("Folders"));
    }

    public IActionResult Contents(int id)
    {
        return View(GetRoleViewPath("Folders"));
    }

    public IActionResult Move(int folderId)
    {
        return View(GetRoleViewPath("Folders"));
    }
}
