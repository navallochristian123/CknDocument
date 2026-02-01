using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;
using System.Security.Claims;

namespace CKNDocument.Controllers.LawFirm;

/// <summary>
/// Document management controller
/// Handles document CRUD, upload, download for all roles
/// </summary>
[Authorize(Policy = "FirmMember")]
public class DocumentController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public DocumentController(LawFirmDMSDbContext context)
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
        return View(GetRoleViewPath("Documents"));
    }

    public IActionResult Upload()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Client";
        if (role == "Client")
            return View("~/Views/Client/Upload.cshtml");
        return View(GetRoleViewPath("Documents"));
    }

    public IActionResult Edit(int id)
    {
        return View(GetRoleViewPath("Documents"));
    }

    public IActionResult Details(int id)
    {
        return View(GetRoleViewPath("Documents"));
    }

    public IActionResult Delete(int id)
    {
        return View(GetRoleViewPath("Documents"));
    }

    public IActionResult Download(int id)
    {
        return View(GetRoleViewPath("Documents"));
    }

    public IActionResult Preview(int id)
    {
        return View(GetRoleViewPath("Documents"));
    }

    public IActionResult Search()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Client";
        if (role == "Client")
            return View("~/Views/Client/Search.cshtml");
        return View(GetRoleViewPath("Documents"));
    }

    public IActionResult MyDocuments()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Client";
        if (role == "Client")
            return View("~/Views/Client/MyDocuments.cshtml");
        if (role == "Staff")
            return View("~/Views/Staff/MyDocuments.cshtml");
        return View(GetRoleViewPath("Documents"));
    }

    public IActionResult AssignedToMe()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Staff";
        if (role == "Staff")
            return View("~/Views/Staff/AssignedToMe.cshtml");
        return View(GetRoleViewPath("Documents"));
    }
}
