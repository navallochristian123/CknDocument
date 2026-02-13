using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;

namespace CKNDocument.Controllers.LawFirm;

/// <summary>
/// Dashboard controller for Law Firm users
/// Displays role-specific dashboards (Admin, Lawyer, Staff, Client, Auditor)
/// </summary>
[Authorize(Policy = "FirmMember")]
public class DashboardController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public DashboardController(LawFirmDMSDbContext context)
    {
        _context = context;
    }

    public IActionResult Index()
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        
        return role switch
        {
            "Admin" => View("~/Views/Admin/Dashboard.cshtml"),
            "Lawyer" => View("~/Views/Lawyer/Dashboard.cshtml"),
            "Staff" => View("~/Views/Staff/Dashboard.cshtml"),
            "Client" => View("~/Views/Client/Dashboard.cshtml"),
            "Auditor" => View("~/Views/Auditor/Dashboard.cshtml"),
            _ => View("~/Views/Admin/Dashboard.cshtml")
        };
    }

    public IActionResult AdminDashboard()
    {
        return View("~/Views/Admin/Dashboard.cshtml");
    }

    public IActionResult LawyerDashboard()
    {
        return View("~/Views/Lawyer/Dashboard.cshtml");
    }

    public IActionResult StaffDashboard()
    {
        return View("~/Views/Staff/Dashboard.cshtml");
    }

    public IActionResult ClientDashboard()
    {
        return View("~/Views/Client/Dashboard.cshtml");
    }

    public IActionResult AuditorDashboard()
    {
        return View("~/Views/Auditor/Dashboard.cshtml");
    }
}
