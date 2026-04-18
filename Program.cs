using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using DotNetEnv;
using System.Text;
using CKNDocument.Data;
using CKNDocument.Services;
using CKNDocument.Hubs;

var builder = WebApplication.CreateBuilder(args);

// ===========================================
// ENVIRONMENT VARIABLES (.env support + OS env fallback)
// ===========================================
var envPath = Path.Combine(builder.Environment.ContentRootPath, ".env");
if (File.Exists(envPath))
{
    Env.Load(envPath);
}

if (builder.Environment.IsDevelopment())
{
    var envDevPath = Path.Combine(builder.Environment.ContentRootPath, ".env.development");
    if (File.Exists(envDevPath))
    {
        Env.Load(envDevPath);
    }
}

// Refresh configuration after loading .env into process environment.
builder.Configuration.AddEnvironmentVariables();

// ===========================================
// DATABASE CONTEXT - Single Unified Database
// ===========================================

// LawFirmDMS Database (Unified - includes all entities)
builder.Services.AddDbContext<LawFirmDMSDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ===========================================
// AUTHENTICATION - Cookie + JWT
// ===========================================

// JWT Configuration
var jwtSettings = builder.Configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? throw new InvalidOperationException("JWT Key not configured"));

// Use Cookie as default for MVC, JWT for API
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "CookieAuth";
    options.DefaultChallengeScheme = "CookieAuth";
    options.DefaultAuthenticateScheme = "CookieAuth";
})
.AddCookie("CookieAuth", options =>
{
    options.LoginPath = "/Auth/Login";
    options.LogoutPath = "/Auth/Logout";
    options.AccessDeniedPath = "/Auth/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
})
.AddJwtBearer("JwtBearer", options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidateAudience = true,
        ValidAudience = jwtSettings["Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

// ===========================================
// AUTHORIZATION - Role-based Policies
// ===========================================

builder.Services.AddAuthorization(options =>
{
    // Platform-level policies
    options.AddPolicy("SuperAdminOnly", policy => policy.RequireRole("SuperAdmin"));

    // Law Firm-level policies
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("LawyerOnly", policy => policy.RequireRole("Lawyer"));
    options.AddPolicy("StaffOnly", policy => policy.RequireRole("Staff"));
    options.AddPolicy("ClientOnly", policy => policy.RequireRole("Client"));
    options.AddPolicy("AuditorOnly", policy => policy.RequireRole("Auditor"));

    // Combined policies
    options.AddPolicy("AdminOrLawyer", policy => policy.RequireRole("Admin", "Lawyer"));
    options.AddPolicy("AdminOrStaff", policy => policy.RequireRole("Admin", "Lawyer", "Staff"));
    options.AddPolicy("LawyerOrStaff", policy => policy.RequireRole("Lawyer", "Staff"));
    options.AddPolicy("FirmMember", policy => policy.RequireRole("Admin", "Lawyer", "Staff", "Client", "Auditor"));
    
    // Content editing - only Lawyer can edit document content
    options.AddPolicy("CanEditContent", policy => policy.RequireRole("Admin", "Lawyer"));
    // Metadata editing - Staff can edit metadata
    options.AddPolicy("CanEditMetadata", policy => policy.RequireRole("Admin", "Lawyer", "Staff"));
});

// ===========================================
// MVC CONFIGURATION
// ===========================================

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// Session support
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// HttpContext accessor for getting current user
builder.Services.AddHttpContextAccessor();

// Services
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<DocumentWorkflowService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<DocumentAIService>();

// HttpClient for OpenAI API
builder.Services.AddHttpClient("OpenAI");

// Google reCAPTCHA verification service
builder.Services.AddHttpClient<ReCaptchaService>();
builder.Services.AddScoped<ReCaptchaService>();

// PayMongo Payment Service (API key from environment variable)
builder.Services.AddHttpClient<PayMongoService>();
builder.Services.AddScoped<PayMongoService>();

// Chat Service
builder.Services.AddScoped<ChatService>();

// SignalR for real-time chat
builder.Services.AddSignalR();

// Background Services
builder.Services.AddHostedService<RetentionArchiveBackgroundService>();
builder.Services.AddHostedService<SubscriptionExpiryBackgroundService>();

var app = builder.Build();

// ===========================================
// DATABASE INITIALIZATION (All environments)
// ===========================================
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<CKNDocument.Services.DatabaseSeeder>();
    try
    {
        await seeder.SeedAsync();

        // Auto-add MaxStorageMB column if missing
        var db = scope.ServiceProvider.GetRequiredService<CKNDocument.Data.LawFirmDMSDbContext>();
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Firm') AND name = 'MaxStorageMB')
                BEGIN
                    ALTER TABLE Firm ADD MaxStorageMB BIGINT NOT NULL DEFAULT 2048;
                END");
        }
        catch (Exception colEx)
        {
            var logger2 = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger2.LogWarning(colEx, "Could not auto-add MaxStorageMB column (may already exist).");
        }

        // Auto-add missing Audit_Log columns required for SuperAdmin audit logging
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Audit_Log') AND name = 'SuperAdminId')
                    ALTER TABLE [Audit_Log] ADD [SuperAdminId] INT NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Audit_Log') AND name = 'FirmID')
                    ALTER TABLE [Audit_Log] ADD [FirmID] INT NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Audit_Log') AND name = 'Description')
                    ALTER TABLE [Audit_Log] ADD [Description] NVARCHAR(1000) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Audit_Log') AND name = 'OldValues')
                    ALTER TABLE [Audit_Log] ADD [OldValues] NVARCHAR(2000) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Audit_Log') AND name = 'NewValues')
                    ALTER TABLE [Audit_Log] ADD [NewValues] NVARCHAR(2000) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Audit_Log') AND name = 'UserAgent')
                    ALTER TABLE [Audit_Log] ADD [UserAgent] NVARCHAR(500) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Audit_Log') AND name = 'ActionCategory')
                    ALTER TABLE [Audit_Log] ADD [ActionCategory] NVARCHAR(50) NULL;
            ");
        }
        catch (Exception auditColEx)
        {
            var logger2 = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger2.LogWarning(auditColEx, "Could not auto-add Audit_Log columns (may already exist).");
        }

        // Auto-create SuperAdminNotification table if missing
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SuperAdminNotification')
                BEGIN
                    CREATE TABLE [dbo].[SuperAdminNotification] (
                        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        [SuperAdminId] INT NOT NULL,
                        [Title] NVARCHAR(255) NOT NULL,
                        [Message] NVARCHAR(1000) NOT NULL,
                        [NotificationType] NVARCHAR(50) NOT NULL,
                        [ActionUrl] NVARCHAR(500) NULL,
                        [Icon] NVARCHAR(50) NULL,
                        [IsRead] BIT NOT NULL DEFAULT 0,
                        [ReadAt] DATETIME2 NULL,
                        [CreatedAt] DATETIME2 NULL DEFAULT GETDATE(),
                        [UpdatedAt] DATETIME2 NULL,
                        CONSTRAINT [FK_SuperAdminNotification_SuperAdmin] FOREIGN KEY ([SuperAdminId])
                            REFERENCES [dbo].[SuperAdmin]([SuperAdminId]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_SuperAdminNotification_SuperAdminId] ON [dbo].[SuperAdminNotification]([SuperAdminId]);
                    CREATE INDEX [IX_SuperAdminNotification_IsRead] ON [dbo].[SuperAdminNotification]([IsRead]);
                    CREATE INDEX [IX_SuperAdminNotification_CreatedAt] ON [dbo].[SuperAdminNotification]([CreatedAt] DESC);
                END");
        }
        catch (Exception notifEx)
        {
            var logger3 = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger3.LogWarning(notifEx, "Could not auto-create SuperAdminNotification table (may already exist).");
        }
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// ===========================================
// MIDDLEWARE: Block unpaid firms from accessing the system
// ===========================================
app.Use(async (context, next) =>
{
    var user = context.User;
    if (user.Identity?.IsAuthenticated == true)
    {
        var firmIdClaim = user.FindFirst("FirmId")?.Value;
        if (!string.IsNullOrEmpty(firmIdClaim) && int.TryParse(firmIdClaim, out var firmId))
        {
            var path = context.Request.Path.Value?.ToLower() ?? "";

            // Allow access to Auth controller actions, Home, static files, and signout
            var allowedPaths = new[]
            {
                "/auth/subscriptionpayment",
                "/auth/processsubscriptionpayment",
                "/auth/subscriptionpaymentsuccess",
                "/auth/subscriptionpaymentfailed",
                "/auth/checksubscriptionpaymentstatus",
                "/auth/logout",
                "/auth/login",
                "/home",
                "/"
            };

            var isAllowed = allowedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                            || path.StartsWith("/css") || path.StartsWith("/js") || path.StartsWith("/lib")
                            || path.StartsWith("/images") || path.StartsWith("/_")
                            || path.StartsWith("/chathub"); // Allow SignalR chat hub

            if (!isAllowed)
            {
                // Check firm status from database
                var dbContext = context.RequestServices.GetRequiredService<CKNDocument.Data.LawFirmDMSDbContext>();
                var firm = await dbContext.Firms.AsNoTracking().FirstOrDefaultAsync(f => f.FirmID == firmId);

                if (firm != null && firm.Status == "PendingPayment")
                {
                    // Find the pending subscription to redirect to payment page
                    var sub = await dbContext.FirmSubscriptions.AsNoTracking()
                        .Where(s => s.FirmID == firmId && s.Status == "PendingPayment")
                        .OrderByDescending(s => s.CreatedAt)
                        .FirstOrDefaultAsync();

                    var redirectUrl = sub != null
                        ? $"/Auth/SubscriptionPayment?subscriptionId={sub.SubscriptionID}"
                        : "/Auth/SubscriptionPayment";

                    context.Response.Redirect(redirectUrl);
                    return;
                }

                // Expired firm — allow access only to billing page for renewal
                if (firm != null && firm.Status == "Expired")
                {
                    var billingPaths = new[] { "/billing", "/lawfirm/billing" };
                    var isBillingPage = billingPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
                    if (!isBillingPage)
                    {
                        context.Response.Redirect("/Billing");
                        return;
                    }
                }
            }
        }
    }
    await next();
});

// ===========================================
// ROUTE MAPPING
// ===========================================

// Default route - landing page
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

// SignalR Hub for real-time chat
app.MapHub<ChatHub>("/chatHub");

app.Run();
