using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;

namespace CKNDocument.Controllers.SuperAdmin;

/// <summary>
/// Audit Log controller for SuperAdmin
/// Shows only SuperAdmin-related activity logs
/// </summary>
[Authorize(Policy = "SuperAdminOnly")]
public class SuperAdminAuditLogController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public SuperAdminAuditLogController(LawFirmDMSDbContext context)
    {
        _context = context;
    }

    public IActionResult Index()
    {
        return View("~/Views/SuperAdmin/AuditLog.cshtml");
    }

    /// <summary>
    /// Get audit logs filtered to SuperAdmin activities only
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAuditLogs(
        string? category = null,
        string? action = null,
        string? period = null,
        int page = 1,
        int pageSize = 25)
    {
        try
        {
            var query = _context.AuditLogs
                .Include(a => a.SuperAdmin)
                .Where(a => a.SuperAdminId != null)
                .AsQueryable();

            if (!string.IsNullOrEmpty(category) && category != "all")
                query = query.Where(a => a.ActionCategory == category);

            if (!string.IsNullOrEmpty(action) && action != "all")
                query = query.Where(a => a.Action == action);

            // Period filtering
            if (!string.IsNullOrEmpty(period) && period != "all")
            {
                var now = DateTime.UtcNow;
                query = period switch
                {
                    "today" => query.Where(a => a.Timestamp >= now.Date),
                    "week" => query.Where(a => a.Timestamp >= now.AddDays(-7)),
                    "month" => query.Where(a => a.Timestamp >= now.AddMonths(-1)),
                    "year" => query.Where(a => a.Timestamp >= now.AddYears(-1)),
                    _ => query
                };
            }

            var totalCount = await query.CountAsync();

            var logs = await query
                .OrderByDescending(a => a.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new
                {
                    a.AuditID,
                    adminName = a.SuperAdmin != null ? (a.SuperAdmin.FirstName + " " + a.SuperAdmin.LastName).Trim() : "System",
                    a.Action,
                    a.EntityType,
                    a.EntityID,
                    a.Description,
                    a.ActionCategory,
                    a.IPAddress,
                    a.Timestamp,
                    a.OldValues,
                    a.NewValues
                })
                .ToListAsync();

            return Json(new { logs, totalCount, page, pageSize, totalPages = (int)Math.Ceiling((double)totalCount / pageSize) });
        }
        catch (Exception ex)
        {
            return Json(new { logs = Array.Empty<object>(), totalCount = 0, page, pageSize, totalPages = 0, error = ex.Message });
        }
    }

    /// <summary>
    /// Get audit log stats for SuperAdmin
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAuditStats()
    {
        try
        {
            var now = DateTime.UtcNow;
            var superAdminLogs = _context.AuditLogs.Where(a => a.SuperAdminId != null);

            var totalLogs = await superAdminLogs.CountAsync();
            var todayCount = await superAdminLogs.CountAsync(a => a.Timestamp >= now.Date);
            var weekCount = await superAdminLogs.CountAsync(a => a.Timestamp >= now.AddDays(-7));

            var categoryBreakdown = await superAdminLogs
                .GroupBy(a => a.ActionCategory ?? "General")
                .Select(g => new { category = g.Key, count = g.Count() })
                .ToListAsync();

            return Json(new { totalLogs, todayCount, weekCount, categoryBreakdown });
        }
        catch (Exception ex)
        {
            return Json(new { totalLogs = 0, todayCount = 0, weekCount = 0, categoryBreakdown = Array.Empty<object>(), error = ex.Message });
        }
    }
}
