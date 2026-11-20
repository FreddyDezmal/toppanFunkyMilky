using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Milkshake.Api.Contracts.Management;
using Milkshake.Api.Security;
using Milkshake.Application.Management;

namespace Milkshake.Api.Controllers;

/// <summary>
/// Audit history for managers (M11, AC-11.4, T11.4): the field-level change history and the sign-in events, with
/// filters and paging. Read-only – audit records cannot be changed through the application (AC-11.3). Personal data
/// is masked; managers acting in the history are shown in full.
/// </summary>
[ApiController]
[Route("api/manager/audit")]
[Authorize(Policy = AuthorizationPolicies.ManagerOnly)]
[EnableRateLimiting(RateLimitPolicies.Audit)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ManagerAuditController(IAuditQueryService audit) : ControllerBase
{
    /// <summary>
    /// Changes, newest first. Filters: <c>from</c>, <c>to</c> (ISO 8601, UTC, to exclusive, at most 366 days apart),
    /// <c>user</c> (email or id), <c>actor</c> (user, anonymous, system:…), <c>entity</c> (table), <c>action</c>
    /// (Insert, Update, Delete), <c>q</c> (free text, at most 100 characters), <c>cursor</c>, <c>limit</c> (1–100, default 50).
    /// </summary>
    /// <response code="200">A page of changes.</response>
    /// <response code="400">An unknown, repeated or invalid filter.</response>
    /// <response code="429">Too many requests; try again shortly.</response>
    [HttpGet("changes")]
    [ProducesResponseType<AuditPageResponse<AuditChangeResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuditPageResponse<AuditChangeResponse>>> Changes(CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        AuditQuery? query = AuditQueryParser.ParseChanges(QueryValues(), errors);
        if (query is null)
        {
            return Invalid(errors);
        }

        AuditPage<AuditEntryView>? page = await audit.ChangesAsync(query, cancellationToken);
        if (page is null)
        {
            return Invalid(new Dictionary<string, string> { ["entity"] = AuditMessages.UnknownEntity });
        }

        return Ok(new AuditPageResponse<AuditChangeResponse>(page.Items.Select(AuditChangeResponse.From).ToList(), page.NextCursor));
    }

    /// <summary>
    /// Sign-ins, failed sign-ins, lockouts and sign-outs, newest first. Filters: <c>from</c>, <c>to</c>, <c>user</c>,
    /// <c>eventType</c>, <c>cursor</c>, <c>limit</c>.
    /// </summary>
    /// <response code="200">A page of events.</response>
    /// <response code="400">An unknown, repeated or invalid filter.</response>
    /// <response code="429">Too many requests; try again shortly.</response>
    [HttpGet("security")]
    [ProducesResponseType<AuditPageResponse<SecurityEventResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuditPageResponse<SecurityEventResponse>>> Security(CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        SecurityEventQuery? query = AuditQueryParser.ParseSecurity(QueryValues(), errors);
        if (query is null)
        {
            return Invalid(errors);
        }

        AuditPage<SecurityEventView> page = await audit.SecurityEventsAsync(query, cancellationToken);
        return Ok(new AuditPageResponse<SecurityEventResponse>(page.Items.Select(SecurityEventResponse.From).ToList(), page.NextCursor));
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
}
