using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Milkshake.Domain.Auditing;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Infrastructure.Identity;

/// <summary>Writes one row to milkshake.security_events.</summary>
internal sealed class SecurityEventRecorder(
    MilkshakeDbContext dbContext,
    TimeProvider timeProvider,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task RecordAsync(SecurityEventType eventType, Guid? userId, string? email, CancellationToken cancellationToken)
    {
        dbContext.SecurityEvents.Add(new SecurityEvent(
            timeProvider.GetUtcNow(),
            eventType,
            userId,
            email,
            httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            Activity.Current?.TraceId.ToString()));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}