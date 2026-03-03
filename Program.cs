using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using CKNDocument.Data;
using CKNDocument.Services;

var builder = WebApplication.CreateBuilder(args);

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
                            || path.StartsWith("/images") || path.StartsWith("/_");

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

app.Run();
