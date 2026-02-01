using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;
using System.Security.Claims;

namespace CKNDocument.Controllers.LawFirm;

/// <summary>
/// Role management controller for Law Firm Admin
/// Manages user roles within the firm
/// </summary>
[Authorize(Policy = "AdminOnly")]
public class RoleController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public RoleController(LawFirmDMSDbContext context)
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
        return View(GetRoleViewPath("Roles"));
    }

    public IActionResult Create()
    {
        return View(GetRoleViewPath("CreateRole"));
    }

    public IActionResult Edit(int id)
    {
        return View(GetRoleViewPath("EditRole"));
    }

    public IActionResult Delete(int id)
    {
        return View(GetRoleViewPath("DeleteRole"));
    }

    public IActionResult AssignRole(int userId)
    {
        return View(GetRoleViewPath("AssignRole"));
    }
}
