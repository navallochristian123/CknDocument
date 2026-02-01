using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;

namespace CKNDocument.Controllers.SuperAdmin;

/// <summary>
/// LawFirm management controller for SuperAdmin
/// Manages law firm subscriptions (Clients in OwnerERP = Firms in LawFirmDMS)
/// </summary>
[Authorize(Policy = "SuperAdminOnly")]
public class LawFirmController : Controller
{
    private readonly OwnerERPDbContext _ownerContext;
    private readonly LawFirmDMSDbContext _lawFirmContext;

    public LawFirmController(OwnerERPDbContext ownerContext, LawFirmDMSDbContext lawFirmContext)
    {
        _ownerContext = ownerContext;
        _lawFirmContext = lawFirmContext;
    }

    public IActionResult Index()
    {
        return View("~/Views/SuperAdmin/LawFirms.cshtml");
    }

    public IActionResult Create()
    {
        return View("~/Views/SuperAdmin/LawFirms.cshtml");
    }

    public IActionResult Edit(int id)
    {
        return View("~/Views/SuperAdmin/LawFirms.cshtml");
    }

    public IActionResult Details(int id)
    {
        return View("~/Views/SuperAdmin/LawFirms.cshtml");
    }

    public IActionResult Delete(int id)
    {
        return View("~/Views/SuperAdmin/LawFirms.cshtml");
    }
}
