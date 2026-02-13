using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;
using CKNDocument.Services;

namespace CKNDocument.Controllers.Api;

/// <summary>
/// Database diagnostic and setup controller
/// Used for initial setup and troubleshooting
/// </summary>
[AllowAnonymous]
public class SetupController : Controller
{
    private readonly LawFirmDMSDbContext _context;
    private readonly ILogger<SetupController> _logger;

    public SetupController(LawFirmDMSDbContext context, ILogger<SetupController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Check database status and show available accounts
    /// </summary>
    [HttpGet]
    [Route("/setup/status")]
    public async Task<IActionResult> Status()
    {
        var result = new
        {
            DatabaseConnection = false,
            TablesExist = false,
            SuperAdminCount = 0,
            FirmCount = 0,
            RoleCount = 0,
            UserCount = 0,
            UserRoleCount = 0,
            Roles = new List<object>(),
            AvailableAccounts = new List<object>(),
            Errors = new List<string>()
        };

        try
        {
            // Test connection
            await _context.Database.CanConnectAsync();
            result = result with { DatabaseConnection = true };

            // Check tables exist
            try
            {
                var superAdminCount = await _context.SuperAdmins.CountAsync();
                var firmCount = await _context.Firms.CountAsync();
                var roleCount = await _context.Roles.CountAsync();
                var userCount = await _context.Users.CountAsync();
                var userRoleCount = await _context.UserRoles.CountAsync();

                // Get all roles with their IDs
                var roles = await _context.Roles
                    .OrderBy(r => r.RoleID)
                    .Select(r => new { r.RoleID, r.RoleName })
                    .ToListAsync();

                result = result with
                {
                    TablesExist = true,
                    SuperAdminCount = superAdminCount,
                    FirmCount = firmCount,
                    RoleCount = roleCount,
                    UserCount = userCount,
                    UserRoleCount = userRoleCount,
                    Roles = roles.Cast<object>().ToList()
                };

                // Get available accounts with roles
                var accounts = new List<object>();

                // SuperAdmin accounts
                var superAdmins = await _context.SuperAdmins
                    .Select(s => new { s.Username, s.Email, Role = "SuperAdmin", s.Status })
                    .ToListAsync();
                accounts.AddRange(superAdmins);

                // User accounts with roles
                var users = await _context.Users
                    .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                    .Select(u => new
                    {
                        u.Username,
                        u.Email,
                        Role = u.UserRoles.FirstOrDefault() != null ? u.UserRoles.FirstOrDefault()!.Role!.RoleName : "No Role",
                        u.Status
                    })
                    .ToListAsync();
                accounts.AddRange(users);

                result = result with { AvailableAccounts = accounts };
            }
            catch (Exception ex)
            {
                result = result with { Errors = result.Errors.Append($"Table error: {ex.Message}").ToList() };
            }
        }
        catch (Exception ex)
        {
            result = result with { Errors = result.Errors.Append($"Connection error: {ex.Message}").ToList() };
        }

        return Json(result);
    }

    /// <summary>
    /// Fix user roles - reassign proper roles to users
    /// </summary>
    [HttpGet]
    [Route("/setup/fix-roles")]
    public async Task<IActionResult> FixRoles()
    {
        var messages = new List<string>();

        try
        {
            var roles = await _context.Roles.ToListAsync();
            messages.Add($"Roles in database: {string.Join(", ", roles.Select(r => $"{r.RoleName}(ID:{r.RoleID})"))}");

            // Fix duplicate Auditor roles first
            var auditorRoles = roles.Where(r => r.RoleName == "Auditor").OrderBy(r => r.RoleID).ToList();
            if (auditorRoles.Count > 1)
            {
                var primaryAuditorRole = auditorRoles.First();
                var duplicateAuditorRoles = auditorRoles.Skip(1).ToList();

                foreach (var dupRole in duplicateAuditorRoles)
                {
                    // Reassign any users with duplicate role to primary role
                    var usersWithDupRole = await _context.UserRoles.Where(ur => ur.RoleID == dupRole.RoleID).ToListAsync();
                    foreach (var ur in usersWithDupRole)
                    {
                        ur.RoleID = primaryAuditorRole.RoleID;
                        messages.Add($"Moved user {ur.UserID} from Auditor(ID:{dupRole.RoleID}) to Auditor(ID:{primaryAuditorRole.RoleID})");
                    }

                    // Delete duplicate role
                    _context.Roles.Remove(dupRole);
                    messages.Add($"Removed duplicate Auditor role (ID:{dupRole.RoleID})");
                }

                await _context.SaveChangesAsync();
            }

            // Refresh roles after cleanup
            roles = await _context.Roles.ToListAsync();
            var clientRole = roles.FirstOrDefault(r => r.RoleName == "Client");
            var adminRole = roles.FirstOrDefault(r => r.RoleName == "Admin");
            var staffRole = roles.FirstOrDefault(r => r.RoleName == "Staff");
            var auditorRole = roles.FirstOrDefault(r => r.RoleName == "Auditor");

            if (clientRole == null)
            {
                return Json(new { Error = "Client role not found!" });
            }

            // Get all users with their roles
            var users = await _context.Users
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .ToListAsync();

            foreach (var user in users)
            {
                var currentRole = user.UserRoles.FirstOrDefault()?.Role?.RoleName ?? "None";
                var existingUserRole = user.UserRoles.FirstOrDefault();

                if (existingUserRole == null) continue;

                // Determine the correct role based on username/email
                Role? targetRole = null;

                if (user.Username?.ToLower().Contains("admin") == true ||
                    user.Email?.ToLower().Contains("admin") == true)
                {
                    targetRole = adminRole;
                }
                else if (user.Username?.ToLower().Contains("staff") == true ||
                         user.Email?.ToLower().Contains("staff") == true)
                {
                    targetRole = staffRole;
                }
                else if (user.Username?.ToLower().Contains("auditor") == true ||
                         user.Email?.ToLower().Contains("auditor") == true)
                {
                    targetRole = auditorRole;
                }
                else
                {
                    // Default to Client for everyone else
                    targetRole = clientRole;
                }

                // If current role is different from target, update it
                if (targetRole != null && existingUserRole.RoleID != targetRole.RoleID)
                {
                    var oldRoleName = currentRole;
                    existingUserRole.RoleID = targetRole.RoleID;
                    messages.Add($"Changed {user.Username} from {oldRoleName} to {targetRole.RoleName}");
                }
            }

            await _context.SaveChangesAsync();
            messages.Add("Role fixes applied successfully!");

            // Show final state
            var finalRoles = await _context.Roles.ToListAsync();
            messages.Add($"Final roles: {string.Join(", ", finalRoles.Select(r => $"{r.RoleName}(ID:{r.RoleID})"))}");
        }
        catch (Exception ex)
        {
            messages.Add($"Error: {ex.Message}");
            if (ex.InnerException != null)
                messages.Add($"Inner: {ex.InnerException.Message}");
        }

        return Json(new { Messages = messages });
    }

    /// <summary>
    /// Reset passwords for all seeded accounts
    /// </summary>
    [HttpGet]
    [Route("/setup/reset-passwords")]
    public async Task<IActionResult> ResetPasswords()
    {
        var messages = new List<string>();

        try
        {
            // Reset SuperAdmin password
            var superAdmin = await _context.SuperAdmins.FirstOrDefaultAsync(s => s.Username == "superadmin");
            if (superAdmin != null)
            {
                superAdmin.PasswordHash = PasswordHelper.HashPassword("SuperAdmin@123");
                messages.Add("SuperAdmin password reset: superadmin / SuperAdmin@123");
            }
            else
            {
                messages.Add("SuperAdmin account not found");
            }

            // Reset Admin password
            var admin = await _context.Users.FirstOrDefaultAsync(u => u.Username == "admin");
            if (admin != null)
            {
                admin.PasswordHash = PasswordHelper.HashPassword("Admin@123456");
                admin.FailedLoginAttempts = 0;
                admin.LockoutEnd = null;
                messages.Add("Admin password reset: admin / Admin@123456");
            }
            else
            {
                messages.Add("Admin account not found");
            }

            // Reset Staff password
            var staff = await _context.Users.FirstOrDefaultAsync(u => u.Username == "staff");
            if (staff != null)
            {
                staff.PasswordHash = PasswordHelper.HashPassword("Staff@123456");
                staff.FailedLoginAttempts = 0;
                staff.LockoutEnd = null;
                messages.Add("Staff password reset: staff / Staff@123456");
            }
            else
            {
                messages.Add("Staff account not found");
            }

            // Reset Client password
            var client = await _context.Users.FirstOrDefaultAsync(u => u.Username == "client");
            if (client != null)
            {
                client.PasswordHash = PasswordHelper.HashPassword("Client@123456");
                client.FailedLoginAttempts = 0;
                client.LockoutEnd = null;
                messages.Add("Client password reset: client / Client@123456");
            }
            else
            {
                messages.Add("Client account not found");
            }

            // Reset Auditor password
            var auditor = await _context.Users.FirstOrDefaultAsync(u => u.Username == "auditor");
            if (auditor != null)
            {
                auditor.PasswordHash = PasswordHelper.HashPassword("Auditor@12345");
                auditor.FailedLoginAttempts = 0;
                auditor.LockoutEnd = null;
                messages.Add("Auditor password reset: auditor / Auditor@12345");
            }
            else
            {
                messages.Add("Auditor account not found");
            }

            await _context.SaveChangesAsync();
            messages.Add("All passwords reset successfully!");
        }
        catch (Exception ex)
        {
            messages.Add($"Error: {ex.Message}");
            if (ex.InnerException != null)
                messages.Add($"Inner: {ex.InnerException.Message}");
        }

        return Json(new { Messages = messages });
    }

    /// <summary>
    /// Initialize database - create tables and seed data
    /// </summary>
    [HttpGet]
    [Route("/setup/init")]
    public async Task<IActionResult> Initialize()
    {
        var messages = new List<string>();

        try
        {
            messages.Add("Starting database initialization...");

            // Generate and execute create script
            try
            {
                var script = _context.Database.GenerateCreateScript();
                messages.Add($"Generated create script ({script.Length} chars)");

                // Split script into individual statements and execute
                var statements = script.Split(new[] { "GO" }, StringSplitOptions.RemoveEmptyEntries);
                var successCount = 0;
                var skipCount = 0;

                foreach (var statement in statements)
                {
                    var trimmed = statement.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    try
                    {
                        await _context.Database.ExecuteSqlRawAsync(trimmed);
                        successCount++;
                    }
                    catch (Exception stmtEx)
                    {
                        if (stmtEx.Message.Contains("already exists") || stmtEx.Message.Contains("already an object"))
                        {
                            skipCount++;
                        }
                        else
                        {
                            messages.Add($"Statement warning: {stmtEx.Message}");
                        }
                    }
                }

                messages.Add($"Executed {successCount} statements, skipped {skipCount} (already exist)");
            }
            catch (Exception schemaEx)
            {
                messages.Add($"Schema creation skipped: {schemaEx.Message}");
            }

            // Now seed the data
            messages.Add("Seeding data...");

            // Seed SuperAdmin
            if (!await _context.SuperAdmins.AnyAsync())
            {
                var superAdmin = new Models.LawFirmDMS.SuperAdmin
                {
                    Username = "superadmin",
                    Email = "superadmin@ckn.com",
                    PasswordHash = PasswordHelper.HashPassword("SuperAdmin@123"),
                    FirstName = "Super",
                    LastName = "Admin",
                    PhoneNumber = "09123456789",
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow
                };
                _context.SuperAdmins.Add(superAdmin);
                await _context.SaveChangesAsync();
                messages.Add("SuperAdmin created: superadmin / SuperAdmin@123");
            }
            else
            {
                messages.Add("SuperAdmin already exists");
            }

            // Seed Roles
            if (!await _context.Roles.AnyAsync())
            {
                var roles = new List<Role>
                {
                    new Role { RoleName = "Admin", Description = "Administrator" },
                    new Role { RoleName = "Staff", Description = "Staff member" },
                    new Role { RoleName = "Client", Description = "Client" },
                    new Role { RoleName = "Auditor", Description = "Auditor" }
                };
                _context.Roles.AddRange(roles);
                await _context.SaveChangesAsync();
                messages.Add("Roles created: Admin, Staff, Client, Auditor");
            }
            else
            {
                messages.Add($"Roles already exist ({await _context.Roles.CountAsync()})");
            }

            // Seed Firm
            if (!await _context.Firms.AnyAsync())
            {
                var firm = new Firm
                {
                    FirmName = "Demo Law Firm",
                    ContactEmail = "contact@demolawfirm.com",
                    Address = "123 Legal Street",
                    PhoneNumber = "09123456789",
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow
                };
                _context.Firms.Add(firm);
                await _context.SaveChangesAsync();
                messages.Add($"Firm created with ID: {firm.FirmID}");
            }
            else
            {
                messages.Add($"Firms already exist ({await _context.Firms.CountAsync()})");
            }

            // Seed Users
            if (!await _context.Users.AnyAsync())
            {
                var firm = await _context.Firms.FirstAsync();
                var adminRole = await _context.Roles.FirstAsync(r => r.RoleName == "Admin");
                var staffRole = await _context.Roles.FirstAsync(r => r.RoleName == "Staff");
                var clientRole = await _context.Roles.FirstAsync(r => r.RoleName == "Client");
                var auditorRole = await _context.Roles.FirstAsync(r => r.RoleName == "Auditor");

                // Admin
                var admin = new User
                {
                    FirmID = firm.FirmID,
                    FirstName = "Admin",
                    LastName = "User",
                    Email = "admin@lawfirm.com",
                    Username = "admin",
                    PasswordHash = PasswordHelper.HashPassword("Admin@123456"),
                    Status = "Active",
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Users.Add(admin);
                await _context.SaveChangesAsync();
                _context.UserRoles.Add(new UserRole { UserID = admin.UserID, RoleID = adminRole.RoleID, AssignedAt = DateTime.UtcNow });
                messages.Add("Admin created: admin / Admin@123456");

                // Staff
                var staff = new User
                {
                    FirmID = firm.FirmID,
                    FirstName = "Staff",
                    LastName = "User",
                    Email = "staff@lawfirm.com",
                    Username = "staff",
                    PasswordHash = PasswordHelper.HashPassword("Staff@123456"),
                    Status = "Active",
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Users.Add(staff);
                await _context.SaveChangesAsync();
                _context.UserRoles.Add(new UserRole { UserID = staff.UserID, RoleID = staffRole.RoleID, AssignedAt = DateTime.UtcNow });
                messages.Add("Staff created: staff / Staff@123456");

                // Client
                var client = new User
                {
                    FirmID = firm.FirmID,
                    FirstName = "Client",
                    LastName = "User",
                    Email = "client@email.com",
                    Username = "client",
                    PasswordHash = PasswordHelper.HashPassword("Client@123456"),
                    Status = "Active",
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Users.Add(client);
                await _context.SaveChangesAsync();
                _context.UserRoles.Add(new UserRole { UserID = client.UserID, RoleID = clientRole.RoleID, AssignedAt = DateTime.UtcNow });
                messages.Add("Client created: client / Client@123456");

                // Auditor
                var auditor = new User
                {
                    FirmID = firm.FirmID,
                    FirstName = "Auditor",
                    LastName = "User",
                    Email = "auditor@lawfirm.com",
                    Username = "auditor",
                    PasswordHash = PasswordHelper.HashPassword("Auditor@12345"),
                    Status = "Active",
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Users.Add(auditor);
                await _context.SaveChangesAsync();
                _context.UserRoles.Add(new UserRole { UserID = auditor.UserID, RoleID = auditorRole.RoleID, AssignedAt = DateTime.UtcNow });
                messages.Add("Auditor created: auditor / Auditor@12345");

                await _context.SaveChangesAsync();
            }
            else
            {
                messages.Add($"Users already exist ({await _context.Users.CountAsync()})");

                // Check if UserRoles exist
                var userRoleCount = await _context.UserRoles.CountAsync();
                if (userRoleCount == 0)
                {
                    messages.Add("WARNING: Users exist but no UserRoles! Fixing...");

                    // Fix missing UserRoles
                    var users = await _context.Users.ToListAsync();
                    var roles = await _context.Roles.ToListAsync();

                    foreach (var user in users)
                    {
                        var existingRole = await _context.UserRoles.AnyAsync(ur => ur.UserID == user.UserID);
                        if (!existingRole)
                        {
                            // Assign role based on username/email
                            Role? role = null;
                            if (user.Username?.ToLower().Contains("admin") == true || user.Email?.ToLower().Contains("admin") == true)
                                role = roles.FirstOrDefault(r => r.RoleName == "Admin");
                            else if (user.Username?.ToLower().Contains("staff") == true || user.Email?.ToLower().Contains("staff") == true)
                                role = roles.FirstOrDefault(r => r.RoleName == "Staff");
                            else if (user.Username?.ToLower().Contains("auditor") == true || user.Email?.ToLower().Contains("auditor") == true)
                                role = roles.FirstOrDefault(r => r.RoleName == "Auditor");
                            else
                                role = roles.FirstOrDefault(r => r.RoleName == "Client");

                            if (role != null)
                            {
                                _context.UserRoles.Add(new UserRole { UserID = user.UserID, RoleID = role.RoleID, AssignedAt = DateTime.UtcNow });
                                messages.Add($"Assigned {role.RoleName} role to user {user.Username}");
                            }
                        }
                    }
                    await _context.SaveChangesAsync();
                }
            }

            messages.Add("Initialization complete!");
        }
        catch (Exception ex)
        {
            messages.Add($"ERROR: {ex.Message}");
            if (ex.InnerException != null)
                messages.Add($"INNER: {ex.InnerException.Message}");
        }

        return Json(new { Messages = messages });
    }

    /// <summary>
    /// Migrate database to add Lawyer role and convert existing Staff to Lawyer
    /// Staff becomes Metadata Manager role
    /// </summary>
    [HttpGet]
    [Route("/setup/migrate-lawyer-role")]
    public async Task<IActionResult> MigrateLawyerRole()
    {
        var messages = new List<string>();

        try
        {
            messages.Add("Starting Lawyer role migration...");

            var roles = await _context.Roles.ToListAsync();
            messages.Add($"Current roles: {string.Join(", ", roles.Select(r => $"{r.RoleName}(ID:{r.RoleID})"))}");

            // Check if Lawyer role already exists
            var lawyerRole = roles.FirstOrDefault(r => r.RoleName == "Lawyer");
            if (lawyerRole != null)
            {
                messages.Add($"Lawyer role already exists (ID:{lawyerRole.RoleID})");
            }
            else
            {
                // Create Lawyer role
                lawyerRole = new Role
                {
                    RoleName = "Lawyer",
                    Description = "Lawyer - Document review, editing, and approval"
                };
                _context.Roles.Add(lawyerRole);
                await _context.SaveChangesAsync();
                messages.Add($"Created Lawyer role (ID:{lawyerRole.RoleID})");
            }

            // Update Staff role description to Metadata Manager
            var staffRole = roles.FirstOrDefault(r => r.RoleName == "Staff");
            if (staffRole != null)
            {
                staffRole.Description = "Staff - Metadata Manager - Can edit document metadata, tags, and status only";
                messages.Add($"Updated Staff role description to Metadata Manager (ID:{staffRole.RoleID})");
            }

            await _context.SaveChangesAsync();

            // Get the firm for new user
            var firm = await _context.Firms.FirstOrDefaultAsync();

            // Check if lawyer demo user already exists
            var existingLawyer = await _context.Users.FirstOrDefaultAsync(u => u.Username == "lawyer");
            if (existingLawyer != null)
            {
                messages.Add("Lawyer demo user already exists");
            }
            else if (firm != null)
            {
                // Create lawyer demo user
                var lawyerUser = new User
                {
                    FirmID = firm.FirmID,
                    FirstName = "Lawyer",
                    MiddleName = "Demo",
                    LastName = "User",
                    Email = "lawyer@lawfirm.com",
                    Username = "lawyer",
                    PasswordHash = PasswordHelper.HashPassword("Lawyer@123456"),
                    PhoneNumber = "09222222222",
                    DateOfBirth = new DateTime(1990, 5, 20),
                    Street = "456 Legal Avenue",
                    City = "Quezon City",
                    Province = "Metro Manila",
                    ZipCode = "1100",
                    Status = "Active",
                    Department = "Legal",
                    Position = "Associate Lawyer",
                    BarNumber = "12345",
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Users.Add(lawyerUser);
                await _context.SaveChangesAsync();

                // Assign Lawyer role
                _context.UserRoles.Add(new UserRole
                {
                    UserID = lawyerUser.UserID,
                    RoleID = lawyerRole.RoleID,
                    AssignedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                messages.Add($"Created lawyer demo user: lawyer / Lawyer@123456 (UserID:{lawyerUser.UserID})");
            }

            // Show final role state
            var finalRoles = await _context.Roles.OrderBy(r => r.RoleID).ToListAsync();
            messages.Add("=== Final Roles ===");
            foreach (var role in finalRoles)
            {
                messages.Add($"  {role.RoleName} (ID:{role.RoleID}): {role.Description}");
            }

            // Show all demo accounts
            messages.Add("=== Demo Accounts ===");
            messages.Add("  superadmin / SuperAdmin@123 (SuperAdmin)");
            messages.Add("  admin / Admin@123456 (Admin)");
            messages.Add("  lawyer / Lawyer@123456 (Lawyer - can edit documents)");
            messages.Add("  staff / Staff@123456 (Staff - Metadata Manager)");
            messages.Add("  client / Client@123456 (Client)");
            messages.Add("  auditor / Auditor@12345 (Auditor)");

            messages.Add("Migration completed successfully!");
        }
        catch (Exception ex)
        {
            messages.Add($"ERROR: {ex.Message}");
            if (ex.InnerException != null)
                messages.Add($"INNER: {ex.InnerException.Message}");
        }

        return Json(new { Messages = messages });
    }
}
