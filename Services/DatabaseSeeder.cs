using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;

namespace CKNDocument.Services;

/// <summary>
/// Database seeder service
/// Seeds: SuperAdmin, Firm, Roles, Admin/Staff/Client/Auditor (all in unified LawFirmDMS database)
/// </summary>
public class DatabaseSeeder
{
    private readonly LawFirmDMSDbContext _context;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        LawFirmDMSDbContext context,
        ILogger<DatabaseSeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Seed database
    /// </summary>
    public async Task SeedAsync()
    {
        try
        {
            _logger.LogInformation("Starting database seeding...");

            // Try to create database/tables - EnsureCreated won't work if DB already exists
            // So we use a different approach: try to create tables via raw SQL if they don't exist
            _logger.LogInformation("Checking if tables exist...");

            try
            {
                // First try EnsureCreated
                var created = await _context.Database.EnsureCreatedAsync();
                _logger.LogInformation("EnsureCreated result: {Created}", created);

                if (!created)
                {
                    // Database exists but tables might not - try to create schema
                    _logger.LogInformation("Database exists, checking if schema needs to be created...");
                    var script = _context.Database.GenerateCreateScript();

                    // Try to execute the script - it might fail if tables exist, that's OK
                    try
                    {
                        // Check if Roles table exists first
                        var tablesExist = await _context.Database.ExecuteSqlRawAsync(
                            "SELECT CASE WHEN EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Roles') THEN 1 ELSE 0 END");

                        // If tables don't exist, create them
                        var conn = _context.Database.GetDbConnection();
                        await conn.OpenAsync();

                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo'";
                        var tableCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());

                        _logger.LogInformation("Found {TableCount} tables in database", tableCount);

                        if (tableCount < 5)
                        {
                            _logger.LogInformation("Creating database schema...");
                            await _context.Database.ExecuteSqlRawAsync(script);
                            _logger.LogInformation("Schema created successfully");
                        }
                    }
                    catch (Exception schemaEx)
                    {
                        _logger.LogWarning("Schema creation skipped (tables may already exist): {Message}", schemaEx.Message);
                    }
                }
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "Error during database creation: {Message}", dbEx.Message);
            }

            // Seed in order
            await SeedSuperAdminAsync();
            await SeedRolesAsync();
            await SeedFirmAsync();
            await SeedUsersAsync();
            await SeedFirmSubscriptionAsync();

