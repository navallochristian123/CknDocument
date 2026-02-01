using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Models.DTOs;
using CKNDocument.Models.LawFirmDMS;
using CKNDocument.Services;
using System.Security.Claims;

namespace CKNDocument.Controllers;

/// <summary>
/// Authentication API Controller
/// Handles: Login, Logout, Registration (Client self-register)
/// Uses Cookie authentication for MVC
/// </summary>
public class AuthController : Controller
{
    private readonly OwnerERPDbContext _ownerContext;
    private readonly LawFirmDMSDbContext _lawFirmContext;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        OwnerERPDbContext ownerContext,
        LawFirmDMSDbContext lawFirmContext,
        ILogger<AuthController> logger)
    {
        _ownerContext = ownerContext;
        _lawFirmContext = lawFirmContext;
        _logger = logger;
    }

    #region Views

    /// <summary>
    /// Login page
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectBasedOnRole();
        }
        ViewData["ReturnUrl"] = returnUrl;
        ViewData["Firms"] = await GetFirmsForDropdown();
        return View("~/Views/Auth/Login.cshtml");
    }

    /// <summary>
    /// Registration page
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Register()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectBasedOnRole();
        }
        ViewData["Firms"] = await GetFirmsForDropdown();
        return View("~/Views/Auth/Register.cshtml");
    }

    /// <summary>
    /// Forgot Password page
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword()
    {
        return View("~/Views/Auth/ForgotPassword.cshtml");
    }

    /// <summary>
    /// Access Denied page
    /// </summary>
    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View("~/Views/Auth/AccessDenied.cshtml");
    }

    #endregion

    #region API Endpoints

    /// <summary>
    /// API: Login with email/username and password
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login([FromForm] LoginRequestDto request, string? returnUrl = null)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Please fill in all required fields correctly.";
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Login.cshtml", request);
            }

            // Check if it's SuperAdmin login
            var superAdmin = await _ownerContext.SuperAdmins
                .FirstOrDefaultAsync(s => 
                    (s.Email.ToLower() == request.EmailOrUsername.ToLower() || 
                     s.Username.ToLower() == request.EmailOrUsername.ToLower()) &&
                    s.Status == "Active");

            if (superAdmin != null)
            {
                if (PasswordHelper.VerifyPassword(request.Password, superAdmin.PasswordHash))
                {
                    await SignInUser(
                        superAdmin.SuperAdminId,
                        superAdmin.FirstName + " " + superAdmin.LastName,
                        superAdmin.Email,
                        superAdmin.Username,
                        "SuperAdmin",
                        null,
                        request.RememberMe);

                    superAdmin.LastLoginAt = DateTime.UtcNow;
                    await _ownerContext.SaveChangesAsync();

                    _logger.LogInformation("SuperAdmin {Email} logged in", superAdmin.Email);

                    TempData["ToastType"] = "success";
                    TempData["ToastMessage"] = $"Welcome back, {superAdmin.FirstName}!";

                    return RedirectToAction("Index", "SuperAdminDashboard");
                }
            }

            // Check LawFirm users
            var user = await _lawFirmContext.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .Include(u => u.Firm)
                .FirstOrDefaultAsync(u => 
                    (u.Email != null && u.Email.ToLower() == request.EmailOrUsername.ToLower()) || 
                    (u.Username != null && u.Username.ToLower() == request.EmailOrUsername.ToLower()));

            if (user == null)
            {
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Invalid email/username or password.";
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Login.cshtml", request);
            }

            // Check account status
            if (user.Status != "Active")
            {
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Your account is inactive. Please contact your administrator.";
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Login.cshtml", request);
            }

            // Check lockout
            if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow)
            {
                var remainingMinutes = (user.LockoutEnd.Value - DateTime.UtcNow).TotalMinutes;
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = $"Account locked. Please try again in {Math.Ceiling(remainingMinutes)} minutes.";
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Login.cshtml", request);
            }

            // Verify password
            if (!PasswordHelper.VerifyPassword(request.Password, user.PasswordHash ?? ""))
            {
                user.FailedLoginAttempts = (user.FailedLoginAttempts ?? 0) + 1;

                // Lock account after 5 failed attempts
                if (user.FailedLoginAttempts >= 5)
                {
                    user.LockoutEnd = DateTime.UtcNow.AddMinutes(15);
                    TempData["ToastType"] = "error";
                    TempData["ToastMessage"] = "Account locked due to too many failed attempts. Please try again in 15 minutes.";
                }
                else
                {
                    TempData["ToastType"] = "error";
                    TempData["ToastMessage"] = $"Invalid password. {5 - user.FailedLoginAttempts} attempts remaining.";
                }

                await _lawFirmContext.SaveChangesAsync();
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Login.cshtml", request);
            }

            // Successful login - reset failed attempts
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            user.LastLoginAt = DateTime.UtcNow;
            await _lawFirmContext.SaveChangesAsync();

            // Get user role
            var role = user.UserRoles.FirstOrDefault()?.Role?.RoleName ?? "Client";

            await SignInUser(
                user.UserID,
                user.FullName,
                user.Email ?? "",
                user.Username ?? "",
                role,
                user.FirmID,
                request.RememberMe);

            _logger.LogInformation("User {Email} ({Role}) logged in", user.Email, role);

            TempData["ToastType"] = "success";
            TempData["ToastMessage"] = $"Welcome back, {user.FirstName}!";

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectBasedOnRole(role);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login error for {EmailOrUsername}: {Message}", request.EmailOrUsername, ex.Message);
            
            // Get inner exception details
            var innerMessage = ex.InnerException?.Message ?? ex.Message;
            _logger.LogError("Inner exception: {InnerMessage}", innerMessage);
            
            TempData["ToastType"] = "error";
            #if DEBUG
            TempData["ToastMessage"] = $"Login failed: {innerMessage}";
            #else
            TempData["ToastMessage"] = "An error occurred. Please try again.";
            #endif
            
            ViewData["Firms"] = await GetFirmsForDropdown();
            return View("~/Views/Auth/Login.cshtml", request);
        }
    }

    /// <summary>
    /// API: Client self-registration
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register([FromForm] ClientRegisterRequestDto request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Please fill in all required fields correctly.";
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Register.cshtml", request);
            }

            // Validate password strength
            var passwordValidation = PasswordHelper.ValidatePassword(request.Password);
            if (!passwordValidation.IsValid)
            {
                foreach (var error in passwordValidation.Errors)
                {
                    ModelState.AddModelError("Password", error);
                }
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Password does not meet requirements.";
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Register.cshtml", request);
            }

            // Check if email already exists
            if (await _lawFirmContext.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == request.Email.ToLower()))
            {
                ModelState.AddModelError("Email", "This email is already registered.");
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Email already registered.";
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Register.cshtml", request);
            }

            // Check if username already exists
            if (await _lawFirmContext.Users.AnyAsync(u => u.Username != null && u.Username.ToLower() == request.Username.ToLower()))
            {
                ModelState.AddModelError("Username", "This username is already taken.");
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Username already taken.";
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Register.cshtml", request);
            }

            // Check if firm exists
            var firm = await _lawFirmContext.Firms.FindAsync(request.FirmId);
            if (firm == null)
            {
                ModelState.AddModelError("FirmId", "Selected law firm is not valid.");
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Invalid law firm selected.";
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Register.cshtml", request);
            }

            // Validate age (must be at least 18)
            var age = DateTime.Today.Year - request.DateOfBirth.Year;
            if (request.DateOfBirth > DateTime.Today.AddYears(-age)) age--;
            if (age < 18)
            {
                ModelState.AddModelError("DateOfBirth", "You must be at least 18 years old to register.");
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "You must be at least 18 years old.";
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Register.cshtml", request);
            }

            // Get Client role
            var clientRole = await _lawFirmContext.Roles.FirstOrDefaultAsync(r => r.RoleName == "Client");
            if (clientRole == null)
            {
                _logger.LogError("Client role not found in database");
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "System configuration error. Please contact support.";
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Register.cshtml", request);
            }

            // Create user
            var user = new User
            {
                FirmID = request.FirmId,
                FirstName = request.FirstName.Trim(),
                MiddleName = request.MiddleName.Trim(),
                LastName = request.LastName.Trim(),
                Email = request.Email.Trim().ToLower(),
                Username = request.Username.Trim().ToLower(),
                PasswordHash = PasswordHelper.HashPassword(request.Password),
                PhoneNumber = request.PhoneNumber.Trim(),
                DateOfBirth = request.DateOfBirth,
                Street = request.Street.Trim(),
                City = request.City.Trim(),
                Province = request.Province.Trim(),
                ZipCode = request.ZipCode?.Trim(),
                Status = "Active",
                EmailConfirmed = false,
                FailedLoginAttempts = 0,
                CreatedAt = DateTime.UtcNow
            };

            _lawFirmContext.Users.Add(user);
            await _lawFirmContext.SaveChangesAsync();

            // Assign Client role
            var userRole = new UserRole
            {
                UserID = user.UserID,
                RoleID = clientRole.RoleID,
                AssignedAt = DateTime.UtcNow
            };
            _lawFirmContext.UserRoles.Add(userRole);
            await _lawFirmContext.SaveChangesAsync();

            _logger.LogInformation("New client registered: {Email}", user.Email);

            TempData["ToastType"] = "success";
            TempData["ToastMessage"] = "Registration successful! Please login with your credentials.";

            return RedirectToAction("Login");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration error for {Email}: {Message}", request?.Email ?? "unknown", ex.Message);
            
            // Get inner exception details
            var innerMessage = ex.InnerException?.Message ?? ex.Message;
            _logger.LogError("Inner exception: {InnerMessage}", innerMessage);
            
            TempData["ToastType"] = "error";
            // Show more detailed error in development
            #if DEBUG
            TempData["ToastMessage"] = $"Registration failed: {innerMessage}";
            #else
            TempData["ToastMessage"] = "An error occurred during registration. Please try again.";
            #endif
            
            ViewData["Firms"] = await GetFirmsForDropdown();
            return View("~/Views/Auth/Register.cshtml", request);
        }
    }

    /// <summary>
    /// API: Logout
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        await HttpContext.SignOutAsync("CookieAuth");
        
        _logger.LogInformation("User {Email} logged out", userEmail);

        TempData["ToastType"] = "success";
        TempData["ToastMessage"] = "You have been logged out successfully.";

        return RedirectToAction("Login");
    }

    /// <summary>
    /// API: Get list of law firms for dropdown
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetFirms()
    {
        var firms = await GetFirmsForDropdown();
        return Json(firms);
    }

    /// <summary>
    /// API: Check if email exists
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> CheckEmail(string email)
    {
        var exists = await _lawFirmContext.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == email.ToLower());
        return Json(new { exists });
    }

    /// <summary>
    /// API: Check if username exists
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> CheckUsername(string username)
    {
        var exists = await _lawFirmContext.Users.AnyAsync(u => u.Username != null && u.Username.ToLower() == username.ToLower());
        return Json(new { exists });
    }

    /// <summary>
    /// Diagnostic: Check database connection and tables
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> DiagnosticCheck()
    {
        var result = new Dictionary<string, object>();
        
        try
        {
            // Check OwnerERP connection
            var ownerCanConnect = await _ownerContext.Database.CanConnectAsync();
            result["OwnerERP_CanConnect"] = ownerCanConnect;
            
            if (ownerCanConnect)
            {
                try
                {
                    var superAdmins = await _ownerContext.SuperAdmins
                        .Select(s => new { 
                            s.SuperAdminId, 
                            s.Username, 
                            s.Email, 
                            s.Status,
                            PasswordHashLength = s.PasswordHash.Length,
                            PasswordHashPreview = s.PasswordHash.Length > 20 ? s.PasswordHash.Substring(0, 20) + "..." : s.PasswordHash
                        })
                        .ToListAsync();
                    result["OwnerERP_SuperAdmins"] = superAdmins;
                }
                catch (Exception ex)
                {
                    result["OwnerERP_SuperAdminError"] = ex.Message;
                }
            }
        }
        catch (Exception ex)
        {
            result["OwnerERP_Error"] = ex.Message;
        }
        
        try
        {
            // Check LawFirmDMS connection
            var lawFirmCanConnect = await _lawFirmContext.Database.CanConnectAsync();
            result["LawFirmDMS_CanConnect"] = lawFirmCanConnect;
            
            if (lawFirmCanConnect)
            {
                try
                {
                    var firms = await _lawFirmContext.Firms
                        .Select(f => new { f.FirmID, f.FirmName, f.Status })
                        .ToListAsync();
                    result["LawFirmDMS_Firms"] = firms;
                }
                catch (Exception ex)
                {
                    result["LawFirmDMS_FirmError"] = ex.Message;
                }
                
                try
                {
                    var roles = await _lawFirmContext.Roles
                        .Select(r => new { r.RoleID, r.RoleName })
                        .ToListAsync();
                    result["LawFirmDMS_Roles"] = roles;
                }
                catch (Exception ex)
                {
                    result["LawFirmDMS_RoleError"] = ex.Message;
                }
                
                try
                {
                    var users = await _lawFirmContext.Users
                        .Include(u => u.UserRoles)
                            .ThenInclude(ur => ur.Role)
                        .Select(u => new {
                            u.UserID,
                            u.Username,
                            u.Email,
                            u.Status,
                            Role = u.UserRoles.FirstOrDefault() != null ? u.UserRoles.FirstOrDefault()!.Role!.RoleName : "No Role",
                            PasswordHashLength = u.PasswordHash != null ? u.PasswordHash.Length : 0,
                            PasswordHashPreview = u.PasswordHash != null && u.PasswordHash.Length > 20 ? u.PasswordHash.Substring(0, 20) + "..." : u.PasswordHash
                        })
                        .ToListAsync();
                    result["LawFirmDMS_Users"] = users;
                }
                catch (Exception ex)
                {
                    result["LawFirmDMS_UserError"] = ex.Message;
                }
            }
        }
        catch (Exception ex)
        {
            result["LawFirmDMS_Error"] = ex.Message;
        }
        
        return Json(result);
    }

    /// <summary>
    /// Utility: Generate a password hash for a given password
    /// Use this to get a hash that can be inserted directly into the database
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult GeneratePasswordHash(string password = "Password@123!")
    {
        var hash = PasswordHelper.HashPassword(password);
        return Json(new 
        { 
            password = password,
            hash = hash,
            hashLength = hash.Length,
            sqlUpdateSuperAdmin = $"UPDATE SuperAdmin SET PasswordHash = '{hash}' WHERE SuperAdminId > 0;",
            sqlUpdateUsers = $"UPDATE [User] SET PasswordHash = '{hash}' WHERE UserID > 0;"
        });
    }

    /// <summary>
    /// Utility: Reset all passwords to a known value (DEVELOPMENT ONLY)
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> ResetAllPasswords(string newPassword = "Password@123!")
    {
        #if !DEBUG
        return NotFound();
        #endif

        try
        {
            var hash = PasswordHelper.HashPassword(newPassword);
            
            // Reset SuperAdmin passwords
            var superAdmins = await _ownerContext.SuperAdmins.ToListAsync();
            foreach (var admin in superAdmins)
            {
                admin.PasswordHash = hash;
            }
            await _ownerContext.SaveChangesAsync();

            // Reset User passwords
            var users = await _lawFirmContext.Users.ToListAsync();
            foreach (var user in users)
            {
                user.PasswordHash = hash;
            }
            await _lawFirmContext.SaveChangesAsync();

            return Json(new 
            { 
                success = true, 
                message = $"All passwords have been reset",
                newPassword = newPassword,
                superAdminsUpdated = superAdmins.Count,
                usersUpdated = users.Count
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    #endregion

    #region Helper Methods

    private async Task<List<FirmDropdownDto>> GetFirmsForDropdown()
    {
        return await _lawFirmContext.Firms
            .Where(f => f.Status == "Active")
            .OrderBy(f => f.FirmName)
            .Select(f => new FirmDropdownDto
            {
                FirmId = f.FirmID,
                FirmName = f.FirmName
            })
            .ToListAsync();
    }

    private async Task SignInUser(int userId, string fullName, string email, string username, string role, int? firmId, bool rememberMe)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, fullName),
            new Claim(ClaimTypes.Email, email),
            new Claim("Username", username),
            new Claim(ClaimTypes.Role, NormalizeRole(role))
        };

        if (firmId.HasValue)
        {
            claims.Add(new Claim("FirmId", firmId.Value.ToString()));
        }

        var identity = new ClaimsIdentity(claims, "CookieAuth");
        var principal = new ClaimsPrincipal(identity);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(8)
        };

        await HttpContext.SignInAsync("CookieAuth", principal, authProperties);
    }

    /// <summary>
    /// Normalize role names to handle variations like "Super Admin" vs "SuperAdmin"
    /// </summary>
    private string NormalizeRole(string role)
    {
        if (string.IsNullOrEmpty(role))
            return "Client";

        // Remove spaces and normalize case
        var normalized = role.Replace(" ", "");
        
        return normalized.ToLower() switch
        {
            "superadmin" => "SuperAdmin",
            "admin" => "Admin",
            "staff" => "Staff",
            "client" => "Client",
            "auditor" => "Auditor",
            _ => role // Return original if not matched
        };
    }

    private IActionResult RedirectBasedOnRole(string? role = null)
    {
        role ??= User.FindFirst(ClaimTypes.Role)?.Value;
        
        // Normalize the role for comparison
        var normalizedRole = NormalizeRole(role ?? "");

        return normalizedRole switch
        {
            "SuperAdmin" => RedirectToAction("Index", "SuperAdminDashboard"),
            "Admin" => RedirectToAction("Index", "Dashboard"),
            "Staff" => RedirectToAction("Index", "Dashboard"),
            "Client" => RedirectToAction("Index", "Dashboard"),
            "Auditor" => RedirectToAction("Index", "Dashboard"),
            _ => RedirectToAction("Index", "Home")
        };
    }

    #endregion
}
