using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Milkshake.Api.Contracts.Management;
using Milkshake.Api.Security;
using Milkshake.Application.Management;

namespace Milkshake.Api.Controllers;

/// <summary>
/// Management reports (M12): orders for a date or date range (AC-12.2) and order volumes by weekday, month and year
/// (AC-12.3). Managers only, read-only, never cached, rate-limited per manager. Periods are business-local dates.
/// </summary>
[ApiController]
[Route("api/manager/reports")]
[Authorize(Policy = AuthorizationPolicies.ManagerOnly)]
[EnableRateLimiting(RateLimitPolicies.Reports)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ManagerReportsController(IOrderReportService reports) : ControllerBase
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
