using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;
using System.Security.Claims;

namespace CKNDocument.Controllers.LawFirm;

/// <summary>
/// Document review controller for Staff and Admin
/// Handles document review workflow
/// </summary>
[Authorize(Policy = "AdminOrStaff")]
public class ReviewController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public ReviewController(LawFirmDMSDbContext context)
    {
        _context = context;
    }

    private string GetRoleViewPath(string viewName)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Staff";
        return $"~/Views/{role}/{viewName}.cshtml";
    }

    public IActionResult Index()
    {
        return View(GetRoleViewPath("Reviews"));
    }

    public IActionResult Pending()
    {
        return View(GetRoleViewPath("PendingReviews"));
    }

    public IActionResult Completed()
    {
        return View(GetRoleViewPath("CompletedReviews"));
    }

    public IActionResult Review(int documentId)
    {
        return View(GetRoleViewPath("ReviewDocument"));
    }

    public IActionResult Details(int id)
    {
        return View(GetRoleViewPath("ReviewDetails"));
    }

    public IActionResult Checklist(int documentId)
    {
        return View(GetRoleViewPath("ReviewChecklist"));
    }

    public IActionResult Approve(int documentId)
    {
        return View(GetRoleViewPath("ApproveDocument"));
    }

    public IActionResult Reject(int documentId)
    {
        return View(GetRoleViewPath("RejectDocument"));
    }

    public IActionResult RequestChanges(int documentId)
    {
        return View(GetRoleViewPath("RequestChanges"));
    }
}
