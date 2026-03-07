using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Models.DTOs;
using CKNDocument.Models.LawFirmDMS;
using CKNDocument.Services;
using CKNDocument.Controllers.SuperAdmin;
using System.Security.Claims;

namespace CKNDocument.Controllers;

/// <summary>
/// Authentication API Controller
/// Handles: Login, Logout, Registration (Client self-register)
/// Uses Cookie authentication for MVC
/// </summary>
public class AuthController : Controller
{
    private readonly LawFirmDMSDbContext _context;
    private readonly AuditLogService _auditLogService;
    private readonly ILogger<AuthController> _logger;
    private readonly PayMongoService _payMongoService;
    private readonly ReCaptchaService _reCaptchaService;
    private readonly IConfiguration _configuration;

    public AuthController(
        LawFirmDMSDbContext context,
        AuditLogService auditLogService,
        ILogger<AuthController> logger,
        PayMongoService payMongoService,
        ReCaptchaService reCaptchaService,
        IConfiguration configuration)
    {
        _context = context;
        _auditLogService = auditLogService;
        _logger = logger;
        _payMongoService = payMongoService;
        _reCaptchaService = reCaptchaService;
        _configuration = configuration;
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
        ViewData["ReCaptchaSiteKey"] = _configuration["GoogleReCaptcha:SiteKey"];
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
        ViewData["ReCaptchaSiteKey"] = _configuration["GoogleReCaptcha:SiteKey"];
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
            // Always pass the reCAPTCHA site key to the view
            ViewData["ReCaptchaSiteKey"] = _configuration["GoogleReCaptcha:SiteKey"];

            if (!ModelState.IsValid)
            {
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Please fill in all required fields correctly.";
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Login.cshtml", request);
            }

            // --- Google reCAPTCHA v3 verification ---
            var recaptchaToken = Request.Form["g-recaptcha-response"].ToString();
            var isCaptchaValid = await _reCaptchaService.VerifyAsync(recaptchaToken, "login");
            if (!isCaptchaValid)
            {
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Please complete the reCAPTCHA verification to confirm you are not a robot.";
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Login.cshtml", request);
            }

            // Check if it's SuperAdmin login
            var superAdmin = await _context.SuperAdmins
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
                        superAdmin.FullName,
                        superAdmin.Email,
                        superAdmin.Username,
                        "SuperAdmin",
                        null,
                        request.RememberMe);

                    superAdmin.LastLoginAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();

                    // Log successful login
                    await _auditLogService.LogLoginAsync(null, superAdmin.SuperAdminId, superAdmin.Email, true);

                    // Create login notification for SuperAdmin
                    await SuperAdminNotificationController.CreateNotification(
                        _context, superAdmin.SuperAdminId,
                        "Login Detected",
                        $"SuperAdmin '{superAdmin.FullName}' logged in at {DateTime.UtcNow:MMM dd, yyyy hh:mm tt} UTC.",
                        "Login", "/SuperAdminDashboard", "bi-box-arrow-in-right");

                    _logger.LogInformation("SuperAdmin {Email} logged in", superAdmin.Email);

                    TempData["ToastType"] = "success";
                    TempData["ToastMessage"] = $"Welcome back, {superAdmin.FirstName}!";

