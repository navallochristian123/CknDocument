using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Services;
using System.Security.Claims;

namespace CKNDocument.Controllers.LawFirm;

/// <summary>
/// Audit log controller for all roles
/// Displays system audit trail with filtering and export capabilities
/// Admin/Auditor see all logs, other roles see their own logs and related documents
/// </summary>
[Authorize(Roles = "Admin,Auditor,Staff,Lawyer,Client")]
public class AuditController : Controller
{
    private readonly LawFirmDMSDbContext _context;
    private readonly AuditLogService _auditLogService;
    private readonly ILogger<AuditController> _logger;

    public AuditController(
        LawFirmDMSDbContext context,
        AuditLogService auditLogService,
        ILogger<AuditController> logger)
    {
        _context = context;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    private int GetCurrentFirmId()
    {
        var firmIdClaim = User.FindFirst("FirmId")?.Value;
        return int.TryParse(firmIdClaim, out int firmId) ? firmId : 0;
    }

    private string GetRoleViewPath(string viewName)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Admin";
        return $"~/Views/{role}/{viewName}.cshtml";
    }

    /// <summary>
    /// Display audit logs page
    /// Admin/Auditor see all logs, other roles see only their own logs
    /// </summary>
    public async Task<IActionResult> Index(
        string? action = null,
        string? category = null,
        int? userId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int page = 1,
        int pageSize = 20)
    {
        var firmId = GetCurrentFirmId();
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Client";
        var currentUserId = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int uid) ? uid : 0;
        
        // Non-admin roles only see their own audit logs
        int? filterUserId = userId;
        if (role != "Admin" && role != "Auditor")
        {
            filterUserId = currentUserId;
        }

        var (logs, totalCount) = await _auditLogService.GetAuditLogsAsync(
            firmId: firmId,
            userId: filterUserId,
            action: action,
            actionCategory: category,
            startDate: startDate,
            endDate: endDate,
            page: page,
            pageSize: pageSize);

        // Get filter options - only for Admin/Auditor
        if (role == "Admin" || role == "Auditor")
        {
            ViewData["Users"] = await _context.Users
                .Where(u => u.FirmID == firmId)
                .Select(u => new { u.UserID, Name = u.FirstName + " " + u.LastName })
                .ToListAsync();
        }
        else
        {
            ViewData["Users"] = null;
        }

        ViewData["Actions"] = new[]
        {
            "Login", "Logout", "LoginFailed", "Registration",
            "AccountCreated", "AccountStatusChanged", "PasswordChanged", "RoleAssigned",
            "DocumentCreated", "DocumentUpdated", "DocumentDeleted", "DocumentViewed",
            "DocumentApproved", "DocumentRejected", "DocumentForwarded", "ReviewSubmitted"
        };

        ViewData["Categories"] = new[] { "Authentication", "UserManagement", "DocumentManagement", "Review", "General" };
        ViewData["CurrentRole"] = role;

        ViewData["TotalCount"] = totalCount;
        ViewData["CurrentPage"] = page;
        ViewData["PageSize"] = pageSize;
        ViewData["TotalPages"] = (int)Math.Ceiling((double)totalCount / pageSize);

        // Current filter values
        ViewData["FilterAction"] = action;
        ViewData["FilterCategory"] = category;
        ViewData["FilterUserId"] = userId;
        ViewData["FilterStartDate"] = startDate?.ToString("yyyy-MM-dd");
        ViewData["FilterEndDate"] = endDate?.ToString("yyyy-MM-dd");

