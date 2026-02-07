using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using System.Text;

namespace CKNDocument.Controllers.SuperAdmin;

/// <summary>
/// Dashboard controller for SuperAdmin
/// Displays platform analytics, law firms overview, revenue/expenses summary
/// Real-time data with tax separation and downloadable reports
/// </summary>
[Authorize(Policy = "SuperAdminOnly")]
public class SuperAdminDashboardController : Controller
{
    private readonly LawFirmDMSDbContext _context;

    public SuperAdminDashboardController(LawFirmDMSDbContext context)
    {
        _context = context;
    }

    public IActionResult Index()
    {
        return View("~/Views/SuperAdmin/Dashboard.cshtml");
    }

    /// <summary>
    /// API: Get real-time dashboard statistics
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetDashboardStats()
    {
        var now = DateTime.Now;
        var startOfMonth = new DateTime(now.Year, now.Month, 1);
        var startOfYear = new DateTime(now.Year, 1, 1);

        // Firm stats
        var totalFirms = await _context.Firms.CountAsync();
        var activeFirms = await _context.Firms.CountAsync(f => f.Status == "Active");

        // Subscription stats
        var activeSubscriptions = await _context.FirmSubscriptions.CountAsync(s => s.Status == "Active");

        // Invoice stats
        var pendingInvoices = await _context.Invoices.CountAsync(i => i.Status == "Pending" || i.Status == "Overdue");
        var overdueInvoices = await _context.Invoices.CountAsync(i => i.Status == "Overdue");

        // Revenue - this month
        var revenueThisMonth = await _context.Revenues
            .Where(r => r.RevenueDate >= startOfMonth)
            .SumAsync(r => r.GrossAmount ?? r.Amount ?? 0);

        var taxThisMonth = await _context.Revenues
            .Where(r => r.RevenueDate >= startOfMonth)
            .SumAsync(r => r.TaxAmount ?? 0);

        var netRevenueThisMonth = revenueThisMonth - taxThisMonth;

        // Revenue - this year
        var revenueThisYear = await _context.Revenues
            .Where(r => r.RevenueDate >= startOfYear)
            .SumAsync(r => r.GrossAmount ?? r.Amount ?? 0);

        var taxThisYear = await _context.Revenues
            .Where(r => r.RevenueDate >= startOfYear)
            .SumAsync(r => r.TaxAmount ?? 0);

        var netRevenueThisYear = revenueThisYear - taxThisYear;

        // Expenses - this month
        var expensesThisMonth = await _context.Expenses
            .Where(e => e.ExpenseDate >= startOfMonth && e.Status != "Rejected")
            .SumAsync(e => e.Amount ?? 0);

        // Expenses - this year
        var expensesThisYear = await _context.Expenses
            .Where(e => e.ExpenseDate >= startOfYear && e.Status != "Rejected")
            .SumAsync(e => e.Amount ?? 0);

        // Payments today
        var paymentsToday = await _context.Payments
            .Where(p => p.PaymentDate == DateTime.Today && p.Status == "Completed")
            .SumAsync(p => p.Amount ?? 0);

        var paymentCountToday = await _context.Payments
            .CountAsync(p => p.PaymentDate == DateTime.Today && p.Status == "Completed");

        // Profit
        var profitThisMonth = netRevenueThisMonth - expensesThisMonth;
        var profitThisYear = netRevenueThisYear - expensesThisYear;

        return Json(new
        {
            firms = new { total = totalFirms, active = activeFirms },
            subscriptions = new { active = activeSubscriptions },
            invoices = new { pending = pendingInvoices, overdue = overdueInvoices },
            revenue = new
            {
                thisMonth = new { gross = revenueThisMonth, tax = taxThisMonth, net = netRevenueThisMonth },
                thisYear = new { gross = revenueThisYear, tax = taxThisYear, net = netRevenueThisYear }
            },
            expenses = new { thisMonth = expensesThisMonth, thisYear = expensesThisYear },
            profit = new { thisMonth = profitThisMonth, thisYear = profitThisYear },
            payments = new { today = paymentsToday, countToday = paymentCountToday }
        });
    }

    /// <summary>
    /// API: Get monthly revenue vs expenses chart data (last 12 months)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMonthlyChartData()
    {
        var months = new List<object>();
        for (int i = 11; i >= 0; i--)
        {
            var date = DateTime.Now.AddMonths(-i);
            var start = new DateTime(date.Year, date.Month, 1);
            var end = start.AddMonths(1);

            var grossRevenue = await _context.Revenues
                .Where(r => r.RevenueDate >= start && r.RevenueDate < end)
                .SumAsync(r => r.GrossAmount ?? r.Amount ?? 0);

            var taxAmount = await _context.Revenues
                .Where(r => r.RevenueDate >= start && r.RevenueDate < end)
                .SumAsync(r => r.TaxAmount ?? 0);

            var expenses = await _context.Expenses
                .Where(e => e.ExpenseDate >= start && e.ExpenseDate < end && e.Status != "Rejected")
                .SumAsync(e => e.Amount ?? 0);

            months.Add(new
            {
                month = start.ToString("MMM yyyy"),
                grossRevenue,
                taxAmount,
                netRevenue = grossRevenue - taxAmount,
                expenses,
                profit = (grossRevenue - taxAmount) - expenses
            });
        }

        return Json(months);
    }

