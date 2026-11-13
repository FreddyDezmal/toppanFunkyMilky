namespace Milkshake.Api.Contracts;

/// <summary>
/// Overall API health, returned by GET /api/health.
/// </summary>
/// <param name="Status">Healthy, Degraded or Unhealthy.</param>
/// <param name="CheckedAtUtc">When the checks ran (UTC).</param>
/// <param name="TotalDurationMs">Total time taken by all checks, in milliseconds.</param>
/// <param name="Checks">Result of each individual check.</param>
public sealed record HealthResponse(
    string Status,
    DateTimeOffset CheckedAtUtc,
    double TotalDurationMs,
    IReadOnlyList<HealthCheckEntryResponse> Checks);

/// <summary>
/// Result of one named health check (e.g. "self", later "database").
/// </summary>
/// <param name="Name">Check name.</param>
/// <param name="Status">Healthy, Degraded or Unhealthy.</param>
/// <param name="Description">Optional human-readable detail.</param>
/// <param name="DurationMs">Time taken by this check, in milliseconds.</param>
public sealed record HealthCheckEntryResponse(
    string Name,
    string Status,
    string? Description,
    double DurationMs);