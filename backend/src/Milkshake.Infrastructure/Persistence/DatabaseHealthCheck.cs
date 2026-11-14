using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Milkshake.Infrastructure.Persistence;

/// <summary>
/// Verifies that the API can open a connection to PostgreSQL and run a trivial query.
/// Unlike EF Core's built-in check, the failure exception is attached to the result,
/// so the health check service logs the real cause (it is never returned to clients).
/// </summary>
internal sealed class DatabaseHealthCheck(MilkshakeDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
            return HealthCheckResult.Healthy("Database connection succeeded.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                "Database is unreachable. See the API log for details.",
                exception);
        }
    }
}