using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;

namespace Milkshake.Application.Ordering;

/// <summary>Why an order can no longer be cancelled.</summary>
public enum CancellationBlock
{
    AlreadyCancelled,

    /// <summary>The pickup time has passed (B1.1).</summary>
    PickupPassed,
}

/// <summary>
/// What cancelling the order now would mean, calculated by the server (R5, R8). For an unpaid order nothing is refunded
/// and nothing is charged; for a paid order the patron gets back what they paid less the fee.
/// </summary>
public sealed record CancellationQuote(
    bool CanCancel,
    CancellationBlock? Block,
    bool IsPaid,
    decimal PaidAmount,
    decimal FeeRate,
    decimal Fee,
    decimal RefundAmount,
    string CurrencyCode);

/// <summary>One refund as stored. <see cref="NeedsAttention"/> means Paystack needs manual handling (Q4).</summary>
public sealed record RefundView(
    string Reference,
    RefundKind Kind,
    RefundStatus Status,
    decimal Amount,
    decimal Fee,
    bool NeedsAttention,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>The cancellation side of one of the patron's orders: what cancelling would mean, and any refunds.</summary>
public sealed record OrderCancellationView(string OrderReference, OrderStatus OrderStatus, CancellationQuote Quote, IReadOnlyList<RefundView> Refunds);

public enum CancelOrderOutcome
{
    /// <summary>The order is cancelled now (with a refund on its way if it was paid).</summary>
    Cancelled,

    /// <summary>It was already cancelled: a repeat of the same request is answered with the current state.</summary>
    AlreadyCancelled,

    NotFound,

    /// <summary>The pickup time has passed (B1.1).</summary>
    PickupPassed,

    /// <summary>
    /// The refund the patron confirmed differs from the server's (e.g. a payment has just come through, or the fee rate
    /// changed). Nothing was cancelled; the view holds the new figures (R6).
    /// </summary>
    RefundChanged,

    /// <summary>A payment for this order is being prepared at this moment; try again shortly.</summary>
    PaymentInProgress,

    /// <summary>The payment provider or the fee setting is unavailable; nothing was cancelled.</summary>
    Unavailable,
}

public sealed record CancelOrderResult(CancelOrderOutcome Outcome, OrderCancellationView? View = null);

/// <summary>Patron cancellation (M8b, B1.1, B1.3, B1.4, R4–R8). Only the signed-in patron's own orders.</summary>
public interface IOrderCancellationService
{
    /// <summary>The order's cancellation quote and refunds, or null if it is not one of the patron's orders.</summary>
    Task<OrderCancellationView?> GetAsync(string orderReference, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels the order if the refund the patron confirmed (0 for an unpaid order) still matches the server's. A paid
    /// order is cancelled together with its refund record before Paystack is asked to return the money.
    /// </summary>
    Task<CancelOrderResult> CancelAsync(string orderReference, decimal expectedRefund, CancellationToken cancellationToken);
}
