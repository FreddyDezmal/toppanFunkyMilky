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
/// Patron cancellation and answers to manager requests (M8b). Order of work:
/// 1. The patron's own order; a cancelled one is answered with its current state (a repeat is harmless).
/// 2. Before the pickup time only (B1.1).
/// 3. Unpaid: an open payment attempt is checked with the provider first (R6). If it was just paid, nothing is
///    cancelled and the patron sees the refund they would get; otherwise the attempt is closed and the order cancelled
///    for free (B1.3).
/// 4. Paid: the refund the patron confirmed must equal the server's – paid less the fee for the patron's own
///    cancellation (B1.4, R5), everything paid when they accept the restaurant's request (Q1). The cancellation and its
///    refund are saved in ONE transaction, then the provider is asked to return the money (R4).
/// Pending manager requests for the order lapse when the patron cancels it themselves.
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
        return order is null ? null : await ViewAsync(order.Id, await FeeRateAsync(cancellationToken), cancellationToken);
    }

    public Task<CancelOrderResult> CancelAsync(string orderReference, decimal expectedRefund, CancellationToken cancellationToken) =>
        CancelAsync(orderReference, expectedRefund, acceptRequest: false, cancellationToken);

    public Task<CancelOrderResult> AcceptRequestAsync(string orderReference, decimal expectedRefund, CancellationToken cancellationToken) =>
        CancelAsync(orderReference, expectedRefund, acceptRequest: true, cancellationToken);

    public async Task<CancelOrderResult> DeclineRequestAsync(string orderReference, CancellationToken cancellationToken)
    {
        Order? order = await FindAsync(orderReference, tracked: false, cancellationToken);
        if (order is null)
        {
            return new CancelOrderResult(CancelOrderOutcome.NotFound);
        }

        decimal? feeRate = await FeeRateAsync(cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();
        CancellationRequest? request = await PendingRequestAsync(order.Id, cancellationToken);
        if (request is null || !request.IsOpenAt(order, now) || order.Status == OrderStatus.Cancelled)
        {
            return await ResultAsync(CancelOrderOutcome.NoOpenRequest, order.Id, feeRate, cancellationToken);
        }

        request.Decline(order, now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Answered, or the order cancelled, at the same moment.
            db.ChangeTracker.Clear();
            return await ResultAsync(CancelOrderOutcome.NoOpenRequest, order.Id, feeRate, cancellationToken);
        }

        LogDeclined(logger, order.Reference);
        return await ResultAsync(CancelOrderOutcome.Declined, order.Id, feeRate, cancellationToken);
    }

    public async Task<IReadOnlyList<PendingCancellationRequest>> PendingRequestsAsync(CancellationToken cancellationToken)
    {
        Guid patronId = currentUser.UserId ?? throw new InvalidOperationException("Requires a signed-in patron.");
        DateTimeOffset now = clock.GetUtcNow();
        // Sorted before the records are built: EF Core cannot translate ordering by a property of a constructed record.
        return await db.CancellationRequests.AsNoTracking()
            .Where(r => r.Status == CancellationRequestStatus.Pending)
            .Join(db.Orders.AsNoTracking().Where(o => o.PatronId == patronId && o.Status != OrderStatus.Cancelled && o.PickupAtUtc > now),
                r => r.OrderId, o => o.Id, (r, o) => new { o.Reference, r.Reason, r.CreatedAt, o.PickupAtUtc })
            .OrderBy(x => x.CreatedAt)
            .Select(x => new PendingCancellationRequest(x.Reference, x.Reason, x.CreatedAt, x.PickupAtUtc))
            .ToListAsync(cancellationToken);
    }

    private async Task<CancelOrderResult> CancelAsync(string orderReference, decimal expectedRefund, bool acceptRequest, CancellationToken cancellationToken)
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
            return await ResultAsync(CancelOrderOutcome.AlreadyCancelled, order.Id, feeRate, cancellationToken);
        }

        if (!order.CanBeCancelledAt(now))
        {
            return await ResultAsync(acceptRequest ? CancelOrderOutcome.NoOpenRequest : CancelOrderOutcome.PickupPassed, order.Id, feeRate, cancellationToken);
        }

        CancellationRequest? accepted = null;
        if (acceptRequest)
        {
            accepted = await PendingRequestAsync(order.Id, cancellationToken);
            if (accepted is null || !accepted.IsOpenAt(order, now))
            {
                return await ResultAsync(CancelOrderOutcome.NoOpenRequest, order.Id, feeRate, cancellationToken);
            }
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
                    return await ResultAsync(blocked.Value, order.Id, feeRate, cancellationToken);
                }
            }
        }

        Refund? refund = null;
        Payment? paid = null;
        if (order.Status == OrderStatus.Confirmed)
        {
            paid = await db.Payments.AsNoTracking().SingleAsync(p => p.OrderId == order.Id && p.Status == PaymentStatus.Succeeded, cancellationToken);
            if (feeRate is null && !acceptRequest)
            {
                return new CancelOrderResult(CancelOrderOutcome.Unavailable);
            }

            decimal expected = acceptRequest ? Paid(paid) : Quote(order, paid, feeRate!.Value).RefundAmount;
            if (expectedRefund != expected)
            {
                return await ResultAsync(CancelOrderOutcome.RefundChanged, order.Id, feeRate, cancellationToken);
            }
        }
        else if (expectedRefund != 0m)
        {
            db.ChangeTracker.Clear();
            return await ResultAsync(CancelOrderOutcome.RefundChanged, order.Id, feeRate, cancellationToken);
        }

        accepted?.Accept(order, now);
        order.Cancel(now);
        if (paid is not null)
        {
            refund = Refund.ForCancellation(order, paid, Refund.NewReference(), acceptRequest ? 0m : feeRate!.Value, requestedByRestaurant: acceptRequest);
            db.Refunds.Add(refund);
        }

        await LapseOtherRequestsAsync(order, now, cancellationToken);

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
            return await ResultAsync(outcome, order.Id, feeRate, cancellationToken);
        }

        LogCancelled(logger, order.Reference, acceptRequest, refund?.Reference);
        if (refund is not null)
        {
            // The cancellation is stored; if the provider cannot be reached now, the reconciliation job retries.
            await refunds.SubmitAsync(refund, paid!.Reference, CancellationToken.None);
        }

        return await ResultAsync(CancelOrderOutcome.Cancelled, order.Id, feeRate, cancellationToken);
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

    /// <summary>A manager's request still pending for an order the patron cancelled themselves is now moot.</summary>
    private async Task LapseOtherRequestsAsync(Order order, DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<CancellationRequest> pending = await db.CancellationRequests
            .Where(r => r.OrderId == order.Id && r.Status == CancellationRequestStatus.Pending)
            .ToListAsync(cancellationToken);
        foreach (CancellationRequest request in pending)
        {
            request.LapseIfDue(order, now);
        }
    }

    private Task<CancellationRequest?> PendingRequestAsync(int orderId, CancellationToken cancellationToken) =>
        db.CancellationRequests.SingleOrDefaultAsync(r => r.OrderId == orderId && r.Status == CancellationRequestStatus.Pending, cancellationToken);

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

    private async Task<CancelOrderResult> ResultAsync(CancelOrderOutcome outcome, int orderId, decimal? feeRate, CancellationToken cancellationToken) =>
        new(outcome, await ViewAsync(orderId, feeRate, cancellationToken));

    private async Task<OrderCancellationView> ViewAsync(int orderId, decimal? feeRate, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        Order current = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId, cancellationToken);
        Payment? paid = await db.Payments.AsNoTracking().SingleOrDefaultAsync(
            p => p.OrderId == current.Id && p.Status == PaymentStatus.Succeeded, cancellationToken);
        List<Refund> stored = await db.Refunds.AsNoTracking().Where(r => r.OrderId == current.Id).OrderBy(r => r.Id).ToListAsync(cancellationToken);
        CancellationRequest? pending = await db.CancellationRequests.AsNoTracking()
            .SingleOrDefaultAsync(r => r.OrderId == current.Id && r.Status == CancellationRequestStatus.Pending, cancellationToken);

        CancellationBlock? block = current.Status == OrderStatus.Cancelled ? CancellationBlock.AlreadyCancelled
            : !current.CanBeCancelledAt(now) ? CancellationBlock.PickupPassed
            : null;
        var none = new CancellationQuote(false, block, paid is not null, 0m, 0m, 0m, 0m, current.CurrencyCode);

        CancellationQuote quote = block is not null ? none
            : paid is null ? none with { CanCancel = true }
            : feeRate is null ? none      // a paid order waits until the fee rate setting is fixed
            : Quote(current, paid, feeRate.Value);

        CancellationRequestView? openRequest = pending is not null && pending.IsOpenAt(current, now) && block is null
            ? ToView(pending, isOpen: true, paid is null ? 0m : Paid(paid))
            : null;

        return new OrderCancellationView(current.Reference, current.Status, quote, openRequest, stored.Select(ToView).ToList());
    }

    private static decimal Paid(Payment payment) => MinorUnits.FromMinor(payment.AmountMinor, payment.CurrencyCode);

    private static CancellationQuote Quote(Order order, Payment payment, decimal feeRate)
    {
        decimal paid = Paid(payment);
        decimal fee = Refund.CancellationFee(order, feeRate);
        return new CancellationQuote(true, null, true, paid, feeRate, fee, paid - fee, payment.CurrencyCode);
    }

    internal static CancellationRequestView ToView(CancellationRequest request, bool isOpen, decimal refundAmount) =>
        new(request.Id, request.Reason, request.Status, isOpen, refundAmount, request.CreatedAt, request.ClosedAt);

    internal static RefundView ToView(Refund refund) => new(
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderReference} was cancelled (accepting a manager request: {AcceptedRequest}; refund {RefundReference}).")]
    private static partial void LogCancelled(ILogger logger, string orderReference, bool acceptedRequest, string? refundReference);

    [LoggerMessage(Level = LogLevel.Information, Message = "The patron declined the cancellation request for order {OrderReference}.")]
    private static partial void LogDeclined(ILogger logger, string orderReference);

    [LoggerMessage(Level = LogLevel.Error, Message = "The Refund.FeeRate setting is missing or invalid; paid orders cannot be cancelled by their patron until it is fixed.")]
    private static partial void LogFeeRateUnavailable(ILogger logger);
}
