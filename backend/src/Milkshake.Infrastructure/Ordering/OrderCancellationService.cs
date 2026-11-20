using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Milkshake.Application.Abstractions;
using Milkshake.Application.Ordering;
using Milkshake.Application.Payments;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;
using Milkshake.Domain.Settings;
using Milkshake.Infrastructure.Payments;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Infrastructure.Ordering;

/// <summary>
/// Patron cancellation (M8b). Order of work:
/// 1. The patron's own order; a cancelled one is answered with its current state (a repeat is harmless).
/// 2. Before the pickup time only (B1.1).
/// 3. Unpaid: an open payment attempt is checked with the provider first (R6). If it was just paid, nothing is
///    cancelled and the patron sees the refund they would get; otherwise the attempt is closed and the order cancelled
///    for free (B1.3).
/// 4. Paid: the refund the patron confirmed must equal the server's (paid − fee, R5). The order's cancellation and its
///    refund are saved in ONE transaction, then the provider is asked to return the money (R4).
/// Pending manager requests for the order lapse when it is cancelled.
/// </summary>
internal sealed partial class OrderCancellationService(
    MilkshakeDbContext db,
    IPaymentGateway gateway,
    RefundSubmitter refunds,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<OrderCancellationService> logger) : IOrderCancellationService
{
    public async Task<OrderCancellationView?> GetAsync(string orderReference, CancellationToken cancellationToken)
    {
        Order? order = await FindAsync(orderReference, tracked: false, cancellationToken);
        return order is null ? null : await ViewAsync(order, await FeeRateAsync(cancellationToken), cancellationToken);
    }

    public async Task<CancelOrderResult> CancelAsync(string orderReference, decimal expectedRefund, CancellationToken cancellationToken)
    {
        Order? order = await FindAsync(orderReference, tracked: true, cancellationToken);
        if (order is null)
        {
            return new CancelOrderResult(CancelOrderOutcome.NotFound);
        }

        decimal? feeRate = await FeeRateAsync(cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();

        if (order.Status == OrderStatus.Cancelled)
        {
            return await ResultAsync(CancelOrderOutcome.AlreadyCancelled, order, feeRate, cancellationToken);
        }

        if (!order.CanBeCancelledAt(now))
        {
            return await ResultAsync(CancelOrderOutcome.PickupPassed, order, feeRate, cancellationToken);
        }

        if (OrderStatusTransitions.AwaitsPayment(order.Status))
        {
            Payment? open = await db.Payments.SingleOrDefaultAsync(
                p => p.OrderId == order.Id && (p.Status == PaymentStatus.Initiating || p.Status == PaymentStatus.Pending), cancellationToken);
            if (open is not null)
            {
                CancelOrderOutcome? blocked = await CloseOpenAttemptAsync(order, open, now, cancellationToken);
                if (blocked is not null)
                {
                    return await ResultAsync(blocked.Value, order, feeRate, cancellationToken);
                }
            }
        }

        if (order.Status == OrderStatus.Confirmed)
        {
            return await CancelPaidAsync(order, expectedRefund, feeRate, now, cancellationToken);
        }

        if (expectedRefund != 0m)
        {
            db.ChangeTracker.Clear();
            return await ResultAsync(CancelOrderOutcome.RefundChanged, order, feeRate, cancellationToken);
        }

        order.Cancel(now);
        await LapseRequestsAsync(order, now, cancellationToken);
        return await SaveCancellationAsync(order, feeRate, refund: null, paymentReference: null, cancellationToken);
    }

    /// <summary>
    /// Settles an unpaid order's open attempt before cancelling (R6). Returns an outcome that stops the cancellation, or
    /// null to continue (the attempt is closed in memory, or the order has just been confirmed and is now paid).
    /// </summary>
    private async Task<CancelOrderOutcome?> CloseOpenAttemptAsync(Order order, Payment open, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (open.Status == PaymentStatus.Initiating && now - open.CreatedAt < PaymentService.InitiatingGrace)
        {
            return CancelOrderOutcome.PaymentInProgress;
        }

        GatewayVerification verified;
        try
        {
            verified = await gateway.VerifyAsync(open.Reference, cancellationToken);
        }
        catch (PaymentGatewayException)
        {
            // Unknown whether money is moving: cancelling now could leave a paid, cancelled order.
            return CancelOrderOutcome.Unavailable;
        }

        if (!verified.Found)
        {
            open.MarkAbandoned("No transaction at the provider.", now);
        }
        else if (verified.Outcome is VerifiedOutcome.Open or VerifiedOutcome.Abandoned)
        {
            // Closed on our side; if the patron still pays in an open tab, that payment is refunded in full (Q2).
            open.Supersede();
        }
        else
        {
            bool otherSuccess = await db.Payments.AnyAsync(
                p => p.OrderId == order.Id && p.Id != open.Id && p.Status == PaymentStatus.Succeeded, cancellationToken);
            open.ApplyVerifiedOutcome(order, verified.Outcome, verified.AmountMinor, verified.CurrencyCode, verified.TransactionId,
                verified.Message, otherSuccess, now);
        }

        if (order.Status != OrderStatus.Confirmed)
        {
            return null;
        }

        // It was paid just now: keep that, and show the patron the refund before anything is cancelled.
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return CancelOrderOutcome.PaymentInProgress;
        }

        return CancelOrderOutcome.RefundChanged;
    }

    private async Task<CancelOrderResult> CancelPaidAsync(Order order, decimal expectedRefund, decimal? feeRate, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Payment payment = await db.Payments.AsNoTracking().SingleAsync(
            p => p.OrderId == order.Id && p.Status == PaymentStatus.Succeeded, cancellationToken);
        if (feeRate is null)
        {
            return new CancelOrderResult(CancelOrderOutcome.Unavailable);
        }

        CancellationQuote quote = Quote(order, payment, feeRate.Value);
        if (expectedRefund != quote.RefundAmount)
        {
            return await ResultAsync(CancelOrderOutcome.RefundChanged, order, feeRate, cancellationToken);
        }

        order.Cancel(now);
        Refund refund = Refund.ForCancellation(order, payment, Refund.NewReference(), feeRate.Value, requestedByRestaurant: false);
        db.Refunds.Add(refund);
        await LapseRequestsAsync(order, now, cancellationToken);
        return await SaveCancellationAsync(order, feeRate, refund, payment.Reference, cancellationToken);
    }

    private async Task<CancelOrderResult> SaveCancellationAsync(
        Order order, decimal? feeRate, Refund? refund, string? paymentReference, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A simultaneous request or notification changed the order first (row version, or one refund per payment).
            db.ChangeTracker.Clear();
            Order current = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id, cancellationToken);
            CancelOrderOutcome outcome = current.Status == OrderStatus.Cancelled ? CancelOrderOutcome.AlreadyCancelled : CancelOrderOutcome.RefundChanged;
            return await ResultAsync(outcome, current, feeRate, cancellationToken);
        }

        LogCancelled(logger, order.Reference, refund?.Reference);
        if (refund is not null)
        {
            // The cancellation is stored; if the provider cannot be reached now, the reconciliation job retries.
            await refunds.SubmitAsync(refund, paymentReference!, CancellationToken.None);
        }

        return await ResultAsync(CancelOrderOutcome.Cancelled, order, feeRate, cancellationToken);
    }

    private async Task LapseRequestsAsync(Order order, DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<CancellationRequest> pending = await db.CancellationRequests
            .Where(r => r.OrderId == order.Id && r.Status == CancellationRequestStatus.Pending)
            .ToListAsync(cancellationToken);
        foreach (CancellationRequest request in pending)
        {
            request.LapseIfDue(order, now);
        }
    }

    private async Task<Order?> FindAsync(string orderReference, bool tracked, CancellationToken cancellationToken)
    {
        Guid patronId = currentUser.UserId ?? throw new InvalidOperationException("Cancelling requires a signed-in patron.");
        if (!OrderReference.IsValid(orderReference))
        {
            return null;
        }

        IQueryable<Order> orders = tracked ? db.Orders : db.Orders.AsNoTracking();
        return await orders.SingleOrDefaultAsync(o => o.PatronId == patronId && o.Reference == orderReference, cancellationToken);
    }

    private async Task<CancelOrderResult> ResultAsync(CancelOrderOutcome outcome, Order order, decimal? feeRate, CancellationToken cancellationToken) =>
        new(outcome, await ViewAsync(order, feeRate, cancellationToken));

    internal async Task<OrderCancellationView> ViewAsync(Order order, decimal? feeRate, CancellationToken cancellationToken)
    {
        Order current = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id, cancellationToken);
        Payment? paid = await db.Payments.AsNoTracking().SingleOrDefaultAsync(
            p => p.OrderId == current.Id && p.Status == PaymentStatus.Succeeded, cancellationToken);
        List<Refund> stored = await db.Refunds.AsNoTracking().Where(r => r.OrderId == current.Id).OrderBy(r => r.Id).ToListAsync(cancellationToken);

        CancellationBlock? block = current.Status == OrderStatus.Cancelled ? CancellationBlock.AlreadyCancelled
            : !current.CanBeCancelledAt(clock.GetUtcNow()) ? CancellationBlock.PickupPassed
            : null;
        var none = new CancellationQuote(false, block, paid is not null, 0m, 0m, 0m, 0m, current.CurrencyCode);

        CancellationQuote quote = block is not null ? none
            : paid is null ? none with { CanCancel = true }
            : feeRate is null ? none      // a paid order waits until the fee rate setting is fixed
            : Quote(current, paid, feeRate.Value);

        return new OrderCancellationView(current.Reference, current.Status, quote, stored.Select(ToView).ToList());
    }

    private static CancellationQuote Quote(Order order, Payment payment, decimal feeRate)
    {
        decimal paid = MinorUnits.FromMinor(payment.AmountMinor, payment.CurrencyCode);
        decimal fee = Refund.CancellationFee(order, feeRate);
        return new CancellationQuote(true, null, true, paid, feeRate, fee, paid - fee, payment.CurrencyCode);
    }

    private static RefundView ToView(Refund refund) => new(
        refund.Reference,
        refund.Kind,
        refund.Status,
        MinorUnits.FromMinor(refund.AmountMinor, refund.CurrencyCode),
        MinorUnits.FromMinor(refund.FeeMinor, refund.CurrencyCode),
        refund.Status == RefundStatus.Pending && refund.ProviderMessage == GatewayRefund.NeedsAttentionMessage,
        refund.CreatedAt,
        refund.CompletedAt);

    /// <summary>The fee rate setting (B1.4), or null if it is missing or out of range (paid cancellations then wait).</summary>
    private async Task<decimal?> FeeRateAsync(CancellationToken cancellationToken)
    {
        string? raw = await db.SystemSettings.AsNoTracking().Where(s => s.Key == SettingKeys.RefundFeeRate).Select(s => s.Value)
            .SingleOrDefaultAsync(cancellationToken);
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal rate) && rate >= 0m && rate <= Refund.MaxFeeRate)
        {
            return rate;
        }

        LogFeeRateUnavailable(logger);
        return null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderReference} was cancelled by its patron (refund {RefundReference}).")]
    private static partial void LogCancelled(ILogger logger, string orderReference, string? refundReference);

    [LoggerMessage(Level = LogLevel.Error, Message = "The Refund.FeeRate setting is missing or invalid; paid orders cannot be cancelled until it is fixed.")]
    private static partial void LogFeeRateUnavailable(ILogger logger);
}
