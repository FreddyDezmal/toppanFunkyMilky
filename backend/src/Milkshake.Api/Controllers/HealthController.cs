using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Milkshake.Api.Contracts;

namespace Milkshake.Api.Controllers;

/// <summary>
/// Reports whether the API and its dependencies are available.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/health")]
public sealed class HealthController(HealthCheckService healthCheckService, TimeProvider timeProvider) : ControllerBase
{
    /// <summary>
    /// Runs all registered health checks.
    /// </summary>
    /// <response code="200">The API is healthy or degraded.</response>
    /// <response code="503">At least one critical dependency is unhealthy.</response>
    [HttpGet]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthResponse>> Get(CancellationToken cancellationToken)
    {
        HealthReport report = await healthCheckService.CheckHealthAsync(cancellationToken);

        var response = new HealthResponse(
            Status: report.Status.ToString(),
            CheckedAtUtc: timeProvider.GetUtcNow(),
            TotalDurationMs: report.TotalDuration.TotalMilliseconds,
            Checks: report.Entries
                .Select(entry => new HealthCheckEntryResponse(
                    Name: entry.Key,
                    Status: entry.Value.Status.ToString(),
                    Description: entry.Value.Description,
                    DurationMs: entry.Value.Duration.TotalMilliseconds))
                .ToList());

        return report.Status == HealthStatus.Unhealthy
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, response)
            : Ok(response);
    }
}