            _logger.LogInformation("Database seeding completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding database: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Seed SuperAdmin account
    /// </summary>
    private async Task SeedSuperAdminAsync()
    {
        try
        {
            _logger.LogInformation("Checking SuperAdmin table...");
            // Primary SuperAdmin
            var primaryUsername = Environment.GetEnvironmentVariable("PRIMARY_SUPERADMIN_USERNAME")?.Trim();
            var primaryEmail = Environment.GetEnvironmentVariable("PRIMARY_SUPERADMIN_EMAIL")?.Trim();
            var primaryPassword = Environment.GetEnvironmentVariable("PRIMARY_SUPERADMIN_PASSWORD")?.Trim();

            primaryUsername = string.IsNullOrWhiteSpace(primaryUsername) ? "superadmin" : primaryUsername;
            primaryEmail = string.IsNullOrWhiteSpace(primaryEmail) ? "superadmin@ckn.com" : primaryEmail;

            var primaryExists = await _context.SuperAdmins
                .AnyAsync(s => s.Username.ToLower() == primaryUsername || s.Email.ToLower() == primaryEmail);

            if (!primaryExists)
            {
                if (string.IsNullOrWhiteSpace(primaryPassword))
                {
                    _logger.LogWarning("Primary SuperAdmin does not exist but PRIMARY_SUPERADMIN_PASSWORD is not set. Skipping primary account creation.");
                }
                else
                {
                    _logger.LogInformation("Seeding primary SuperAdmin...");
                    _context.SuperAdmins.Add(new SuperAdmin
                    {
                        Username = primaryUsername,
                        Email = primaryEmail,
                        PasswordHash = PasswordHelper.HashPassword(primaryPassword),
                        FirstName = "Primary",
                        LastName = "SuperAdmin",
                        PhoneNumber = "09123456789",
                        Status = "Active",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // Backup SuperAdmin
            var backupUsername = Environment.GetEnvironmentVariable("BACKUP_SUPERADMIN_USERNAME")?.Trim();
            var backupEmail = Environment.GetEnvironmentVariable("BACKUP_SUPERADMIN_EMAIL")?.Trim();
            var backupPassword = Environment.GetEnvironmentVariable("BACKUP_SUPERADMIN_PASSWORD")?.Trim();

            backupUsername = string.IsNullOrWhiteSpace(backupUsername) ? "backup_superadmin" : backupUsername;
            backupEmail = string.IsNullOrWhiteSpace(backupEmail) ? "backup.superadmin@ckn.com" : backupEmail;

            var backupExists = await _context.SuperAdmins
                .AnyAsync(s => s.Username.ToLower() == backupUsername.ToLower() || s.Email.ToLower() == backupEmail.ToLower());

            if (!backupExists)
            {
                if (string.IsNullOrWhiteSpace(backupPassword))
                {
                    _logger.LogWarning("Backup SuperAdmin does not exist but BACKUP_SUPERADMIN_PASSWORD is not set. Skipping backup account creation.");
                }
                else
                {
                    _logger.LogInformation("Seeding backup SuperAdmin account...");
                    _context.SuperAdmins.Add(new SuperAdmin
                    {
                        Username = backupUsername,
                        Email = backupEmail,
                        PasswordHash = PasswordHelper.HashPassword(backupPassword),
                        FirstName = "Backup",
                        LastName = "SuperAdmin",
                        PhoneNumber = "09123456780",
                        Status = "Active",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            if (_context.ChangeTracker.HasChanges())
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("SuperAdmin seeding complete (primary/backup ensured).");
            }
            else
            {
                _logger.LogInformation("Primary and backup SuperAdmin accounts already exist.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding SuperAdmin: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Seed roles
    /// </summary>
    private async Task SeedRolesAsync()
    {
        try
        {
            _logger.LogInformation("Checking Roles table...");
            if (await _context.Roles.AnyAsync())
            {
                _logger.LogInformation("Roles already exist, skipping seed");
                return;
            }

            _logger.LogInformation("Seeding Roles...");
            var roles = new List<Role>
            {
                new Role { RoleName = "Admin", Description = "Law Firm Administrator - Full access to firm management" },
                new Role { RoleName = "Lawyer", Description = "Lawyer - Document review, editing, and approval" },
                new Role { RoleName = "Client", Description = "Client - Document upload and viewing" },
                new Role { RoleName = "Auditor", Description = "Auditor - Read-only access for compliance review" },
                new Role { RoleName = "Staff", Description = "Staff - Metadata Manager - Can edit document metadata, tags, and status only" }
            };

            _context.Roles.AddRange(roles);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Roles seeded: Admin, Lawyer, Client, Auditor, Staff (Metadata Manager)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding Roles: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Seed sample law firm
    /// </summary>
    private async Task SeedFirmAsync()
    {
        try
        {
            _logger.LogInformation("Checking Firms table...");
            if (await _context.Firms.AnyAsync())
            {
                _logger.LogInformation("Firms already exist, skipping seed");
                return;
            }

            _logger.LogInformation("Seeding Demo Law Firm...");
            var firm = new Firm
            {
                FirmName = "Demo Law Firm",
                ContactEmail = "contact@demolawfirm.com",
                Address = "123 Legal Street, Metro Manila",
                PhoneNumber = "09123456789",
                Status = "Active",
                FirmCode = "DEMO2024",
                CreatedAt = DateTime.UtcNow
            };

            _context.Firms.Add(firm);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Demo Law Firm seeded with ID: {FirmId}", firm.FirmID);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding Firm: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Seed firm subscription for billing
    /// </summary>
    private async Task SeedFirmSubscriptionAsync()
    {
        try
        {
            _logger.LogInformation("Checking FirmSubscription table...");
            if (await _context.FirmSubscriptions.AnyAsync())
            {
                _logger.LogInformation("FirmSubscriptions already exist, skipping seed");
                return;
            }

            var firm = await _context.Firms.FirstOrDefaultAsync();
            if (firm == null)
            {
                _logger.LogWarning("No firm found, cannot seed subscription");
                return;
            }

            _logger.LogInformation("Seeding Demo Subscription...");
            var subscription = new FirmSubscription
            {
                FirmID = firm.FirmID,
                SubscriptionName = "Demo Law Firm Subscription",
                ContactEmail = firm.ContactEmail,
                BillingAddress = firm.Address,
                Status = "Active",
                PlanType = "Premium",
                StartDate = DateTime.UtcNow,
                EndDate = DateTime.UtcNow.AddYears(1),
                CreatedAt = DateTime.UtcNow
            };

            _context.FirmSubscriptions.Add(subscription);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Demo Subscription seeded with ID: {SubscriptionId}", subscription.SubscriptionID);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding FirmSubscription: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Seed sample users
    /// </summary>
    private async Task SeedUsersAsync()
    {
        try
        {
            _logger.LogInformation("Checking Users table...");
            if (await _context.Users.AnyAsync())
            {
                _logger.LogInformation("Users already exist, skipping seed");
                return;
            }

            var firm = await _context.Firms.FirstOrDefaultAsync();
            if (firm == null)
            {
                _logger.LogWarning("No firm found, cannot seed users");
                return;
            }

            var roles = await _context.Roles.ToListAsync();
            if (!roles.Any())
            {
                _logger.LogWarning("No roles found, cannot seed users");
                return;
            }

            var adminRole = roles.FirstOrDefault(r => r.RoleName == "Admin");
            var lawyerRole = roles.FirstOrDefault(r => r.RoleName == "Lawyer");
            var staffRole = roles.FirstOrDefault(r => r.RoleName == "Staff");
            var clientRole = roles.FirstOrDefault(r => r.RoleName == "Client");
            var auditorRole = roles.FirstOrDefault(r => r.RoleName == "Auditor");

            if (adminRole == null || lawyerRole == null || staffRole == null || clientRole == null || auditorRole == null)
            {
                _logger.LogWarning("Not all roles found, cannot seed users");
                return;
            }

            _logger.LogInformation("Seeding Users...");

            // Admin User
            var adminUser = new User
            {
                FirmID = firm.FirmID,
                FirstName = "Admin",
                MiddleName = "Demo",
                LastName = "User",
                Email = "admin@lawfirm.com",
                Username = "admin_demo",
                PasswordHash = PasswordHelper.HashPassword("User@123456"),
                PhoneNumber = "09111111111",
                DateOfBirth = new DateTime(1985, 1, 15),
                Street = "123 Admin Street",
                City = "Makati",
                Province = "Metro Manila",
                ZipCode = "1200",
                Status = "Active",
                Department = "Management",
                Position = "Administrator",
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.Users.Add(adminUser);
            await _context.SaveChangesAsync();
            _context.UserRoles.Add(new UserRole { UserID = adminUser.UserID, RoleID = adminRole.RoleID, AssignedAt = DateTime.UtcNow });
            _logger.LogInformation("Admin user seeded: admin_demo / User@123456");

            // Lawyer User (can edit document content)
            var lawyerUser = new User
            {
                FirmID = firm.FirmID,
                FirstName = "Lawyer",
                MiddleName = "Demo",
                LastName = "User",
                Email = "lawyer@lawfirm.com",
                Username = "lawyer_demo",
                PasswordHash = PasswordHelper.HashPassword("User@123456"),
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
            _context.UserRoles.Add(new UserRole { UserID = lawyerUser.UserID, RoleID = lawyerRole.RoleID, AssignedAt = DateTime.UtcNow });
            _logger.LogInformation("Lawyer user seeded: lawyer_demo / User@123456");

            // Staff User (Metadata Manager - can only edit metadata, tags, status)
            var staffUser = new User
            {
                FirmID = firm.FirmID,
                FirstName = "Staff",
                MiddleName = "Demo",
                LastName = "User",
                Email = "staff@lawfirm.com",
                Username = "staff_demo",
                PasswordHash = PasswordHelper.HashPassword("User@123456"),
                PhoneNumber = "09555555555",
                DateOfBirth = new DateTime(1992, 7, 15),
                Street = "789 Staff Street",
                City = "Mandaluyong",
                Province = "Metro Manila",
                ZipCode = "1550",
                Status = "Active",
                Department = "Records",
                Position = "Metadata Manager",
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.Users.Add(staffUser);
            await _context.SaveChangesAsync();
            _context.UserRoles.Add(new UserRole { UserID = staffUser.UserID, RoleID = staffRole.RoleID, AssignedAt = DateTime.UtcNow });
            _logger.LogInformation("Staff user seeded: staff_demo / User@123456");

            // Client User
            var clientUser = new User
            {
                FirmID = firm.FirmID,
                FirstName = "Client",
                MiddleName = "Demo",
                LastName = "User",
                Email = "client@email.com",
                Username = "client_demo",
                PasswordHash = PasswordHelper.HashPassword("User@123456"),
                PhoneNumber = "09333333333",
                DateOfBirth = new DateTime(1988, 8, 10),
                Street = "789 Client Road",
                City = "Pasig",
                Province = "Metro Manila",
                Barangay = "Kapitolyo",
                ZipCode = "1600",
                CompanyName = "Demo Company Inc.",
                Purpose = "Document management for legal matters",
                Status = "Active",
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.Users.Add(clientUser);
            await _context.SaveChangesAsync();
            _context.UserRoles.Add(new UserRole { UserID = clientUser.UserID, RoleID = clientRole.RoleID, AssignedAt = DateTime.UtcNow });
            _logger.LogInformation("Client user seeded: client_demo / User@123456");

            // Auditor User
            var auditorUser = new User
            {
                FirmID = firm.FirmID,
                FirstName = "Auditor",
                MiddleName = "Demo",
                LastName = "User",
                Email = "auditor@lawfirm.com",
                Username = "auditor_demo",
                PasswordHash = PasswordHelper.HashPassword("User@123456"),
                PhoneNumber = "09444444444",
                DateOfBirth = new DateTime(1982, 3, 25),
                Street = "321 Auditor Lane",
                City = "Taguig",
                Province = "Metro Manila",
                ZipCode = "1630",
                Status = "Active",
                Department = "Audit",
                Position = "External Auditor",
                LicenseNumber = "AUD-2024-001",
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.Users.Add(auditorUser);
            await _context.SaveChangesAsync();
            _context.UserRoles.Add(new UserRole { UserID = auditorUser.UserID, RoleID = auditorRole.RoleID, AssignedAt = DateTime.UtcNow });
            _logger.LogInformation("Auditor user seeded: auditor_demo / User@123456");

            await _context.SaveChangesAsync();

            _logger.LogInformation("All users seeded successfully:");
            _logger.LogInformation("  Admin:      admin_demo / User@123456");
            _logger.LogInformation("  Lawyer:     lawyer_demo / User@123456");
            _logger.LogInformation("  Staff:      staff_demo / User@123456 (Metadata Manager)");
            _logger.LogInformation("  Client:     client_demo / User@123456");
            _logger.LogInformation("  Auditor:    auditor_demo / User@123456");
            _logger.LogInformation("  Firm Code:  DEMO2024 (for client registration)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding Users: {Message}", ex.Message);
            throw;
        }
    }
}