                    return RedirectToAction("Index", "SuperAdminDashboard");
                }
                else
                {
                    // Log failed login attempt
                    await _auditLogService.LogLoginAsync(null, superAdmin.SuperAdminId, superAdmin.Email, false, "Invalid password");
                }
            }

            // Check LawFirm users
            var user = await _context.Users
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
            if (user.Status == "Pending")
            {
                await _auditLogService.LogLoginAsync(user.UserID, null, user.Email ?? "", false, "Account pending verification", user.FirmID);
                TempData["ToastType"] = "warning";
                TempData["ToastMessage"] = "Your account is still under verification. Please wait for admin approval. You will be notified once your account is activated.";
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Login.cshtml", request);
            }

            // PendingPayment - firm admin has not completed payment yet
            if (user.Status == "PendingPayment")
            {
                // Allow login but redirect to payment page
                var role2 = user.UserRoles.FirstOrDefault()?.Role?.RoleName ?? "Client";
                if (role2 == "Admin")
                {
                    // Sign them in and redirect to payment
                    user.FailedLoginAttempts = 0;
                    user.LockoutEnd = null;
                    user.LastLoginAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();

                    await SignInUser(
                        user.UserID,
                        user.FullName,
                        user.Email ?? "",
                        user.Username ?? "",
                        role2,
                        user.FirmID,
                        request.RememberMe);

                    TempData["ToastType"] = "warning";
                    TempData["ToastMessage"] = "Please complete your subscription payment to activate your law firm account.";
                    return RedirectToAction("SubscriptionPayment");
                }

                await _auditLogService.LogLoginAsync(user.UserID, null, user.Email ?? "", false, "Firm pending payment", user.FirmID);
                TempData["ToastType"] = "warning";
                TempData["ToastMessage"] = "Your law firm's subscription payment is pending. Please contact your firm administrator.";
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Login.cshtml", request);
            }

            if (user.Status != "Active")
            {
                await _auditLogService.LogLoginAsync(user.UserID, null, user.Email ?? "", false, $"Account inactive: {user.Status}", user.FirmID);
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Your account is inactive. Please contact your administrator.";
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Login.cshtml", request);
            }

            // Check lockout
            if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow)
            {
                var remainingMinutes = (user.LockoutEnd.Value - DateTime.UtcNow).TotalMinutes;
                await _auditLogService.LogLoginAsync(user.UserID, null, user.Email ?? "", false, "Account locked", user.FirmID);
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
                    await _auditLogService.LogLoginAsync(user.UserID, null, user.Email ?? "", false, "Account locked due to failed attempts", user.FirmID);
                    TempData["ToastType"] = "error";
                    TempData["ToastMessage"] = "Account locked due to too many failed attempts. Please try again in 15 minutes.";
                }
                else
                {
                    await _auditLogService.LogLoginAsync(user.UserID, null, user.Email ?? "", false, "Invalid password", user.FirmID);
                    TempData["ToastType"] = "error";
                    TempData["ToastMessage"] = $"Invalid password. {5 - user.FailedLoginAttempts} attempts remaining.";
                }

                await _context.SaveChangesAsync();
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Login.cshtml", request);
            }

            // Successful login - reset failed attempts
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            user.LastLoginAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

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

            // Log successful login
            await _auditLogService.LogLoginAsync(user.UserID, null, user.Email ?? "", true, null, user.FirmID);

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
            // Always pass the reCAPTCHA site key to the view
            ViewData["ReCaptchaSiteKey"] = _configuration["GoogleReCaptcha:SiteKey"];

            if (!ModelState.IsValid)
            {
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Please fill in all required fields correctly.";
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Register.cshtml", request);
            }

            // --- Google reCAPTCHA v3 verification ---
            var recaptchaToken = Request.Form["g-recaptcha-response"].ToString();
            var isCaptchaValid = await _reCaptchaService.VerifyAsync(recaptchaToken, "register");
            if (!isCaptchaValid)
            {
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "reCAPTCHA verification failed. Please try again.";
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
            if (await _context.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == request.Email.ToLower()))
            {
                ModelState.AddModelError("Email", "This email is already registered.");
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Email already registered.";
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Register.cshtml", request);
            }

            // Check if username already exists
            if (await _context.Users.AnyAsync(u => u.Username != null && u.Username.ToLower() == request.Username.ToLower()))
            {
                ModelState.AddModelError("Username", "This username is already taken.");
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Username already taken.";
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Register.cshtml", request);
            }

            // Check if firm exists
            var firm = await _context.Firms.FindAsync(request.FirmId);
            if (firm == null)
            {
                ModelState.AddModelError("FirmId", "Selected law firm is not valid.");
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Invalid law firm selected.";
                ViewData["Firms"] = await GetFirmsForDropdown();
                return View("~/Views/Auth/Register.cshtml", request);
            }

            // Validate FirmCode - proof that client belongs to the law firm
            if (string.IsNullOrWhiteSpace(firm.FirmCode) || 
                !string.Equals(firm.FirmCode, request.FirmCode?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError("FirmCode", "Invalid firm verification code. Please contact the law firm for the correct code.");
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Invalid firm verification code.";
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
            var clientRole = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Client");
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
                Barangay = request.Barangay?.Trim(),
                City = request.City.Trim(),
                Province = request.Province.Trim(),
                ZipCode = request.ZipCode?.Trim(),
                CompanyName = request.CompanyName?.Trim(),
                Purpose = request.Purpose.Trim(),
                Status = "Pending", // Requires admin approval
                EmailConfirmed = false,
                FailedLoginAttempts = 0,
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Assign Client role
            var userRole = new UserRole
            {
                UserID = user.UserID,
                RoleID = clientRole.RoleID,
                AssignedAt = DateTime.UtcNow
            };
            _context.UserRoles.Add(userRole);
            await _context.SaveChangesAsync();

            // Log registration
            await _auditLogService.LogRegistrationAsync(user.UserID, user.Email, request.FirmId);

            _logger.LogInformation("New client registered (pending approval): {Email}", user.Email);

            TempData["ToastType"] = "success";
            TempData["ToastMessage"] = "Registration submitted successfully! Your account is pending verification. You will be notified once approved by the administrator.";

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
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        var firmIdClaim = User.FindFirst("FirmId")?.Value;
        int? firmId = !string.IsNullOrEmpty(firmIdClaim) && int.TryParse(firmIdClaim, out int fid) ? fid : null;

        // Log logout
        if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out int userId))
        {
            if (role == "SuperAdmin")
            {
                await _auditLogService.LogLogoutAsync(null, userId, userEmail ?? "");
            }
            else
            {
                await _auditLogService.LogLogoutAsync(userId, null, userEmail ?? "", firmId);
            }
        }

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
        var exists = await _context.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == email.ToLower());
        return Json(new { exists });
    }

    /// <summary>
    /// API: Check if username exists
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> CheckUsername(string username)
    {
        var exists = await _context.Users.AnyAsync(u => u.Username != null && u.Username.ToLower() == username.ToLower());
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
            // Check database connection
            var canConnect = await _context.Database.CanConnectAsync();
            result["Database_CanConnect"] = canConnect;

            if (canConnect)
            {
                try
                {
                    var superAdmins = await _context.SuperAdmins
                        .Select(s => new
                        {
                            s.SuperAdminId,
                            s.Username,
                            s.Email,
                            s.Status,
                            PasswordHashLength = s.PasswordHash.Length,
                            PasswordHashPreview = s.PasswordHash.Length > 20 ? s.PasswordHash.Substring(0, 20) + "..." : s.PasswordHash
                        })
                        .ToListAsync();
                    result["SuperAdmins"] = superAdmins;
                }
                catch (Exception ex)
                {
                    result["SuperAdminError"] = ex.Message;
                }

                try
                {
                    var firms = await _context.Firms
                        .Select(f => new { f.FirmID, f.FirmName, f.Status })
                        .ToListAsync();
                    result["Firms"] = firms;
                }
                catch (Exception ex)
                {
                    result["FirmError"] = ex.Message;
                }

                try
                {
                    var roles = await _context.Roles
                        .Select(r => new { r.RoleID, r.RoleName })
                        .ToListAsync();
                    result["Roles"] = roles;
                }
                catch (Exception ex)
                {
                    result["RoleError"] = ex.Message;
                }

                try
                {
                    var users = await _context.Users
                        .Include(u => u.UserRoles)
                            .ThenInclude(ur => ur.Role)
                        .Select(u => new
                        {
                            u.UserID,
                            u.Username,
                            u.Email,
                            u.Status,
                            Role = u.UserRoles.FirstOrDefault() != null ? u.UserRoles.FirstOrDefault()!.Role!.RoleName : "No Role",
                            PasswordHashLength = u.PasswordHash != null ? u.PasswordHash.Length : 0,
                            PasswordHashPreview = u.PasswordHash != null && u.PasswordHash.Length > 20 ? u.PasswordHash.Substring(0, 20) + "..." : u.PasswordHash
                        })
                        .ToListAsync();
                    result["Users"] = users;
                }
                catch (Exception ex)
                {
                    result["UserError"] = ex.Message;
                }
            }
        }
        catch (Exception ex)
        {
            result["Error"] = ex.Message;
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
            var superAdmins = await _context.SuperAdmins.ToListAsync();
            foreach (var admin in superAdmins)
            {
                admin.PasswordHash = hash;
            }

            // Reset User passwords
            var users = await _context.Users.ToListAsync();
            foreach (var user in users)
            {
                user.PasswordHash = hash;
            }

            await _context.SaveChangesAsync();

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
        try
        {
            return await _context.Firms
                .Where(f => f.Status == "Active")
                .OrderBy(f => f.FirmName)
                .Select(f => new FirmDropdownDto
                {
                    FirmId = f.FirmID,
                    FirmName = f.FirmName
                })
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading firms for dropdown");
            return new List<FirmDropdownDto>();
        }
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
            "lawyer" => "Lawyer",
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
            "Lawyer" => RedirectToAction("Index", "Dashboard"),
            "Staff" => RedirectToAction("Index", "Dashboard"),
            "Client" => RedirectToAction("Index", "Dashboard"),
            "Auditor" => RedirectToAction("Index", "Dashboard"),
            _ => RedirectToAction("Index", "Home")
        };
    }

    #endregion

    #region Firm Registration

    /// <summary>
    /// Law Firm registration page - admin registers a new firm + admin account
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult RegisterFirm(string? plan = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectBasedOnRole();
        }
        var model = new FirmRegisterRequestDto();
        if (!string.IsNullOrEmpty(plan))
        {
            model.Plan = plan;
        }
        return View("~/Views/Auth/RegisterFirm.cshtml", model);
    }

    /// <summary>
    /// Process law firm registration
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterFirm([FromForm] FirmRegisterRequestDto request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "Please fill in all required fields correctly.";
                return View("~/Views/Auth/RegisterFirm.cshtml", request);
            }

            // Check duplicate email
            var emailExists = await _context.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == request.Email.ToLower());
            if (emailExists)
            {
                ModelState.AddModelError("Email", "This email is already registered.");
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "This email is already registered.";
                return View("~/Views/Auth/RegisterFirm.cshtml", request);
            }

            // Check duplicate username
            var usernameExists = await _context.Users.AnyAsync(u => u.Username != null && u.Username.ToLower() == request.Username.ToLower());
            if (usernameExists)
            {
                ModelState.AddModelError("Username", "This username is already taken.");
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "This username is already taken.";
                return View("~/Views/Auth/RegisterFirm.cshtml", request);
            }

            // Check duplicate firm name
            var firmExists = await _context.Firms.AnyAsync(f => f.FirmName.ToLower() == request.FirmName.ToLower());
            if (firmExists)
            {
                ModelState.AddModelError("FirmName", "A law firm with this name already exists.");
                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = "A law firm with this name already exists.";
                return View("~/Views/Auth/RegisterFirm.cshtml", request);
            }

            // Determine plan details
            var (storageMb, maxUsers, monthlyPrice) = request.Plan switch
            {
                "Starter" => (2048, 5, 1499m),
                "Professional" => (10240, 25, 3499m),
                "Enterprise" => (51200, -1, 7999m), // -1 = unlimited
                _ => (2048, 5, 1499m)
            };

            // Generate a unique firm code
            var firmCode = GenerateFirmCode();

            // Create the Firm
            var firm = new Firm
            {
                FirmName = request.FirmName,
                ContactEmail = request.FirmEmail,
                Address = request.FirmAddress,
                PhoneNumber = request.FirmPhone,
                Status = "PendingPayment",
                FirmCode = firmCode,
                MaxStorageMB = storageMb
            };

            _context.Firms.Add(firm);
            await _context.SaveChangesAsync();

            // Create the Admin role if not exists
            var adminRole = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Admin");
            if (adminRole == null)
            {
                adminRole = new Role { RoleName = "Admin", Description = "Law Firm Administrator" };
                _context.Roles.Add(adminRole);
                await _context.SaveChangesAsync();
            }

            // Create the Admin user (PendingPayment until subscription is paid)
            var adminUser = new User
            {
                FirmID = firm.FirmID,
                FirstName = request.FirstName,
                MiddleName = request.MiddleName,
                LastName = request.LastName,
                Email = request.Email,
                Username = request.Username,
                PasswordHash = PasswordHelper.HashPassword(request.Password),
                PhoneNumber = request.PhoneNumber,
                Status = "PendingPayment",
                EmailConfirmed = true
            };

            _context.Users.Add(adminUser);
            await _context.SaveChangesAsync();

            // Assign Admin role
            _context.UserRoles.Add(new UserRole
            {
                UserID = adminUser.UserID,
                RoleID = adminRole.RoleID
            });

            // Create subscription record
            var subscription = new FirmSubscription
            {
                FirmID = firm.FirmID,
                SubscriptionName = request.Plan,
                ContactEmail = request.FirmEmail,
                BillingAddress = request.FirmAddress,
                Status = "PendingPayment",
                PlanType = request.Plan,
                StartDate = DateTime.UtcNow,
                EndDate = DateTime.UtcNow.AddMonths(1)
            };

            _context.FirmSubscriptions.Add(subscription);
            await _context.SaveChangesAsync();

            // Log the registration
            await _auditLogService.LogAsync(
                action: "FirmRegistered",
                entityType: "Firm",
                entityId: firm.FirmID,
                description: $"Law firm '{firm.FirmName}' registered with {request.Plan} plan",
                actionCategory: "Registration",
                userId: adminUser.UserID,
                firmId: firm.FirmID);

            // Sign in the admin user
            await SignInUser(
                adminUser.UserID,
                adminUser.FullName,
                adminUser.Email ?? "",
                adminUser.Username ?? "",
                "Admin",
                firm.FirmID,
                false);

            TempData["ToastType"] = "success";
            TempData["ToastMessage"] = $"Your law firm '{firm.FirmName}' has been registered. Please complete payment to activate your account.";
            TempData["FirmCode"] = firmCode;
            TempData["Plan"] = request.Plan;
            TempData["MonthlyPrice"] = monthlyPrice.ToString("N0");
            TempData["SubscriptionId"] = subscription.SubscriptionID;

            // Redirect to payment page (NOT dashboard)
            return RedirectToAction("SubscriptionPayment", new { subscriptionId = subscription.SubscriptionID });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firm registration error: {Message}", ex.Message);
            TempData["ToastType"] = "error";
            TempData["ToastMessage"] = "An error occurred during registration. Please try again.";
            return View("~/Views/Auth/RegisterFirm.cshtml", request);
        }
    }

    /// <summary>
    /// Show Subscription Payment page — user must pay before accessing the system
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> SubscriptionPayment(int? subscriptionId = null)
    {
        var firmIdClaim = User.FindFirstValue("FirmId");
        if (string.IsNullOrEmpty(firmIdClaim) || !int.TryParse(firmIdClaim, out var firmId))
            return RedirectToAction("Login");

        // Get the subscription record
        FirmSubscription? subscription;
        if (subscriptionId.HasValue)
        {
            subscription = await _context.FirmSubscriptions
                .Include(s => s.Firm)
                .FirstOrDefaultAsync(s => s.SubscriptionID == subscriptionId.Value && s.FirmID == firmId);
        }
        else
        {
            subscription = await _context.FirmSubscriptions
                .Include(s => s.Firm)
                .Where(s => s.FirmID == firmId && s.Status == "PendingPayment")
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();
        }

        if (subscription == null)
            return RedirectToAction("Index", "Dashboard");

        // If already active, go to dashboard
        if (subscription.Status == "Active")
            return RedirectToAction("Index", "Dashboard");

        // Get pricing info
        var monthlyPrice = subscription.PlanType switch
        {
            "Starter" => 1499m,
            "Professional" => 3499m,
            "Enterprise" => 7999m,
            _ => 1499m
        };

        // Check if there's an existing pending payment
        var pendingPayment = await _context.Payments
            .Where(p => p.SubscriptionID == subscription.SubscriptionID && p.Status == "Pending" && p.PayMongoCheckoutUrl != null)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        ViewBag.Subscription = subscription;
        ViewBag.MonthlyPrice = monthlyPrice;
        ViewBag.FirmName = subscription.Firm?.FirmName ?? "Your Firm";
        ViewBag.FirmCode = subscription.Firm?.FirmCode ?? "";
        ViewBag.PendingPayment = pendingPayment;
        ViewBag.PayMongoConfigured = _payMongoService.IsConfigured;

        return View("~/Views/Auth/SubscriptionPayment.cshtml");
    }

    /// <summary>
    /// Process subscription payment via PayMongo
    /// Uses Source API for e-wallets (GCash, GrabPay) and Intent API for others (Card, Maya, BPI, UnionBank)
    /// </summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessSubscriptionPayment(
        int subscriptionId, string paymentMethod = "gcash",
        string? cardNumber = null, int? expMonth = null, int? expYear = null, string? cvc = null)
    {
        var firmIdClaim = User.FindFirstValue("FirmId");
        if (string.IsNullOrEmpty(firmIdClaim) || !int.TryParse(firmIdClaim, out var firmId))
            return RedirectToAction("Login");

        var subscription = await _context.FirmSubscriptions
            .Include(s => s.Firm)
            .FirstOrDefaultAsync(s => s.SubscriptionID == subscriptionId && s.FirmID == firmId);

        if (subscription == null)
        {
            TempData["ToastType"] = "error";
            TempData["ToastMessage"] = "Subscription not found.";
            return RedirectToAction("SubscriptionPayment");
        }

        if (!_payMongoService.IsConfigured)
        {
            TempData["ToastType"] = "error";
            TempData["ToastMessage"] = "Payment system is currently unavailable. Please contact support.";
            return RedirectToAction("SubscriptionPayment", new { subscriptionId });
        }

        // Validate payment method
        if (!PayMongoService.SupportedMethods.ContainsKey(paymentMethod))
        {
            TempData["ToastType"] = "error";
            TempData["ToastMessage"] = "Invalid payment method. Please select a valid payment option.";
            return RedirectToAction("SubscriptionPayment", new { subscriptionId });
        }

        var monthlyPrice = subscription.PlanType switch
        {
            "Starter" => 1499m,
            "Professional" => 3499m,
            "Enterprise" => 7999m,
            _ => 1499m
        };

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var successUrl = $"{baseUrl}/Auth/SubscriptionPaymentSuccess?subscriptionId={subscriptionId}";
        var failedUrl = $"{baseUrl}/Auth/SubscriptionPaymentFailed?subscriptionId={subscriptionId}";
        var description = $"CKN Document - {subscription.PlanType} Plan Subscription for {subscription.Firm?.FirmName}";

        PayMongoSourceResult result;
        bool isIntentBased = PayMongoService.IntentBasedMethods.Contains(paymentMethod);

        if (isIntentBased)
        {
            // Payment Intent flow for card, Maya, online banking
            CardDetails? cardDetails = null;
            if (paymentMethod == "card" && !string.IsNullOrEmpty(cardNumber))
            {
                cardDetails = new CardDetails
                {
                    CardNumber = cardNumber.Replace(" ", "").Replace("-", ""),
                    ExpMonth = expMonth ?? 12,
                    ExpYear = expYear ?? 2026,
                    Cvc = cvc ?? "123"
                };
            }
            result = await _payMongoService.CreatePaymentViaIntent(monthlyPrice, paymentMethod, successUrl, description, cardDetails);
        }
        else
        {
            // Source flow for GCash, GrabPay
            result = await _payMongoService.CreateSource(monthlyPrice, paymentMethod, successUrl, failedUrl, description);
        }

        if (!result.Success)
        {
            _logger.LogError("Payment creation failed for {Method}: {Error}", paymentMethod, result.ErrorMessage);
            TempData["ToastType"] = "error";
            TempData["ToastMessage"] = $"Payment initialization failed: {result.ErrorMessage}";
            return RedirectToAction("SubscriptionPayment", new { subscriptionId });
        }

        // Create a pending payment record
        var payment = new Payment
        {
            SubscriptionID = subscription.SubscriptionID,
            Amount = monthlyPrice,
            TaxAmount = Math.Round(monthlyPrice * 0.12m / 1.12m, 2),
            NetAmount = Math.Round(monthlyPrice / 1.12m, 2),
            PaymentMethod = paymentMethod,
            PaymentDate = DateTime.Today,
            Status = "Pending",
            PayMongoCheckoutSessionId = isIntentBased ? null : result.SourceId,
            PayMongoPaymentIntentId = isIntentBased ? result.SourceId : null,
            PayMongoCheckoutUrl = result.CheckoutUrl,
            PayMongoStatus = result.Status,
            PaymentReference = $"REG-{DateTime.Now:yyyyMMddHHmmss}",
            Notes = $"Initial subscription payment for {subscription.PlanType} plan via {PayMongoService.SupportedMethods.GetValueOrDefault(paymentMethod, paymentMethod)}",
            CreatedAt = DateTime.Now
        };

        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Payment initiated: PaymentID={PaymentId}, Id={Id}, Method={Method}, Flow={Flow}, Status={Status}",
            payment.PaymentID, result.SourceId, paymentMethod, isIntentBased ? "Intent" : "Source", result.Status);

        // If payment already succeeded (e.g. no 3DS required), finalize immediately
        if (result.Status == "succeeded" && string.IsNullOrEmpty(result.CheckoutUrl))
        {
            return await FinalizeSubscriptionPayment(payment, subscription, firmId, result.SourceId);
        }

        // Redirect to PayMongo authorization page
        if (string.IsNullOrEmpty(result.CheckoutUrl))
        {
            TempData["ToastType"] = "error";
            TempData["ToastMessage"] = "Payment initialization failed: No redirect URL returned.";
            return RedirectToAction("SubscriptionPayment", new { subscriptionId });
        }

        return Redirect(result.CheckoutUrl);
    }

    /// <summary>
    /// PayMongo success callback for subscription payment
    /// Handles both Source-based (GCash/GrabPay) and Intent-based (Card/Maya/BPI/UnionBank) flows
    /// </summary>
    [Authorize]
    public async Task<IActionResult> SubscriptionPaymentSuccess(int subscriptionId)
    {
        var firmIdClaim = User.FindFirstValue("FirmId");
        if (string.IsNullOrEmpty(firmIdClaim) || !int.TryParse(firmIdClaim, out var firmId))
            return RedirectToAction("Login");

        var subscription = await _context.FirmSubscriptions
            .Include(s => s.Firm)
            .FirstOrDefaultAsync(s => s.SubscriptionID == subscriptionId && s.FirmID == firmId);

        if (subscription == null)
        {
            TempData["ToastType"] = "error";
            TempData["ToastMessage"] = "Subscription not found.";
            return RedirectToAction("Login");
        }

        // Find the pending payment (source-based or intent-based)
        var payment = await _context.Payments
            .Where(p => p.SubscriptionID == subscriptionId && p.Status == "Pending"
                && (p.PayMongoCheckoutSessionId != null || p.PayMongoPaymentIntentId != null))
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        if (payment == null)
        {
            TempData["ToastType"] = "error";
            TempData["ToastMessage"] = "Payment record not found.";
            return RedirectToAction("SubscriptionPayment", new { subscriptionId });
        }

        // Determine flow type
        bool isIntentBased = !string.IsNullOrEmpty(payment.PayMongoPaymentIntentId);

        if (isIntentBased)
        {
            // Payment Intent flow — check intent status
            var intentStatus = await _payMongoService.GetPaymentIntentStatus(payment.PayMongoPaymentIntentId!);
            _logger.LogInformation("Intent {Id} status: {Status}", payment.PayMongoPaymentIntentId, intentStatus.Status);

            if (intentStatus.Status == "succeeded")
            {
                return await FinalizeSubscriptionPayment(payment, subscription, firmId, intentStatus.Type);
            }
            else if (intentStatus.Status == "processing" || intentStatus.Status == "awaiting_next_action")
            {
                payment.PayMongoStatus = intentStatus.Status;
                payment.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();

                TempData["ToastType"] = "warning";
                TempData["ToastMessage"] = "Payment is still being processed. Please wait a moment and refresh.";
                return RedirectToAction("SubscriptionPayment", new { subscriptionId });
            }
            else
            {
                payment.PayMongoStatus = intentStatus.Status;
                payment.Status = "Failed";
                payment.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();

                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = $"Payment failed (status: {intentStatus.Status}). Please try again.";
                return RedirectToAction("SubscriptionPayment", new { subscriptionId });
            }
        }

        // Source-based flow — check source status
        var sourceStatus = await _payMongoService.GetSourceStatus(payment.PayMongoCheckoutSessionId!);
        _logger.LogInformation("Source {Id} status: {Status}", payment.PayMongoCheckoutSessionId, sourceStatus.Status);

        if (sourceStatus.Status == "chargeable")
        {
            // Source is chargeable — capture funds
            var payResult = await _payMongoService.CreatePayment(
                payment.PayMongoCheckoutSessionId!,
                payment.Amount ?? 0,
                $"CKN Document - {subscription.PlanType} Plan Activation"
            );

            if (payResult.Success)
            {
                payment.PaymentMethod = payResult.PaymentMethod ?? payment.PaymentMethod;
                return await FinalizeSubscriptionPayment(payment, subscription, firmId, payResult.PaymentId);
            }
            else
            {
                payment.PayMongoStatus = "charge_failed";
                payment.Status = "Failed";
                payment.Notes = payResult.ErrorMessage;
                payment.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();

                TempData["ToastType"] = "error";
                TempData["ToastMessage"] = $"Payment capture failed: {payResult.ErrorMessage}. Please try again.";
                return RedirectToAction("SubscriptionPayment", new { subscriptionId });
            }
        }
        else if (sourceStatus.Status == "paid")
        {
            return await FinalizeSubscriptionPayment(payment, subscription, firmId, null);
        }
        else
        {
            payment.PayMongoStatus = sourceStatus.Status;
            payment.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            TempData["ToastType"] = "warning";
            TempData["ToastMessage"] = $"Payment is still being processed (status: {sourceStatus.Status}). Please wait or try again.";
            return RedirectToAction("SubscriptionPayment", new { subscriptionId });
        }
    }

    /// <summary>
    /// PayMongo failed callback for subscription payment
    /// </summary>
    [Authorize]
    public async Task<IActionResult> SubscriptionPaymentFailed(int subscriptionId)
    {
        var payment = await _context.Payments
            .Where(p => p.SubscriptionID == subscriptionId && p.Status == "Pending")
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        if (payment != null)
        {
            payment.Status = "Failed";
            payment.PayMongoStatus = "failed";
            payment.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();
        }

        TempData["ToastType"] = "error";
        TempData["ToastMessage"] = "Payment was cancelled or failed. Please try again to activate your subscription.";
        return RedirectToAction("SubscriptionPayment", new { subscriptionId });
    }

    /// <summary>
    /// API: Check payment status for polling from the payment page
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> CheckSubscriptionPaymentStatus(string sourceId, string? type = null)
    {
        if (string.IsNullOrEmpty(sourceId))
            return Json(new { status = "error", message = "No source ID" });

        if (type == "intent")
        {
            var intentStatus = await _payMongoService.GetPaymentIntentStatus(sourceId);
            return Json(new { status = intentStatus.Status, amount = intentStatus.Amount });
        }

        var status = await _payMongoService.GetSourceStatus(sourceId);
        return Json(new { status = status.Status, amount = status.Amount });
    }

    /// <summary>
    /// Finalize a successful subscription payment — activate firm, create invoice/revenue
    /// </summary>
    private async Task<IActionResult> FinalizeSubscriptionPayment(
        Payment payment, FirmSubscription subscription, int firmId, string? payMongoPaymentId)
    {
        payment.Status = "Completed";
        payment.PayMongoPaymentId = payMongoPaymentId;
        payment.PayMongoStatus = "succeeded";
        payment.UpdatedAt = DateTime.Now;

        // Activate subscription
        subscription.Status = "Active";
        subscription.StartDate = DateTime.UtcNow;
        subscription.EndDate = DateTime.UtcNow.AddMonths(1);
        subscription.UpdatedAt = DateTime.Now;

        // Activate firm
        var firm = await _context.Firms.FindAsync(firmId);
        if (firm != null)
        {
            firm.Status = "Active";
            firm.UpdatedAt = DateTime.Now;
        }

        // Activate all PendingPayment users of this firm (the admin who registered)
        var pendingUsers = await _context.Users
            .Where(u => u.FirmID == firmId && u.Status == "PendingPayment")
            .ToListAsync();
        foreach (var pu in pendingUsers)
        {
            pu.Status = "Active";
            pu.UpdatedAt = DateTime.Now;
        }

        // Create revenue record
        var revenue = new Revenue
        {
            SubscriptionID = subscription.SubscriptionID,
            PaymentID = payment.PaymentID,
            Source = "Subscription",
            GrossAmount = payment.Amount,
            TaxAmount = payment.TaxAmount,
            NetAmount = payment.NetAmount,
            TaxRate = 12.00m,
            Amount = payment.Amount,
            RevenueDate = DateTime.Today,
            Description = $"{subscription.PlanType} Plan - Initial Payment ({firm?.FirmName})",
            Category = "Subscription",
            CreatedAt = DateTime.Now
        };
        _context.Revenues.Add(revenue);

        // Create an invoice
        var invoice = new Invoice
        {
            SubscriptionID = subscription.SubscriptionID,
            InvoiceNumber = $"INV-{DateTime.Now:yyyyMMdd}-{subscription.SubscriptionID:D4}",
            InvoiceDate = DateTime.Today,
            DueDate = DateTime.Today,
            TotalAmount = payment.Amount,
            PaidAmount = payment.Amount,
            Status = "Paid",
            Notes = $"Initial subscription payment for {subscription.PlanType} plan",
            CreatedAt = DateTime.Now
        };
        _context.Invoices.Add(invoice);
        await _context.SaveChangesAsync();

        // Add invoice line item
        var invoiceItem = new InvoiceItem
        {
            InvoiceID = invoice.InvoiceID,
            Description = $"{subscription.PlanType} Plan - Monthly Subscription",
            Quantity = 1,
            UnitPrice = payment.Amount,
            SubTotal = payment.Amount
        };
        _context.InvoiceItems.Add(invoiceItem);

        payment.InvoiceID = invoice.InvoiceID;
        await _context.SaveChangesAsync();

        // Audit log
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
        await _auditLogService.LogAsync(
            action: "SubscriptionActivated",
            entityType: "FirmSubscription",
            entityId: subscription.SubscriptionID,
            description: $"Subscription activated via {payment.PaymentMethod} payment (₱{payment.Amount:N2})",
            actionCategory: "Payment",
            userId: userId,
            firmId: firmId);

        TempData["ToastType"] = "success";
        TempData["ToastMessage"] = $"Payment successful! Your {subscription.PlanType} plan has been activated. Your Firm Code is: {firm?.FirmCode ?? "N/A"} — share it with team members to join your firm!";
        TempData["FirmCode"] = firm?.FirmCode ?? "";

        return RedirectToAction("Index", "Dashboard");
    }

    private string GenerateFirmCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        var code = new string(Enumerable.Range(0, 8).Select(_ => chars[random.Next(chars.Length)]).ToArray());
        return code;
    }

    #endregion
}
