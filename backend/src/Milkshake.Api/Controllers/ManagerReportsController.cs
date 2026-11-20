using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Milkshake.Api.Contracts.Management;
using Milkshake.Api.Security;
using Milkshake.Application.Management;

namespace Milkshake.Api.Controllers;

/// <summary>
/// Management reports (M12): orders for a date or date range (AC-12.2), order volumes by weekday, month and year
/// (AC-12.3), and CSV exports of those and of the audit history (AC-12.6). Managers only, read-only, never cached,
/// rate-limited per manager (exports more strictly). Periods are business-local dates; audit periods are UTC instants.
/// </summary>
[ApiController]
[Route("api/manager/reports")]
[Authorize(Policy = AuthorizationPolicies.ManagerOnly)]
[EnableRateLimiting(RateLimitPolicies.Reports)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ManagerReportsController(IOrderReportService reports, IReportExportService exports) : ControllerBase
{
    /// <summary>
    /// Orders placed on a date (<c>from</c>) or between two dates inclusive (<c>from</c>, <c>to</c>; at most 366 days),
    /// one row per milkshake, newest first. Optional <c>status</c> (all, paid, unpaid, cancelled), <c>q</c> (flavour,
    /// consistency, topping, restaurant or reference), <c>page</c> and <c>pageSize</c> (1–50, default 25).
    /// </summary>
    /// <response code="200">The page and the totals of the whole filtered result.</response>
    /// <response code="400">An unknown or repeated parameter, or an invalid value.</response>
    /// <response code="429">Too many report requests; try again in a minute.</response>
    /// <response code="503">The business time zone setting is unusable.</response>
    [HttpGet("orders")]
    [ProducesResponseType<OrderReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OrderReportResponse>> Orders(CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        OrderReportQuery? query = OrderReportQueryParser.ParseOrders(QueryValues(), errors);
        if (query is null)
        {
            return Invalid(errors);
        }

        OrderReportPage? page = await reports.OrdersAsync(query, cancellationToken);
        return page is null ? Unavailable() : Ok(OrderReportResponse.Create(query, page));
    }

    /// <summary>
    /// Order volumes by weekday (Monday first), month and year, with paid orders shown separately. Without dates, all
    /// orders; with <c>from</c> (and optionally <c>to</c>), that period (at most 366 days).
    /// </summary>
    /// <response code="200">The trend series, with zero for periods without orders.</response>
    /// <response code="400">An unknown or repeated parameter, or an invalid value.</response>
    /// <response code="429">Too many report requests; try again in a minute.</response>
    /// <response code="503">The business time zone setting is unusable.</response>
    [HttpGet("trends")]
    [ProducesResponseType<OrderTrendsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OrderTrendsResponse>> Trends(CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        OrderTrendsQuery? query = OrderReportQueryParser.ParseTrends(QueryValues(), errors);
        if (query is null)
        {
            return Invalid(errors);
        }

        OrderTrends? trends = await reports.TrendsAsync(query, cancellationToken);
        return trends is null ? Unavailable() : Ok(OrderTrendsResponse.Create(trends));
    }

    /// <summary>The orders report with the same filters (page and pageSize are ignored), as CSV: every row, at most 10,000.</summary>
    /// <response code="200">The CSV file.</response>
    /// <response code="400">An unknown or repeated parameter, or an invalid value.</response>
    /// <response code="422">More than 10,000 rows match; narrow the period or filters.</response>
    /// <response code="429">Too many exports; try again in a minute.</response>
    /// <response code="503">The business time zone setting is unusable.</response>
    [HttpGet("orders/export")]
    [EnableRateLimiting(RateLimitPolicies.ReportExports)]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "text/csv")]
    public async Task<IActionResult> ExportOrders(CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        OrderReportQuery? query = OrderReportQueryParser.ParseOrders(QueryValues(), errors);
        return query is null ? Invalid(errors) : Download(await exports.OrdersAsync(query, cancellationToken));
    }

    /// <summary>The trends with the same filters, as CSV: one line per weekday, month and year.</summary>
    /// <response code="200">The CSV file.</response>
    /// <response code="400">An unknown or repeated parameter, or an invalid value.</response>
    /// <response code="429">Too many exports; try again in a minute.</response>
    /// <response code="503">The business time zone setting is unusable.</response>
    [HttpGet("trends/export")]
    [EnableRateLimiting(RateLimitPolicies.ReportExports)]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "text/csv")]
    public async Task<IActionResult> ExportTrends(CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        OrderTrendsQuery? query = OrderReportQueryParser.ParseTrends(QueryValues(), errors);
        return query is null ? Invalid(errors) : Download(await exports.TrendsAsync(query, cancellationToken));
    }

    /// <summary>
    /// The change history with the AC-11.4 filters of GET /api/manager/audit/changes, as CSV: one line per changed field,
    /// newest first, personal data masked. cursor and limit are ignored: an export starts at the newest record.
    /// </summary>
    /// <response code="200">The CSV file.</response>
    /// <response code="400">An unknown or repeated filter, an invalid value, or an unknown table.</response>
    /// <response code="422">More than 10,000 records match; narrow the period or filters.</response>
    /// <response code="429">Too many exports; try again in a minute.</response>
    [HttpGet("audit/changes/export")]
    [EnableRateLimiting(RateLimitPolicies.ReportExports)]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "text/csv")]
    public async Task<IActionResult> ExportAuditChanges(CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        AuditQuery? query = AuditQueryParser.ParseChanges(QueryValues(), errors);
        return query is null ? Invalid(errors) : Download(await exports.AuditChangesAsync(query, cancellationToken));
    }

    /// <summary>Sign-in events with the filters of GET /api/manager/audit/security, as CSV; addresses masked as on screen.</summary>
    /// <response code="200">The CSV file.</response>
    /// <response code="400">An unknown or repeated filter, or an invalid value.</response>
    /// <response code="422">More than 10,000 events match; narrow the period or filters.</response>
    /// <response code="429">Too many exports; try again in a minute.</response>
    [HttpGet("audit/security/export")]
    [EnableRateLimiting(RateLimitPolicies.ReportExports)]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "text/csv")]
    public async Task<IActionResult> ExportSecurityEvents(CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        SecurityEventQuery? query = AuditQueryParser.ParseSecurity(QueryValues(), errors);
        return query is null ? Invalid(errors) : Download(await exports.SecurityEventsAsync(query, cancellationToken));
    }

    private IActionResult Download(ExportResult result) => result.Status switch
    {
        ExportStatus.Ready when result.File is { } file => File(file.Content, "text/csv; charset=utf-8", file.FileName),
        ExportStatus.TooLarge => Problem(
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: ReportMessages.TooLargeTitle,
            detail: ReportMessages.TooLargeDetail),
        ExportStatus.UnknownEntity => Invalid(new Dictionary<string, string> { ["entity"] = AuditMessages.UnknownEntity }),
        _ => Unavailable(),
    };

    private Dictionary<string, string[]> QueryValues() =>
        Request.Query.ToDictionary(pair => pair.Key, pair => pair.Value.Select(value => value ?? string.Empty).ToArray(), StringComparer.Ordinal);

    private ActionResult Invalid(IReadOnlyDictionary<string, string> errors)
    {
        foreach ((string key, string message) in errors)
        {
            ModelState.AddModelError(key, message);
        }

        return ValidationProblem(ModelState);
    }

    private ObjectResult Unavailable() =>
        Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: ReportMessages.UnavailableTitle, detail: ReportMessages.UnavailableDetail);
}
