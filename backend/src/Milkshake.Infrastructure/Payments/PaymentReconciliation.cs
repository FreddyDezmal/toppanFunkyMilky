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
using Milkshake.Domain.Auditing;

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

/// <summary>
/// One reconciliation run (decision P7b, M8b R4, R7, Q2), each part in batches, oldest first:
/// 1. open payment attempts are settled with the provider;
/// 2. payments the order could not accept (late or duplicate) get a full refund (Q2);
/// 3. refunds the provider has not accepted yet are sent again (our reference makes this safe);
/// 4. pending refunds are checked with the provider until Processed or Failed;
/// 5. manager requests left unanswered at the pickup time, or made moot by a cancellation, are marked Lapsed.
/// If the provider is unavailable the provider steps stop for this run; nothing is changed.
/// </summary>
internal sealed partial class PaymentReconciler(
    MilkshakeDbContext db,
    PaymentSettler settler,
    IPaymentGateway gateway,
    RefundSubmitter refunds,
    TimeProvider clock,
    IOptions<PaymentReconciliationOptions> options,
    ILogger<PaymentReconciler> logger) : IPaymentReconciler
{
    public async Task<int> ReconcileAsync(CancellationToken cancellationToken)
    {
        using IDisposable actor = AuditActorScope.Begin(AuditActors.PaymentReconciliation);   // M11 audit attribution
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

        try
        {
            changed += await RefundUnacceptedPaymentsAsync(cancellationToken);
            changed += await SubmitWaitingRefundsAsync(cancellationToken);
            changed += await CheckPendingRefundsAsync(cancellationToken);
        }
        catch (PaymentGatewayException)
        {
            // The provider is unavailable: the refund steps stop for this run; the next one tries again.
            LogProviderUnavailable(logger);
        }

        changed += await LapseRequestsAsync(cancellationToken);

        if (changed > 0)
        {
            LogReconciled(logger, changed);
        }

        return changed;
    }

    /// <summary>Q2: a full refund, without a fee, for each late or duplicate payment that has none yet.</summary>
    private async Task<int> RefundUnacceptedPaymentsAsync(CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        List<Payment> unaccepted = await db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.RequiresRefund && !db.Refunds.Any(r => r.PaymentId == p.Id))
            .OrderBy(p => p.UpdatedAt)
            .Take(Batch)
            .ToListAsync(cancellationToken);

        int changed = 0;
        foreach (Payment payment in unaccepted)
        {
            if (payment.RefundReason == RefundReason.AmountMismatch)
            {
                // The amount actually taken is not the one we asked for, and we do not store it: refunding a guess could
                // return too much or too little. Left for manual handling at Paystack.
                LogMismatchNeedsManualRefund(logger, payment.Reference);
                continue;
            }

            db.ChangeTracker.Clear();
            Refund refund = Refund.ForUnacceptedPayment(payment, Refund.NewReference());
            db.Refunds.Add(refund);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Another run created it at the same moment (one refund per payment).
                continue;
            }

            changed++;
            await refunds.SubmitAsync(refund, payment.Reference, cancellationToken);
        }

        return changed;
    }

    /// <summary>R4: refunds stored but not yet accepted by the provider (it was unreachable, or the process stopped).</summary>
    private async Task<int> SubmitWaitingRefundsAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset before = clock.GetUtcNow() - PaymentService.InitiatingGrace;
        db.ChangeTracker.Clear();
        var waiting = await db.Refunds
            .Where(r => r.Status == RefundStatus.Initiating && r.CreatedAt < before)
            .Join(db.Payments, r => r.PaymentId, p => p.Id, (r, p) => new { Refund = r, PaymentReference = p.Reference })
            .OrderBy(x => x.Refund.CreatedAt)
            .Take(Batch)
            .ToListAsync(cancellationToken);

        int changed = 0;
        foreach (var item in waiting)
        {
            GatewayRefund answer = await gateway.CreateRefundAsync(
                new RefundRequest(item.Refund.Reference, item.PaymentReference, item.Refund.AmountMinor, item.Refund.CurrencyCode, item.Refund.Kind.ToString()),
                cancellationToken);
            changed += await ApplyAsync(item.Refund, answer);
        }

        return changed;
    }

    /// <summary>R4: pending refunds are checked with the provider; only its answer completes them.</summary>
    private async Task<int> CheckPendingRefundsAsync(CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        List<Refund> pending = await db.Refunds
            .Where(r => r.Status == RefundStatus.Pending)
            .OrderBy(r => r.UpdatedAt)
            .Take(Batch)
            .ToListAsync(cancellationToken);

        int changed = 0;
        foreach (Refund refund in pending)
        {
            changed += await ApplyAsync(refund, await gateway.GetRefundAsync(refund.Reference, cancellationToken));
        }

        return changed;
    }

    private async Task<int> ApplyAsync(Refund refund, GatewayRefund answer)
    {
        if (!refunds.Apply(refund, answer))
        {
            return 0;
        }

        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
            return 1;
        }
        catch (DbUpdateConcurrencyException)
        {
            // A notification applied the same answer at the same moment.
            db.Entry(refund).State = EntityState.Detached;
            return 0;
        }
    }

    /// <summary>R7: requests nobody answered before the pickup time, or whose order the patron cancelled, are closed.</summary>
    private async Task<int> LapseRequestsAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        db.ChangeTracker.Clear();
        var due = await db.CancellationRequests
            .Where(r => r.Status == CancellationRequestStatus.Pending)
            .Join(db.Orders.Where(o => o.PickupAtUtc <= now || o.Status == OrderStatus.Cancelled), r => r.OrderId, o => o.Id,
                (r, o) => new { Request = r, Order = o })
            .OrderBy(x => x.Request.Id) // oldest first, so a batch is deterministic (M13: EF Core warning 10102)
            .Take(Batch)
            .ToListAsync(cancellationToken);

        int changed = due.Count(item => item.Request.LapseIfDue(item.Order, now));
        if (changed > 0)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A patron answered at the same moment; the next run sees it.
                return 0;
            }
        }

        return changed;
    }

    private int Batch => Math.Max(1, options.Value.BatchSize);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment reconciliation stopped early: the payment provider is unavailable.")]
    private static partial void LogProviderUnavailable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment reconciliation changed {Count} payment(s), refund(s) or request(s).")]
    private static partial void LogReconciled(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Payment {PaymentReference} was received with a different amount and cannot be refunded automatically; refund it manually at Paystack.")]
    private static partial void LogMismatchNeedsManualRefund(ILogger logger, string paymentReference);
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
