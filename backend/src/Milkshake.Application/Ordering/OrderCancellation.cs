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

/// <summary>
/// A manager's cancellation request (B1.2). <see cref="IsOpen"/>: still waiting for the patron (pending and before the
/// pickup time). <see cref="RefundAmount"/>: what the patron gets back on accepting – everything paid (Q1).
/// </summary>
public sealed record CancellationRequestView(
    int Id,
    string Reason,
    CancellationRequestStatus Status,
    bool IsOpen,
    decimal RefundAmount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt);

/// <summary>
/// The cancellation side of one of the patron's orders: what cancelling would mean, a manager's open request (if any),
/// and any refunds.
/// </summary>
public sealed record OrderCancellationView(
    string OrderReference,
    OrderStatus OrderStatus,
    CancellationQuote Quote,
    CancellationRequestView? OpenRequest,
    IReadOnlyList<RefundView> Refunds);

/// <summary>An order with a manager's request waiting for the patron's answer (the Home page notice, Q3).</summary>
public sealed record PendingCancellationRequest(string OrderReference, string Reason, DateTimeOffset CreatedAt, DateTimeOffset PickupAtUtc);

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

    /// <summary>There is no manager request waiting for an answer (answered, lapsed or never made).</summary>
    NoOpenRequest,

    /// <summary>The patron declined the manager's request; the order stays as it was.</summary>
    Declined,
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

    /// <summary>
    /// Accepts the manager's open request (B1.2): the order is cancelled and, if paid, refunded in full (Q1). The refund
    /// the patron confirmed (everything paid, or 0) must still match the server's.
    /// </summary>
    Task<CancelOrderResult> AcceptRequestAsync(string orderReference, decimal expectedRefund, CancellationToken cancellationToken);

    /// <summary>Declines the manager's open request; the order is not changed.</summary>
    Task<CancelOrderResult> DeclineRequestAsync(string orderReference, CancellationToken cancellationToken);

    /// <summary>The patron's orders with a manager request waiting for an answer, oldest first.</summary>
    Task<IReadOnlyList<PendingCancellationRequest>> PendingRequestsAsync(CancellationToken cancellationToken);
}
