using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Milkshake.Application.Payments;
using Milkshake.Domain.Notifications;
using Milkshake.Domain.Payments;
using Milkshake.Infrastructure.Notifications;
using Milkshake.Infrastructure.Payments;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Infrastructure.Operations;

/// <summary>
/// When each background worker last completed a run (M14 decision D-11). Kept in memory: after a restart the workers
/// run at once, so a missing beat only matters once the app has been up for a while.
/// </summary>
public sealed class WorkerHeartbeats(TimeProvider clock)
{
    /// <summary>Name of the payment reconciliation worker.</summary>
    public const string PaymentReconciliation = "payment-reconciliation";

    /// <summary>Name of the email dispatch worker.</summary>
    public const string EmailDispatch = "email-dispatch";

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRun = new(StringComparer.Ordinal);

    /// <summary>When the app (this instance) started.</summary>
    public DateTimeOffset StartedAt { get; } = clock.GetUtcNow();

    /// <summary>Records that a worker finished a run (or skipped one deliberately, e.g. no provider key yet).</summary>
    public void Beat(string worker) => _lastRun[worker] = clock.GetUtcNow();

    /// <summary>The last run of a worker, or null if it has not run since start-up.</summary>
    public DateTimeOffset? LastRun(string worker) => _lastRun.TryGetValue(worker, out DateTimeOffset at) ? at : null;
}

/// <summary>
/// Health of the background work (M14 decisions D-11, D-13), read by the scheduled monitoring check:
/// <list type="bullet">
/// <item>each enabled worker has run within three of its intervals (after a start-up grace of the same length);</item>
/// <item>no email has failed for good in the last 24 hours;</item>
/// <item>no refund is failed or waiting for attention at Paystack.</item>
/// </list>
/// Reports <see cref="HealthStatus.Degraded"/> (HTTP 200) rather than Unhealthy, so the host does not restart the app
/// over work a manager must handle; the monitoring check treats anything but Healthy as an alert. Counts only: no
/// personal data or references. Registered with failure status Degraded: if the database cannot be read, the
/// "database" check is the one that reports Unhealthy.
/// </summary>
internal sealed class WorkersHealthCheck(
    WorkerHeartbeats heartbeats,
    MilkshakeDbContext db,
    TimeProvider clock,
    IOptions<PaymentReconciliationOptions> reconciliation,
    IOptions<EmailOptions> email) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = clock.GetUtcNow();
        var problems = new List<string>();
        var data = new Dictionary<string, object>(StringComparer.Ordinal);

        CheckWorker(WorkerHeartbeats.PaymentReconciliation, reconciliation.Value.Enabled,
            TimeSpan.FromMinutes(Math.Max(1, reconciliation.Value.IntervalMinutes)), now, problems, data);
        CheckWorker(WorkerHeartbeats.EmailDispatch, email.Value.Dispatch.Enabled,
            TimeSpan.FromSeconds(Math.Max(5, email.Value.Dispatch.IntervalSeconds)), now, problems, data);

        DateTimeOffset dayAgo = now.AddDays(-1);
        int failedEmails = await db.EmailNotifications.AsNoTracking()
            .CountAsync(e => e.Status == NotificationStatus.Failed && e.UpdatedAt >= dayAgo, cancellationToken);
        int refundsNeedingAttention = await db.Refunds.AsNoTracking()
            .CountAsync(r => r.Status == RefundStatus.Failed ||
                             (r.Status == RefundStatus.Pending && r.ProviderMessage == GatewayRefund.NeedsAttentionMessage),
                cancellationToken);
        data["emails-failed-24h"] = failedEmails;
        data["refunds-needing-attention"] = refundsNeedingAttention;
        if (failedEmails > 0)
        {
            problems.Add($"{failedEmails} email(s) failed in the last 24 hours (Management → Emails)");
        }

        if (refundsNeedingAttention > 0)
        {
            problems.Add($"{refundsNeedingAttention} refund(s) need attention (Management → Orders)");
        }

        return problems.Count == 0
            ? HealthCheckResult.Healthy("Background work is running and nothing is waiting for a manager.", data)
            : HealthCheckResult.Degraded(string.Join("; ", problems) + ".", data: data);
    }

    private void CheckWorker(string name, bool enabled, TimeSpan interval, DateTimeOffset now, List<string> problems, Dictionary<string, object> data)
    {
        if (!enabled)
        {
            data[name] = "disabled";
            return;
        }

        TimeSpan allowed = interval * 3;
        DateTimeOffset? lastRun = heartbeats.LastRun(name);
        data[name] = lastRun?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? "not yet";
        DateTimeOffset since = lastRun ?? heartbeats.StartedAt;
        if (now - since > allowed)
        {
            problems.Add(lastRun is null
                ? $"{name} has not run since start-up"
                : $"{name} last ran {(int)(now - since).TotalMinutes} minute(s) ago");
        }
    }
}
