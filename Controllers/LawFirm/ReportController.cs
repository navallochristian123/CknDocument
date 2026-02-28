using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using System.Security.Claims;

namespace CKNDocument.Controllers.LawFirm;

/// <summary>
/// Report controller for Admin and Auditor
/// Generates various reports with PDF data
/// </summary>
[Authorize(Roles = "Admin,Auditor")]
public class ReportController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public ReportController(LawFirmDMSDbContext context)
    {
        _context = context;
    }

    private int GetFirmId() => int.Parse(User.FindFirst("FirmID")?.Value ?? "0");
    private int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

    private string GetRoleViewPath(string viewName)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Admin";
        return $"~/Views/{role}/{viewName}.cshtml";
    }

    public IActionResult Index()
    {
        return View(GetRoleViewPath("Reports"));
    }

    /// <summary>
    /// Get document report data as JSON for PDF generation
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetDocumentReportData(string period = "month")
    {
        var firmId = GetFirmId();
        var startDate = period switch
        {
            "week" => DateTime.Today.AddDays(-7),
            "month" => new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1),
            "quarter" => new DateTime(DateTime.Now.Year, ((DateTime.Now.Month - 1) / 3) * 3 + 1, 1),
            "year" => new DateTime(DateTime.Now.Year, 1, 1),
            "all" => DateTime.MinValue,
            _ => new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1)
        };

        var documents = await _context.Documents
            .Include(d => d.Uploader)
            .Include(d => d.Folder)
            .Where(d => d.FirmID == firmId && d.CreatedAt >= startDate)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new
            {
                title = d.Title,
                documentType = d.DocumentType ?? "Unknown",
                status = d.Status ?? "Unknown",
                workflowStage = d.WorkflowStage ?? "Unknown",
                uploadedBy = d.Uploader != null ? (d.Uploader.FirstName ?? "") + " " + (d.Uploader.LastName ?? "") : "Unknown",
                folder = d.Folder != null ? d.Folder.FolderName : "Root",
                createdAt = d.CreatedAt,
                currentVersion = d.CurrentVersion
            })
            .ToListAsync();

        var totalDocs = documents.Count;
        var byStatus = documents.GroupBy(d => d.status).Select(g => new { status = g.Key, count = g.Count() });
        var byType = documents.GroupBy(d => d.documentType).Select(g => new { type = g.Key, count = g.Count() });

        return Json(new
        {
            period = period == "all" ? "All Time" : $"{startDate:MMM dd, yyyy} - {DateTime.Now:MMM dd, yyyy}",
            generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            totalDocuments = totalDocs,
            byStatus,
            byType,
            documents
        });
    }

    /// <summary>
    /// Get user activity report data as JSON for PDF generation
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetUserActivityReportData(string period = "month")
    {
        var firmId = GetFirmId();
        var startDate = period switch
        {
            "week" => DateTime.Today.AddDays(-7),
            "month" => new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1),
            "quarter" => new DateTime(DateTime.Now.Year, ((DateTime.Now.Month - 1) / 3) * 3 + 1, 1),
            "year" => new DateTime(DateTime.Now.Year, 1, 1),
            "all" => DateTime.MinValue,
            _ => new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1)
        };

        var firmUserIds = await _context.Users.Where(u => u.FirmID == firmId).Select(u => u.UserID).ToListAsync();

        var activities = await _context.AuditLogs
            .Include(a => a.User)
            .Where(a => (a.FirmID == firmId || (a.UserID != null && firmUserIds.Contains(a.UserID.Value))) && a.Timestamp >= startDate)
            .OrderByDescending(a => a.Timestamp)
            .Take(500)
            .Select(a => new
            {
                timestamp = a.Timestamp,
                user = a.User != null ? (a.User.FirstName ?? "") + " " + (a.User.LastName ?? "") : "System",
                action = a.Action ?? "Unknown",
                category = a.ActionCategory ?? "General",
                entityType = a.EntityType ?? "-",
                description = a.Description ?? "-"
            })
            .ToListAsync();

        var byUser = activities.GroupBy(a => a.user).Select(g => new { user = g.Key, count = g.Count() });
        var byAction = activities.GroupBy(a => a.action).Select(g => new { action = g.Key, count = g.Count() });

        return Json(new
        {
            period = period == "all" ? "All Time" : $"{startDate:MMM dd, yyyy} - {DateTime.Now:MMM dd, yyyy}",
            generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            totalActivities = activities.Count,
            byUser,
            byAction,
            activities
        });
    }

    /// <summary>
    /// Get compliance report data as JSON for PDF generation
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetComplianceReportData()
    {
        var firmId = GetFirmId();

        var retentions = await _context.DocumentRetentions
            .Include(r => r.Document)
            .Include(r => r.Policy)
            .Where(r => r.Document != null && r.Document.FirmID == firmId)
            .Select(r => new
            {
                documentTitle = r.Document!.Title,
                policyName = r.Policy != null ? r.Policy.PolicyName : "Default",
                retentionYears = r.RetentionYears,
                retentionMonths = r.RetentionMonths,
                retentionDays = r.RetentionDays,
                expiryDate = r.ExpiryDate,
                isArchived = r.IsArchived ?? false,
                isExpired = r.ExpiryDate != null && r.ExpiryDate <= DateTime.UtcNow
            })
            .ToListAsync();

        var archives = await _context.Archives
            .Include(a => a.Document)
            .Include(a => a.ArchivedByUser)
            .Where(a => a.Document != null && a.Document.FirmID == firmId && a.IsDeleted != true)
            .Select(a => new
            {
                documentTitle = a.Document!.Title,
                archiveType = a.ArchiveType ?? "Manual",
                archivedAt = a.ArchivedDate,
                archivedBy = a.ArchivedByUser != null ? (a.ArchivedByUser.FirstName ?? "") + " " + (a.ArchivedByUser.LastName ?? "") : "System",
                reason = a.Reason ?? "-",
                isRestored = a.IsRestored ?? false
            })
            .ToListAsync();

        return Json(new
        {
            generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            totalRetentionPolicies = retentions.Count,
            expiredRetentions = retentions.Count(r => r.isExpired),
            archivedRetentions = retentions.Count(r => r.isArchived),
            totalArchives = archives.Count,
            retentions,
            archives
        });
    }

    /// <summary>
    /// Get overall transactions report data as JSON for PDF generation
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetOverallTransactionsReportData(string period = "month")
    {
        var firmId = GetFirmId();
        var startDate = period switch
        {
            "week" => DateTime.Today.AddDays(-7),
            "month" => new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1),
            "quarter" => new DateTime(DateTime.Now.Year, ((DateTime.Now.Month - 1) / 3) * 3 + 1, 1),
            "year" => new DateTime(DateTime.Now.Year, 1, 1),
            "all" => DateTime.MinValue,
            _ => new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1)
        };

        // Document transactions
        var documents = await _context.Documents
            .Include(d => d.Uploader)
            .Where(d => d.FirmID == firmId && d.CreatedAt >= startDate)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new
            {
                title = d.Title,
                type = d.DocumentType ?? "Unknown",
                status = d.Status ?? "Unknown",
                uploadedBy = d.Uploader != null ? (d.Uploader.FirstName ?? "") + " " + (d.Uploader.LastName ?? "") : "Unknown",
                date = d.CreatedAt
            })
            .ToListAsync();

        // Reviews
        var reviews = await _context.DocumentReviews
            .Include(r => r.Document)
            .Include(r => r.Reviewer)
            .Where(r => r.Document != null && r.Document.FirmID == firmId && r.ReviewedAt >= startDate)
            .OrderByDescending(r => r.ReviewedAt)
            .Select(r => new
            {
                documentTitle = r.Document!.Title,
                reviewer = r.Reviewer != null ? (r.Reviewer.FirstName ?? "") + " " + (r.Reviewer.LastName ?? "") : "Unknown",
                role = r.ReviewerRole ?? "Unknown",
                status = r.ReviewStatus ?? "Unknown",
                remarks = r.Remarks ?? "-",
                date = r.ReviewedAt
            })
            .ToListAsync();

        // Payment transactions
        var payments = await _context.Payments
            .Include(p => p.Invoice)
            .Where(p => p.Invoice != null && p.Invoice.Subscription != null && p.Invoice.Subscription.FirmID == firmId && p.PaymentDate >= startDate)
            .OrderByDescending(p => p.PaymentDate)
            .Select(p => new
            {
                reference = p.PaymentReference ?? "N/A",
                invoiceNumber = p.Invoice != null ? p.Invoice.InvoiceNumber : "N/A",
                amount = p.Amount ?? 0,
                taxAmount = p.TaxAmount ?? 0,
                netAmount = p.NetAmount ?? 0,
                method = p.PaymentMethod ?? "N/A",
                status = p.Status ?? "Unknown",
                date = p.PaymentDate
            })
            .ToListAsync();

        return Json(new
        {
            period = period == "all" ? "All Time" : $"{startDate:MMM dd, yyyy} - {DateTime.Now:MMM dd, yyyy}",
            generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            documentSummary = new
            {
                total = documents.Count,
                approved = documents.Count(d => d.status == "Approved" || d.status == "Completed"),
                pending = documents.Count(d => d.status == "Pending" || d.status == "UnderReview"),
                rejected = documents.Count(d => d.status == "Rejected")
            },
            reviewSummary = new
            {
                total = reviews.Count,
                approved = reviews.Count(r => r.status == "Approved"),
                rejected = reviews.Count(r => r.status == "Rejected")
            },
            paymentSummary = new
            {
                total = payments.Count,
                totalAmount = payments.Sum(p => p.amount),
                totalTax = payments.Sum(p => p.taxAmount)
            },
            documents,
            reviews,
            payments
        });
    }

    public IActionResult DocumentReport()
    {
        return View(GetRoleViewPath("DocumentReport"));
    }

    public IActionResult UserActivityReport()
    {
        return View(GetRoleViewPath("UserActivityReport"));
    }

    public IActionResult ComplianceReport()
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "Admin";
        if (role == "Auditor")
            return View("~/Views/Auditor/Compliance.cshtml");
        return View(GetRoleViewPath("ComplianceReport"));
    }

    public IActionResult RetentionReport()
    {
        return View(GetRoleViewPath("RetentionReport"));
    }

    public IActionResult Generate(string reportType)
    {
        return View(GetRoleViewPath("GenerateReport"));
    }

    public IActionResult Download(int id)
    {
        return View(GetRoleViewPath("DownloadReport"));
    }

    public IActionResult History()
    {
        return View(GetRoleViewPath("ReportHistory"));
    }
}
