using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CKNDocument.Controllers.SuperAdmin;

/// <summary>
/// Settings controller for SuperAdmin
/// Handles profile, password change, and appearance settings
/// </summary>
[Authorize(Policy = "SuperAdminOnly")]
public class SuperAdminSettingsController : Controller
{
    public IActionResult Index()
    {
        return View("~/Views/SuperAdmin/Settings.cshtml");
    }
}
