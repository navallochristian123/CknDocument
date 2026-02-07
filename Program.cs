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
    options.AddPolicy("StaffOnly", policy => policy.RequireRole("Staff"));
    options.AddPolicy("ClientOnly", policy => policy.RequireRole("Client"));
    options.AddPolicy("AuditorOnly", policy => policy.RequireRole("Auditor"));

    // Combined policies
    options.AddPolicy("AdminOrStaff", policy => policy.RequireRole("Admin", "Staff"));
    options.AddPolicy("FirmMember", policy => policy.RequireRole("Admin", "Staff", "Client", "Auditor"));
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

// PayMongo Payment Service (API key from environment variable)
builder.Services.AddHttpClient<PayMongoService>();
builder.Services.AddScoped<PayMongoService>();

// Background Services
builder.Services.AddHostedService<RetentionArchiveBackgroundService>();

var app = builder.Build();

// ===========================================
// DATABASE SEEDING (Development only)
// ===========================================
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var seeder = scope.ServiceProvider.GetRequiredService<CKNDocument.Services.DatabaseSeeder>();
        try
        {
            await seeder.SeedAsync();
        }
        catch (Exception ex)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "An error occurred while seeding the database.");
        }
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
// ROUTE MAPPING
// ===========================================

// Default route (all roles use clean URLs)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();
