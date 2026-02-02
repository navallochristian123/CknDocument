using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;
using CKNDocument.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace CKNDocument.Controllers.SuperAdmin;

/// <summary>
/// LawFirm management controller for SuperAdmin
/// Manages law firm registrations and subscriptions
/// </summary>
[Authorize(Policy = "SuperAdminOnly")]
public class LawFirmController : Controller
{
    private readonly LawFirmDMSDbContext _context;
    private readonly AuditLogService _auditLogService;
    private readonly ILogger<LawFirmController> _logger;

    public LawFirmController(
        LawFirmDMSDbContext context,
        AuditLogService auditLogService,
        ILogger<LawFirmController> logger)
    {
        _context = context;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    private int GetCurrentSuperAdminId()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(idClaim, out int id) ? id : 0;
    }

    #region Views

    /// <summary>
    /// Display all law firms
    /// </summary>
    public async Task<IActionResult> Index(string? search = null, string? status = null, string? plan = null, int page = 1, int pageSize = 10)
    {
        var query = _context.Firms
            .Include(f => f.Subscriptions.OrderByDescending(s => s.CreatedAt).Take(1))
            .Include(f => f.Users.Where(u => u.UserRoles.Any(ur => ur.Role!.RoleName == "Admin")).Take(1))
            .AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            search = search.ToLower();
            query = query.Where(f =>
                (f.FirmName != null && f.FirmName.ToLower().Contains(search)) ||
                (f.ContactEmail != null && f.ContactEmail.ToLower().Contains(search)));
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(f => f.Status == status);
        }

        if (!string.IsNullOrEmpty(plan))
        {
            query = query.Where(f => f.Subscriptions.Any(s => s.PlanType == plan));
        }

        var totalCount = await query.CountAsync();
        var firms = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewData["TotalCount"] = totalCount;
        ViewData["CurrentPage"] = page;
        ViewData["PageSize"] = pageSize;
        ViewData["TotalPages"] = (int)Math.Ceiling((double)totalCount / pageSize);
        ViewData["Search"] = search;
        ViewData["Status"] = status;
        ViewData["Plan"] = plan;