        return View(GetRoleViewPath("AuditLogs"), logs);
    }

    /// <summary>
    /// View audit log details
    /// </summary>
    public async Task<IActionResult> Details(int id)
    {
        var firmId = GetCurrentFirmId();

        var log = await _context.AuditLogs
            .Include(a => a.User)
            .Include(a => a.SuperAdmin)
            .Include(a => a.Firm)
            .FirstOrDefaultAsync(a => a.AuditID == id && (a.FirmID == firmId || a.FirmID == null));

        if (log == null)
        {
            TempData["ToastType"] = "error";
            TempData["ToastMessage"] = "Audit log not found.";
            return RedirectToAction("Index");
        }

        return View(GetRoleViewPath("AuditLogDetails"), log);
    }

    /// <summary>
    /// View audit logs for a specific user
    /// </summary>
    public async Task<IActionResult> UserActivity(int userId, int page = 1, int pageSize = 20)
    {
        var firmId = GetCurrentFirmId();

        // Verify user belongs to this firm
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.UserID == userId && u.FirmID == firmId);

        if (user == null)
        {
            TempData["ToastType"] = "error";
            TempData["ToastMessage"] = "User not found.";
            return RedirectToAction("Index");
        }

        var (logs, totalCount) = await _auditLogService.GetAuditLogsAsync(
            firmId: firmId,
            userId: userId,
            page: page,
            pageSize: pageSize);

        ViewData["User"] = user;
        ViewData["TotalCount"] = totalCount;
        ViewData["CurrentPage"] = page;
        ViewData["PageSize"] = pageSize;
        ViewData["TotalPages"] = (int)Math.Ceiling((double)totalCount / pageSize);

        return View(GetRoleViewPath("UserAuditLogs"), logs);
    }

    /// <summary>
    /// View audit logs for a specific document
    /// </summary>
    public async Task<IActionResult> DocumentActivity(int documentId, int page = 1, int pageSize = 20)
    {
        var firmId = GetCurrentFirmId();

        // Verify document belongs to this firm
        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.FirmID == firmId);

        if (document == null)
        {
            TempData["ToastType"] = "error";
            TempData["ToastMessage"] = "Document not found.";
            return RedirectToAction("Index");
        }

        var query = _context.AuditLogs
            .Include(a => a.User)
            .Where(a => a.EntityType == "Document" && a.EntityID == documentId)
            .OrderByDescending(a => a.Timestamp);

        var totalCount = await query.CountAsync();
        var logs = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewData["Document"] = document;
        ViewData["TotalCount"] = totalCount;
        ViewData["CurrentPage"] = page;
        ViewData["PageSize"] = pageSize;
        ViewData["TotalPages"] = (int)Math.Ceiling((double)totalCount / pageSize);

        return View(GetRoleViewPath("DocumentAuditLogs"), logs);
    }

    /// <summary>
    /// API: Get audit logs (for AJAX)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetLogs(
        string? action = null,
        string? category = null,
        int? userId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int page = 1,
        int pageSize = 20)
    {
        var firmId = GetCurrentFirmId();

        var (logs, totalCount) = await _auditLogService.GetAuditLogsAsync(
            firmId: firmId,
            userId: userId,
            action: action,
            actionCategory: category,
            startDate: startDate,
            endDate: endDate,
            page: page,
            pageSize: pageSize);

        return Json(new
        {
            data = logs.Select(l => new
            {
                l.AuditID,
                l.Action,
                l.ActionCategory,
                l.EntityType,
                l.EntityID,
                l.Description,
                l.IPAddress,
                Timestamp = l.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                UserName = l.User?.FullName ?? l.SuperAdmin?.FullName ?? "System"
            }),
            totalCount,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        });
    }

    /// <summary>
    /// Export audit logs to CSV
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Export(
        string? action = null,
        string? category = null,
        int? userId = null,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var firmId = GetCurrentFirmId();

        // Limit date range to 90 days
        if (startDate.HasValue && endDate.HasValue)
        {
            var diff = (endDate.Value - startDate.Value).TotalDays;
            if (diff > 90)
            {
                startDate = endDate.Value.AddDays(-90);
            }
        }

        var query = _context.AuditLogs
            .Include(a => a.User)
            .Include(a => a.SuperAdmin)
            .Where(a => a.FirmID == firmId || a.FirmID == null);

        if (!string.IsNullOrEmpty(action))
            query = query.Where(a => a.Action == action);

        if (!string.IsNullOrEmpty(category))
            query = query.Where(a => a.ActionCategory == category);

        if (userId.HasValue)
            query = query.Where(a => a.UserID == userId);

        if (startDate.HasValue)
            query = query.Where(a => a.Timestamp >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(a => a.Timestamp <= endDate.Value.AddDays(1));

        var logs = await query
            .OrderByDescending(a => a.Timestamp)
            .Take(10000) // Limit export to 10000 records
            .ToListAsync();

        // Generate CSV
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Timestamp,User,Action,Category,Entity Type,Entity ID,Description,IP Address,User Agent");

        foreach (var log in logs)
        {
            var userName = log.User?.FullName ?? log.SuperAdmin?.FullName ?? "System";
            csv.AppendLine($"\"{log.Timestamp:yyyy-MM-dd HH:mm:ss}\",\"{userName}\",\"{log.Action}\",\"{log.ActionCategory}\",\"{log.EntityType}\",\"{log.EntityID}\",\"{log.Description?.Replace("\"", "\"\"")}\",\"{log.IPAddress}\",\"{log.UserAgent?.Replace("\"", "\"\"")}\"");
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
        return File(bytes, "text/csv", $"audit_logs_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
    }

    /// <summary>
    /// Export audit logs to Excel format (CSV with Excel-compatible encoding)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ExportExcel(
        string? action = null,
        string? category = null,
        int? userId = null,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var firmId = GetCurrentFirmId();

        // Limit date range to 90 days
        if (startDate.HasValue && endDate.HasValue)
        {
            var diff = (endDate.Value - startDate.Value).TotalDays;
            if (diff > 90)
            {
                startDate = endDate.Value.AddDays(-90);
            }
        }

        var query = _context.AuditLogs
            .Include(a => a.User)
            .Include(a => a.SuperAdmin)
            .Where(a => a.FirmID == firmId || a.FirmID == null);

        if (!string.IsNullOrEmpty(action))
            query = query.Where(a => a.Action == action);

        if (!string.IsNullOrEmpty(category))
            query = query.Where(a => a.ActionCategory == category);

        if (userId.HasValue)
            query = query.Where(a => a.UserID == userId);

        if (startDate.HasValue)
            query = query.Where(a => a.Timestamp >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(a => a.Timestamp <= endDate.Value.AddDays(1));

        var logs = await query
            .OrderByDescending(a => a.Timestamp)
            .Take(10000)
            .ToListAsync();

        // Generate Excel-compatible HTML table
        var html = new System.Text.StringBuilder();
        html.AppendLine("<html xmlns:x=\"urn:schemas-microsoft-com:office:excel\">");
        html.AppendLine("<head><meta charset=\"utf-8\"></head>");
        html.AppendLine("<body><table border=\"1\">");
        html.AppendLine("<tr><th>Timestamp</th><th>User</th><th>Action</th><th>Category</th><th>Entity Type</th><th>Entity ID</th><th>Description</th><th>IP Address</th><th>Old Values</th><th>New Values</th></tr>");

        foreach (var log in logs)
        {
            var userName = log.User?.FullName ?? log.SuperAdmin?.FullName ?? "System";
            html.AppendLine($"<tr><td>{log.Timestamp:yyyy-MM-dd HH:mm:ss}</td><td>{System.Net.WebUtility.HtmlEncode(userName)}</td><td>{log.Action}</td><td>{log.ActionCategory}</td><td>{log.EntityType}</td><td>{log.EntityID}</td><td>{System.Net.WebUtility.HtmlEncode(log.Description ?? "")}</td><td>{log.IPAddress}</td><td>{System.Net.WebUtility.HtmlEncode(log.OldValues ?? "")}</td><td>{System.Net.WebUtility.HtmlEncode(log.NewValues ?? "")}</td></tr>");
        }

        html.AppendLine("</table></body></html>");

        var bytes = System.Text.Encoding.UTF8.GetBytes(html.ToString());
        return File(bytes, "application/vnd.ms-excel", $"audit_logs_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xls");
    }

    /// <summary>
    /// Export audit logs to PDF format (HTML for print)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ExportPdf(
        string? action = null,
        string? category = null,
        int? userId = null,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var firmId = GetCurrentFirmId();

        // Limit date range to 90 days
        if (startDate.HasValue && endDate.HasValue)
        {
            var diff = (endDate.Value - startDate.Value).TotalDays;
            if (diff > 90)
            {
                startDate = endDate.Value.AddDays(-90);
            }
        }

        var query = _context.AuditLogs
            .Include(a => a.User)
            .Include(a => a.SuperAdmin)
            .Where(a => a.FirmID == firmId || a.FirmID == null);

        if (!string.IsNullOrEmpty(action))
            query = query.Where(a => a.Action == action);

        if (!string.IsNullOrEmpty(category))
            query = query.Where(a => a.ActionCategory == category);

        if (userId.HasValue)
            query = query.Where(a => a.UserID == userId);

        if (startDate.HasValue)
            query = query.Where(a => a.Timestamp >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(a => a.Timestamp <= endDate.Value.AddDays(1));

        var logs = await query
            .OrderByDescending(a => a.Timestamp)
            .Take(1000) // Limit PDF to 1000 records for performance
            .ToListAsync();

        // Generate printable HTML
        var html = new System.Text.StringBuilder();
        html.AppendLine(@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>Audit Logs Report</title>
    <style>
        body { font-family: Arial, sans-serif; font-size: 10px; margin: 20px; }
        h1 { text-align: center; margin-bottom: 5px; }
        .subtitle { text-align: center; color: #666; margin-bottom: 20px; }
        table { width: 100%; border-collapse: collapse; }
        th, td { border: 1px solid #ddd; padding: 6px; text-align: left; }
        th { background-color: #f5f5f5; font-weight: bold; }
        tr:nth-child(even) { background-color: #f9f9f9; }
        .footer { margin-top: 20px; text-align: center; color: #666; font-size: 9px; }
        @media print {
            @page { size: landscape; margin: 10mm; }
            body { margin: 0; }
        }
    </style>
</head>
<body>
    <h1>Audit Logs Report</h1>
    <p class=""subtitle"">Generated on " + DateTime.Now.ToString("MMMM dd, yyyy HH:mm:ss") + @"</p>
    <table>
        <thead>
            <tr>
                <th>Timestamp</th>
                <th>User</th>
                <th>Action</th>
                <th>Category</th>
                <th>Resource</th>
                <th>Description</th>
                <th>IP Address</th>
            </tr>
        </thead>
        <tbody>");

        foreach (var log in logs)
        {
            var userName = log.User?.FullName ?? log.SuperAdmin?.FullName ?? "System";
            html.AppendLine($@"<tr>
                <td>{log.Timestamp:yyyy-MM-dd HH:mm:ss}</td>
                <td>{System.Net.WebUtility.HtmlEncode(userName)}</td>
                <td>{log.Action}</td>
                <td>{log.ActionCategory ?? "-"}</td>
                <td>{log.EntityType ?? "-"}</td>
                <td>{System.Net.WebUtility.HtmlEncode(log.Description ?? "-")}</td>
                <td>{log.IPAddress ?? "-"}</td>
            </tr>");
        }

        html.AppendLine(@"</tbody>
    </table>
    <p class=""footer"">Total Records: " + logs.Count + @" | This report is confidential</p>
    <script>window.onload = function() { window.print(); }</script>
</body>
</html>");

        return Content(html.ToString(), "text/html", System.Text.Encoding.UTF8);
    }

    /// <summary>
    /// Get audit log statistics
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Statistics(DateTime? startDate = null, DateTime? endDate = null)
    {
        var firmId = GetCurrentFirmId();

        startDate ??= DateTime.UtcNow.AddDays(-30);
        endDate ??= DateTime.UtcNow;

        var query = _context.AuditLogs
            .Where(a => (a.FirmID == firmId || a.FirmID == null) && a.Timestamp >= startDate && a.Timestamp <= endDate);

        var stats = new
        {
            TotalLogs = await query.CountAsync(),
            LoginCount = await query.CountAsync(a => a.Action == "Login"),
            LogoutCount = await query.CountAsync(a => a.Action == "Logout"),
            FailedLogins = await query.CountAsync(a => a.Action == "LoginFailed"),
            AccountsCreated = await query.CountAsync(a => a.Action == "AccountCreated"),
            PasswordChanges = await query.CountAsync(a => a.Action == "PasswordChanged"),
            ByCategory = await query
                .GroupBy(a => a.ActionCategory)
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .ToListAsync(),
            ByDay = await query
                .GroupBy(a => a.Timestamp.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .OrderBy(g => g.Date)
                .ToListAsync()
        };

        return Json(stats);
    }
}
