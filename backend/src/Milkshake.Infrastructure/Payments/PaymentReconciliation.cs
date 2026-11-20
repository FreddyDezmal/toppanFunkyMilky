using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Milkshake.Application.Abstractions;
using Milkshake.Application.Payments;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Infrastructure.Payments;

/// <summary>Settings for the reconciliation job (section "Payments:Reconciliation").</summary>
public sealed class PaymentReconciliationOptions
{
    public const string SectionName = "Payments:Reconciliation";

    /// <summary>Switched off in automated tests, which call the reconciler directly.</summary>
    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 5;

    public int BatchSize { get; set; } = 50;
}

/// <summary>Checks one batch of open attempts with the provider, oldest first (decision P7b).</summary>
internal sealed partial class PaymentReconciler(
    MilkshakeDbContext db,
    PaymentSettler settler,
    TimeProvider clock,
    IOptions<PaymentReconciliationOptions> options,
    ILogger<PaymentReconciler> logger) : IPaymentReconciler
{
    public async Task<int> ReconcileAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset initiatingBefore = clock.GetUtcNow() - PaymentService.InitiatingGrace;
        List<int> open = await db.Payments
            .Where(p => p.Status == PaymentStatus.Pending || (p.Status == PaymentStatus.Initiating && p.CreatedAt < initiatingBefore))
            .OrderBy(p => p.CreatedAt)
            .Select(p => p.Id)
            .Take(Math.Max(1, options.Value.BatchSize))
            .ToListAsync(cancellationToken);

        int changed = 0;
        foreach (int id in open)
        {
            db.ChangeTracker.Clear();
            Payment? payment = await db.Payments.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (payment is null)
            {
                continue;
            }

            try
            {
                if (await settler.SettleAsync(payment, cancellationToken))
                {
                    changed++;
                }
            }
            catch (PaymentGatewayException)
            {
                // The provider is unavailable: stop this run; the next one tries again. Nothing was changed.
                LogProviderUnavailable(logger);
                break;
            }
            catch (DbUpdateConcurrencyException)
            {
                // A notification settled it at the same moment; the next run sees the result.
            }
        }

        if (changed > 0)
        {
            LogReconciled(logger, changed);
        }

        return changed;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment reconciliation stopped early: the payment provider is unavailable.")]
    private static partial void LogProviderUnavailable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment reconciliation settled {Count} payment attempt(s).")]
    private static partial void LogReconciled(ILogger logger, int count);
}

/// <summary>
/// Runs the reconciler every few minutes inside the API (no extra infrastructure). Skips runs while no provider key is
/// configured, and never lets one failed run stop the next.
/// </summary>
internal sealed partial class PaymentReconciliationWorker(
    IServiceScopeFactory scopes,
    IOptions<PaymentReconciliationOptions> options,
    IOptions<PaystackOptions> paystack,
    ILogger<PaymentReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.Value.IntervalMinutes)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (string.IsNullOrEmpty(paystack.Value.SecretKey))
            {
                continue;
            }

            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IPaymentReconciler>().ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException or TimeoutException or Npgsql.NpgsqlException)
            {
                LogRunFailed(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A payment reconciliation run failed; the next run will try again.")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);
}

/// <summary>The patron's refresh (decision P7a): the latest attempt, checked with the provider if it is open.</summary>
internal sealed class PaymentStatusService(MilkshakeDbContext db, PaymentSettler settler, ICurrentUser currentUser) : IPaymentStatusService
{
    public async Task<PaymentStatusView?> RefreshAsync(string orderReference, CancellationToken cancellationToken)
    {
        Guid patronId = currentUser.UserId ?? throw new InvalidOperationException("Refreshing a payment requires a signed-in patron.");
        if (!OrderReference.IsValid(orderReference))
        {
            return null;
        }

        int? orderId = await db.Orders.Where(o => o.PatronId == patronId && o.Reference == orderReference)
            .Select(o => (int?)o.Id).SingleOrDefaultAsync(cancellationToken);
        if (orderId is null)
        {
            return null;
        }

        Payment? latest = await Latest(orderId.Value, cancellationToken);
        if (latest is not null && PaymentStatusTransitions.IsOpen(latest.Status))
        {
            try
            {
                // Settle a tracked copy so the change is saved.
                Payment tracked = await db.Payments.SingleAsync(p => p.Id == latest.Id, cancellationToken);
                await settler.SettleAsync(tracked, cancellationToken);
            }
            catch (Exception exception) when (exception is PaymentGatewayException or DbUpdateConcurrencyException)
            {
                // Provider unavailable or settled elsewhere at the same moment: show what is stored; the patron can retry.
                db.ChangeTracker.Clear();
            }

            db.ChangeTracker.Clear();
            latest = await Latest(orderId.Value, cancellationToken);
        }

        OrderStatus status = await db.Orders.Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync(cancellationToken);
        return new PaymentStatusView(status, latest?.Reference, latest?.Status, latest?.ProviderMessage);
    }

    private Task<Payment?> Latest(int orderId, CancellationToken cancellationToken) =>
        db.Payments.AsNoTracking().Where(p => p.OrderId == orderId)
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