    /// <summary>
    /// API: Get recent payments list
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetRecentPayments(int take = 10)
    {
        var payments = await _context.Payments
            .Include(p => p.Subscription)
                .ThenInclude(s => s!.Firm)
            .Include(p => p.Invoice)
            .Where(p => p.Status == "Completed")
            .OrderByDescending(p => p.PaymentDate)
            .ThenByDescending(p => p.CreatedAt)
            .Take(take)
            .Select(p => new
            {
                p.PaymentID,
                p.PaymentReference,
                firmName = p.Subscription != null && p.Subscription.Firm != null ? p.Subscription.Firm.FirmName : "N/A",
                invoiceNumber = p.Invoice != null ? p.Invoice.InvoiceNumber : "N/A",
                amount = p.Amount ?? 0,
                taxAmount = p.TaxAmount ?? 0,
                netAmount = p.NetAmount ?? 0,
                method = p.PaymentMethod ?? "N/A",
                date = p.PaymentDate,
                status = p.Status
            })
            .ToListAsync();

        return Json(payments);
    }

    /// <summary>
    /// API: Get revenue breakdown by category
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetRevenueBreakdown(string period = "month")
    {
        var startDate = period switch
        {
            "week" => DateTime.Today.AddDays(-7),
            "month" => new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1),
            "quarter" => new DateTime(DateTime.Now.Year, ((DateTime.Now.Month - 1) / 3) * 3 + 1, 1),
            "year" => new DateTime(DateTime.Now.Year, 1, 1),
            "all" => DateTime.MinValue,
            _ => new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1)
        };

        var breakdown = await _context.Revenues
            .Where(r => r.RevenueDate >= startDate)
            .GroupBy(r => r.Category ?? "Other")
            .Select(g => new
            {
                category = g.Key,
                grossAmount = g.Sum(r => r.GrossAmount ?? r.Amount ?? 0),
                taxAmount = g.Sum(r => r.TaxAmount ?? 0),
                netAmount = g.Sum(r => (r.GrossAmount ?? r.Amount ?? 0) - (r.TaxAmount ?? 0)),
                count = g.Count()
            })
            .ToListAsync();

        return Json(breakdown);
    }

    /// <summary>
    /// API: Get expense breakdown by category
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetExpenseBreakdown(string period = "month")
    {
        var startDate = period switch
        {
            "week" => DateTime.Today.AddDays(-7),
            "month" => new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1),
            "quarter" => new DateTime(DateTime.Now.Year, ((DateTime.Now.Month - 1) / 3) * 3 + 1, 1),
            "year" => new DateTime(DateTime.Now.Year, 1, 1),
            "all" => DateTime.MinValue,
            _ => new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1)
        };

        var breakdown = await _context.Expenses
            .Where(e => e.ExpenseDate >= startDate && e.Status != "Rejected")
            .GroupBy(e => e.Category ?? "Other")
            .Select(g => new
            {
                category = g.Key,
                amount = g.Sum(e => e.Amount ?? 0),
                count = g.Count()
            })
            .ToListAsync();

        return Json(breakdown);
    }

    /// <summary>
    /// API: Get firms with subscription status for dashboard table
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetFirmsSummary()
    {
        var firms = await _context.Firms
            .Include(f => f.Subscriptions)
            .OrderByDescending(f => f.CreatedAt)
            .Take(20)
            .Select(f => new
            {
                f.FirmID,
                f.FirmName,
                f.ContactEmail,
                f.Status,
                subscription = f.Subscriptions
                    .Where(s => s.Status == "Active")
                    .OrderByDescending(s => s.CreatedAt)
                    .Select(s => new { s.PlanType, s.EndDate, s.Status })
                    .FirstOrDefault(),
                createdAt = f.CreatedAt
            })
            .ToListAsync();

        return Json(firms);
    }

    /// <summary>
    /// Download comprehensive revenue report as CSV
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> DownloadReport(string type = "revenue", string period = "month", string sortBy = "date")
    {
        var startDate = period switch
        {
            "week" => DateTime.Today.AddDays(-7),
            "month" => new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1),
            "quarter" => new DateTime(DateTime.Now.Year, ((DateTime.Now.Month - 1) / 3) * 3 + 1, 1),
            "year" => new DateTime(DateTime.Now.Year, 1, 1),
            "all" => DateTime.MinValue,
            _ => new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1)
        };

        var csv = new StringBuilder();

        if (type == "revenue" || type == "full")
        {
            csv.AppendLine("=== REVENUE REPORT ===");
            csv.AppendLine($"Period: {(period == "all" ? "All Time" : $"{startDate:MMM dd, yyyy} - {DateTime.Now:MMM dd, yyyy}")}");
            csv.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            csv.AppendLine();
            csv.AppendLine("Date,Source,Category,Firm,Invoice,Gross Amount,Tax (VAT 12%),Net Amount");

            var revenuesQuery = _context.Revenues
                .Include(r => r.Subscription).ThenInclude(s => s!.Firm)
                .Include(r => r.Payment).ThenInclude(p => p!.Invoice)
                .Where(r => r.RevenueDate >= startDate);

            var revenues = sortBy switch
            {
                "amount" => await revenuesQuery.OrderByDescending(r => r.GrossAmount ?? r.Amount ?? 0).ToListAsync(),
                "firm" => await revenuesQuery.OrderBy(r => r.Subscription != null && r.Subscription.Firm != null ? r.Subscription.Firm.FirmName : "").ToListAsync(),
                "category" => await revenuesQuery.OrderBy(r => r.Category).ToListAsync(),
                _ => await revenuesQuery.OrderByDescending(r => r.RevenueDate).ToListAsync()
            };

            decimal totalGross = 0, totalTax = 0, totalNet = 0;
            foreach (var r in revenues)
            {
                var gross = r.GrossAmount ?? r.Amount ?? 0;
                var tax = r.TaxAmount ?? 0;
                var net = gross - tax;
                totalGross += gross;
                totalTax += tax;
                totalNet += net;

                csv.AppendLine($"{r.RevenueDate:yyyy-MM-dd}," +
                    $"\"{r.Source ?? "N/A"}\"," +
                    $"\"{r.Category ?? "N/A"}\"," +
                    $"\"{r.Subscription?.Firm?.FirmName ?? "N/A"}\"," +
                    $"\"{r.Payment?.Invoice?.InvoiceNumber ?? "N/A"}\"," +
                    $"{gross:F2},{tax:F2},{net:F2}");
            }

            csv.AppendLine();
            csv.AppendLine($"TOTALS,,,,,{totalGross:F2},{totalTax:F2},{totalNet:F2}");
            csv.AppendLine();
        }

        if (type == "expenses" || type == "full")
        {
            csv.AppendLine("=== EXPENSE REPORT ===");
            csv.AppendLine($"Period: {(period == "all" ? "All Time" : $"{startDate:MMM dd, yyyy} - {DateTime.Now:MMM dd, yyyy}")}");
            csv.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            csv.AppendLine();
            csv.AppendLine("Date,Category,Description,Amount,Status");

            var expensesQuery = _context.Expenses
                .Where(e => e.ExpenseDate >= startDate && e.Status != "Rejected");

            var expenses = sortBy switch
            {
                "amount" => await expensesQuery.OrderByDescending(e => e.Amount ?? 0).ToListAsync(),
                "category" => await expensesQuery.OrderBy(e => e.Category).ToListAsync(),
                _ => await expensesQuery.OrderByDescending(e => e.ExpenseDate).ToListAsync()
            };

            decimal totalExpenses = 0;
            foreach (var e in expenses)
            {
                totalExpenses += e.Amount ?? 0;
                csv.AppendLine($"{e.ExpenseDate:yyyy-MM-dd}," +
                    $"\"{e.Category ?? "N/A"}\"," +
                    $"\"{e.Description ?? "N/A"}\"," +
                    $"{(e.Amount ?? 0):F2}," +
                    $"\"{e.Status ?? "N/A"}\"");
            }

            csv.AppendLine();
            csv.AppendLine($"TOTAL EXPENSES,,,{totalExpenses:F2},");
            csv.AppendLine();
        }

        if (type == "full")
        {
            // Summary section
            var totalRev = await _context.Revenues.Where(r => r.RevenueDate >= startDate).SumAsync(r => r.GrossAmount ?? r.Amount ?? 0);
            var totalTaxRev = await _context.Revenues.Where(r => r.RevenueDate >= startDate).SumAsync(r => r.TaxAmount ?? 0);
            var totalExp = await _context.Expenses.Where(e => e.ExpenseDate >= startDate && e.Status != "Rejected").SumAsync(e => e.Amount ?? 0);

            csv.AppendLine("=== PROFIT & LOSS SUMMARY ===");
            csv.AppendLine($"Gross Revenue,{totalRev:F2}");
            csv.AppendLine($"Tax Collected (VAT),{totalTaxRev:F2}");
            csv.AppendLine($"Net Revenue,{(totalRev - totalTaxRev):F2}");
            csv.AppendLine($"Total Expenses,{totalExp:F2}");
            csv.AppendLine($"Net Profit,{((totalRev - totalTaxRev) - totalExp):F2}");
        }

        var fileName = $"CKN_{type}_report_{period}_{DateTime.Now:yyyyMMdd}.csv";
        var bytes = Encoding.UTF8.GetBytes(csv.ToString());
        return File(bytes, "text/csv", fileName);
    }
}
