using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;
using System.Security.Claims;

namespace CKNDocument.Controllers.LawFirm;

/// <summary>
/// User management controller for Law Firm Admin
/// Manages staff, clients, and auditors
/// </summary>
[Authorize(Policy = "AdminOnly")]
public class UserController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public UserController(LawFirmDMSDbContext context)
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
        return View(GetRoleViewPath("Users"));
    }

    public IActionResult Staff()
    {
        return View(GetRoleViewPath("Staff"));
    }

    public IActionResult Clients()
    {
        return View(GetRoleViewPath("ClientManagement"));
    }

    public IActionResult Auditors()
    {
        return View(GetRoleViewPath("Auditors"));
    }

    public IActionResult Create()
    {
        return View(GetRoleViewPath("CreateUser"));
    }

    public IActionResult Edit(int id)
    {
        return View(GetRoleViewPath("EditUser"));
    }

    public IActionResult Details(int id)
    {
        return View(GetRoleViewPath("UserDetails"));
    }

    public IActionResult Delete(int id)
    {
        return View(GetRoleViewPath("DeleteUser"));
    }
}