        return View("~/Views/SuperAdmin/LawFirms.cshtml", firms);
    }

    /// <summary>
    /// View firm details
    /// </summary>
    public async Task<IActionResult> Details(int id)
    {
        var firm = await _context.Firms
            .Include(f => f.Subscriptions)
            .Include(f => f.Users)
                .ThenInclude(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(f => f.FirmID == id);

        if (firm == null)
        {
            TempData["ToastType"] = "error";
            TempData["ToastMessage"] = "Law firm not found.";
            return RedirectToAction("Index");
        }

        return View("~/Views/SuperAdmin/LawFirmDetails.cshtml", firm);
    }

    #endregion

    #region API Endpoints

    /// <summary>
    /// API: Get all firms (for AJAX)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetFirms(string? search = null, string? status = null, string? plan = null, int page = 1, int pageSize = 10)
    {
        var query = _context.Firms
            .Include(f => f.Subscriptions.OrderByDescending(s => s.CreatedAt).Take(1))
            .Include(f => f.Users.Where(u => u.UserRoles.Any(ur => ur.Role!.RoleName == "Admin")).Take(1))
                .ThenInclude(u => u.UserRoles)
            .AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            search = search.ToLower();
            query = query.Where(f =>
                (f.FirmName != null && f.FirmName.ToLower().Contains(search)) ||
                (f.ContactEmail != null && f.ContactEmail.ToLower().Contains(search)));
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(f => f.Status == status);
        }

        if (!string.IsNullOrEmpty(plan))
        {
            query = query.Where(f => f.Subscriptions.Any(s => s.PlanType == plan));
        }

        var totalCount = await query.CountAsync();
        var firms = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new
            {
                f.FirmID,
                f.FirmName,
                f.ContactEmail,
                f.PhoneNumber,
                f.Status,
                CreatedAt = f.CreatedAt.HasValue ? f.CreatedAt.Value.ToString("MMM dd, yyyy") : "N/A",
                Subscription = f.Subscriptions.OrderByDescending(s => s.CreatedAt).Select(s => s.PlanType).FirstOrDefault() ?? "None",
                Admin = f.Users.Where(u => u.UserRoles.Any(ur => ur.Role!.RoleName == "Admin"))
                    .Select(u => u.FirstName + " " + u.LastName).FirstOrDefault() ?? "No Admin"
            })
            .ToListAsync();

        return Json(new
        {
            data = firms,
            totalCount,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        });
    }

    /// <summary>
    /// API: Get firm by ID
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetFirm(int id)
    {
        var firm = await _context.Firms
            .Include(f => f.Subscriptions.OrderByDescending(s => s.CreatedAt).Take(1))
            .Include(f => f.Users.Where(u => u.UserRoles.Any(ur => ur.Role!.RoleName == "Admin")).Take(1))
            .FirstOrDefaultAsync(f => f.FirmID == id);

        if (firm == null)
        {
            return Json(new { success = false, message = "Law firm not found." });
        }

        var admin = firm.Users.FirstOrDefault();
        var subscription = firm.Subscriptions.FirstOrDefault();

        return Json(new
        {
            success = true,
            data = new
            {
                firm.FirmID,
                firm.FirmName,
                firm.ContactEmail,
                firm.PhoneNumber,
                firm.Address,
                firm.Status,
                firm.LogoUrl,
                AdminFirstName = admin?.FirstName,
                AdminLastName = admin?.LastName,
                AdminEmail = admin?.Email,
                AdminPhone = admin?.PhoneNumber,
                SubscriptionPlan = subscription?.PlanType,
                SubscriptionStatus = subscription?.Status,
                SubscriptionStart = subscription?.StartDate?.ToString("yyyy-MM-dd"),
                SubscriptionEnd = subscription?.EndDate?.ToString("yyyy-MM-dd")
            }
        });
    }

    /// <summary>
    /// API: Create new law firm with admin and subscription
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromForm] CreateLawFirmDto request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return Json(new { success = false, message = string.Join(", ", errors) });
            }

            var superAdminId = GetCurrentSuperAdminId();

            // Check if firm name exists
            if (await _context.Firms.AnyAsync(f => f.FirmName != null && f.FirmName.ToLower() == request.FirmName.ToLower()))
            {
                return Json(new { success = false, message = "A law firm with this name already exists." });
            }

            // Check if admin email exists in the Users table
            var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == request.AdminEmail.ToLower());
            if (existingUser != null)
            {
                return Json(new { success = false, message = $"The admin email '{request.AdminEmail}' is already registered to an existing user (ID: {existingUser.UserID})." });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 1. Create the Firm
                var firm = new Firm
                {
                    FirmName = request.FirmName.Trim(),
                    ContactEmail = request.ContactEmail.Trim().ToLower(),
                    PhoneNumber = request.PhoneNumber?.Trim(),
                    Address = request.Address?.Trim(),
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow
                };

                _context.Firms.Add(firm);
                await _context.SaveChangesAsync();

                // 2. Create the FirmSubscription
                var subscription = new FirmSubscription
                {
                    FirmID = firm.FirmID,
                    SubscriptionName = $"{firm.FirmName} - {request.SubscriptionPlan}",
                    ContactEmail = firm.ContactEmail,
                    BillingAddress = request.Address?.Trim() ?? string.Empty,
                    Status = "Active",
                    PlanType = request.SubscriptionPlan,
                    StartDate = DateTime.UtcNow,
                    EndDate = DateTime.UtcNow.AddMonths(request.SubscriptionMonths),
                    CreatedAt = DateTime.UtcNow
                };

                _context.FirmSubscriptions.Add(subscription);
                await _context.SaveChangesAsync();

                // 3. Create the Admin User
                var adminRole = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Admin");
                if (adminRole == null)
                {
                    throw new Exception("Admin role not found in database.");
                }

                // Generate temporary password
                var tempPassword = GenerateTemporaryPassword();

                var adminUser = new User
                {
                    FirmID = firm.FirmID,
                    FirstName = request.AdminFirstName.Trim(),
                    LastName = request.AdminLastName.Trim(),
                    Email = request.AdminEmail.Trim().ToLower(),
                    Username = request.AdminEmail.Split('@')[0].ToLower(),
                    PasswordHash = PasswordHelper.HashPassword(tempPassword),
                    PhoneNumber = request.AdminPhone?.Trim(),
                    Status = "Active",
                    EmailConfirmed = true,
                    FailedLoginAttempts = 0,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Users.Add(adminUser);
                await _context.SaveChangesAsync();

                // 4. Assign Admin role
                var userRole = new UserRole
                {
                    UserID = adminUser.UserID,
                    RoleID = adminRole.RoleID,
                    AssignedAt = DateTime.UtcNow
                };

                _context.UserRoles.Add(userRole);
                await _context.SaveChangesAsync();

                // 5. Log the action
                await _auditLogService.LogAsync(
                    action: "LawFirmCreated",
                    entityType: "Firm",
                    entityId: firm.FirmID,
                    description: $"Law firm '{firm.FirmName}' created with {request.SubscriptionPlan} plan",
                    superAdminId: superAdminId,
                    firmId: firm.FirmID,
                    actionCategory: "Administration"
                );

                await transaction.CommitAsync();

                _logger.LogInformation("Law firm {FirmName} created by SuperAdmin {SuperAdminId}", firm.FirmName, superAdminId);

                return Json(new
                {
                    success = true,
                    message = $"Law firm '{firm.FirmName}' created successfully.",
                    data = new
                    {
                        firmId = firm.FirmID,
                        firmName = firm.FirmName,
                        adminEmail = adminUser.Email,
                        temporaryPassword = tempPassword // Only return this once!
                    }
                });
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating law firm: {Message}", ex.Message);
            return Json(new { success = false, message = "An error occurred while creating the law firm." });
        }
    }

    /// <summary>
    /// API: Update law firm
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [FromForm] UpdateLawFirmDto request)
    {
        try
        {
            var superAdminId = GetCurrentSuperAdminId();

            var firm = await _context.Firms
                .Include(f => f.Subscriptions)
                .FirstOrDefaultAsync(f => f.FirmID == id);

            if (firm == null)
            {
                return Json(new { success = false, message = "Law firm not found." });
            }

            // Check for duplicate name
            if (await _context.Firms.AnyAsync(f => f.FirmID != id && f.FirmName != null && f.FirmName.ToLower() == request.FirmName.ToLower()))
            {
                return Json(new { success = false, message = "A law firm with this name already exists." });
            }

            var oldStatus = firm.Status;

            // Update firm
            firm.FirmName = request.FirmName.Trim();
            firm.ContactEmail = request.ContactEmail.Trim().ToLower();
            firm.PhoneNumber = request.PhoneNumber?.Trim();
            firm.Address = request.Address?.Trim();
            firm.Status = request.Status;
            firm.UpdatedAt = DateTime.UtcNow;

            // Update subscription if provided
            if (!string.IsNullOrEmpty(request.SubscriptionPlan))
            {
                var subscription = firm.Subscriptions.OrderByDescending(s => s.CreatedAt).FirstOrDefault();
                if (subscription != null)
                {
                    subscription.PlanType = request.SubscriptionPlan;
                    subscription.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync();

            // Log the action
            await _auditLogService.LogAsync(
                action: "LawFirmUpdated",
                entityType: "Firm",
                entityId: firm.FirmID,
                description: $"Law firm '{firm.FirmName}' updated",
                superAdminId: superAdminId,
                firmId: firm.FirmID,
                actionCategory: "Administration",
                oldValues: oldStatus != request.Status ? $"Status: {oldStatus}" : null,
                newValues: oldStatus != request.Status ? $"Status: {request.Status}" : null
            );

            _logger.LogInformation("Law firm {FirmId} updated by SuperAdmin {SuperAdminId}", id, superAdminId);

            return Json(new { success = true, message = "Law firm updated successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating law firm {FirmId}: {Message}", id, ex.Message);
            return Json(new { success = false, message = "An error occurred while updating the law firm." });
        }
    }

    /// <summary>
    /// API: Delete (deactivate) law firm
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var superAdminId = GetCurrentSuperAdminId();

            var firm = await _context.Firms.FindAsync(id);
            if (firm == null)
            {
                return Json(new { success = false, message = "Law firm not found." });
            }

            var oldStatus = firm.Status;
            firm.Status = "Inactive";
            firm.UpdatedAt = DateTime.UtcNow;

            // Also deactivate subscription
            var subscriptions = await _context.FirmSubscriptions.Where(s => s.FirmID == id).ToListAsync();
            foreach (var sub in subscriptions)
            {
                sub.Status = "Cancelled";
                sub.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                action: "LawFirmDeactivated",
                entityType: "Firm",
                entityId: firm.FirmID,
                description: $"Law firm '{firm.FirmName}' deactivated",
                superAdminId: superAdminId,
                firmId: firm.FirmID,
                actionCategory: "Administration"
            );

            _logger.LogInformation("Law firm {FirmId} deactivated by SuperAdmin {SuperAdminId}", id, superAdminId);

            return Json(new { success = true, message = "Law firm has been deactivated." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating law firm {FirmId}: {Message}", id, ex.Message);
            return Json(new { success = false, message = "An error occurred while deactivating the law firm." });
        }
    }

    /// <summary>
    /// API: Activate law firm
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(int id)
    {
        try
        {
            var superAdminId = GetCurrentSuperAdminId();

            var firm = await _context.Firms.FindAsync(id);
            if (firm == null)
            {
                return Json(new { success = false, message = "Law firm not found." });
            }

            firm.Status = "Active";
            firm.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _auditLogService.LogAsync(
                action: "LawFirmActivated",
                entityType: "Firm",
                entityId: firm.FirmID,
                description: $"Law firm '{firm.FirmName}' activated",
                superAdminId: superAdminId,
                firmId: firm.FirmID,
                actionCategory: "Administration"
            );

            _logger.LogInformation("Law firm {FirmId} activated by SuperAdmin {SuperAdminId}", id, superAdminId);

            return Json(new { success = true, message = "Law firm has been activated." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating law firm {FirmId}: {Message}", id, ex.Message);
            return Json(new { success = false, message = "An error occurred while activating the law firm." });
        }
    }

    /// <summary>
    /// API: Get firm statistics
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Statistics()
    {
        var stats = new
        {
            TotalFirms = await _context.Firms.CountAsync(),
            ActiveFirms = await _context.Firms.CountAsync(f => f.Status == "Active"),
            InactiveFirms = await _context.Firms.CountAsync(f => f.Status == "Inactive"),
            TotalUsers = await _context.Users.CountAsync(),
            ByPlan = await _context.FirmSubscriptions
                .Where(s => s.Status == "Active")
                .GroupBy(s => s.PlanType)
                .Select(g => new { Plan = g.Key ?? "None", Count = g.Count() })
                .ToListAsync(),
            RecentFirms = await _context.Firms
                .OrderByDescending(f => f.CreatedAt)
                .Take(5)
                .Select(f => new { f.FirmID, f.FirmName, f.Status, CreatedAt = f.CreatedAt })
                .ToListAsync()
        };

        return Json(stats);
    }

    #endregion

    #region Helpers

    private static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string lower = "abcdefghijklmnopqrstuvwxyz";
        const string digits = "0123456789";
        const string special = "@$!%*?&";

        var random = new Random();
        var password = new char[12];

        // Ensure at least one of each type
        password[0] = upper[random.Next(upper.Length)];
        password[1] = lower[random.Next(lower.Length)];
        password[2] = digits[random.Next(digits.Length)];
        password[3] = special[random.Next(special.Length)];

        // Fill the rest randomly
        const string all = upper + lower + digits + special;
        for (int i = 4; i < 12; i++)
        {
            password[i] = all[random.Next(all.Length)];
        }

        // Shuffle
        return new string(password.OrderBy(_ => random.Next()).ToArray());
    }

    #endregion
}

#region DTOs

public class CreateLawFirmDto
{
    [Required(ErrorMessage = "Firm name is required")]
    [MaxLength(200)]
    public string FirmName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Contact email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [MaxLength(100)]
    public string ContactEmail { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    [MaxLength(255)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? Province { get; set; }

    [MaxLength(10)]
    public string? ZipCode { get; set; }

    [Required(ErrorMessage = "Admin first name is required")]
    [MaxLength(100)]
    public string AdminFirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Admin last name is required")]
    [MaxLength(100)]
    public string AdminLastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Admin email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [MaxLength(100)]
    public string AdminEmail { get; set; } = string.Empty;

    [MaxLength(11)]
    public string? AdminPhone { get; set; }

    [Required(ErrorMessage = "Subscription plan is required")]
    public string SubscriptionPlan { get; set; } = "Basic";

    public int SubscriptionMonths { get; set; } = 12;
}

public class UpdateLawFirmDto
{
    [Required(ErrorMessage = "Firm name is required")]
    [MaxLength(200)]
    public string FirmName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Contact email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [MaxLength(100)]
    public string ContactEmail { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    [MaxLength(255)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? Province { get; set; }

    [MaxLength(10)]
    public string? ZipCode { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "Active";

    public string? SubscriptionPlan { get; set; }
}

#endregion
