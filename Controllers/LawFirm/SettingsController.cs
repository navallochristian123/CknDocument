using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CKNDocument.Controllers.LawFirm;

/// <summary>
/// Settings controller for managing user settings and preferences
/// Serves the Settings view for all roles (Admin, Staff, Client, Auditor)
/// </summary>
[Authorize(Policy = "FirmMember")]
public class SettingsController : Controller
{
    /// <summary>
    /// Display settings page based on user role
    /// </summary>
    public IActionResult Index()
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        
        return role switch
        {
            "Admin" => View("~/Views/Admin/Settings.cshtml"),
            "Staff" => View("~/Views/Staff/Settings.cshtml"),
            "Client" => View("~/Views/Client/Settings.cshtml"),
            "Auditor" => View("~/Views/Auditor/Settings.cshtml"),
            _ => View("~/Views/Client/Settings.cshtml")
        };
    }
}
