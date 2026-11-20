using Milkshake.Application.Ordering;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;

namespace Milkshake.Application.Management;

/// <summary>
/// One order as a manager sees it on Management → Orders (Q3): enough to identify it, contact the patron and see its
/// cancellation requests and refunds. Amounts are the stored snapshot.
/// </summary>
public sealed record ManagerOrderView(
    string Reference,
    OrderStatus Status,
    bool IsPaid,
    string PatronFirstName,
    string PatronEmail,
    string RestaurantName,
    DateTimeOffset PickupAtUtc,
    string PickupTimeZone,
    decimal Total,
    string CurrencyCode,
    DateTimeOffset PlacedAt,
    bool CanRequestCancellation,
    IReadOnlyList<CancellationRequestView> Requests,
    IReadOnlyList<RefundView> Refunds);

public enum RequestCancellationOutcome
{
    Requested,
    NotFound,

    /// <summary>The reason is missing, longer than 50 characters or not plain text.</summary>
    InvalidReason,

    /// <summary>The order is cancelled or its pickup time has passed.</summary>
    NotCancellable,

    /// <summary>A request is already waiting for the patron's answer.</summary>
    AlreadyRequested,
}

public sealed record RequestCancellationResult(RequestCancellationOutcome Outcome, ManagerOrderView? Order = null);

/// <summary>A refund a manager must handle by hand (Q4): failed at Paystack, or Paystack needs the patron's bank details.</summary>
public sealed record RefundNeedingAttention(
    string OrderReference,
    string RefundReference,
    RefundKind Kind,
    RefundStatus Status,
    decimal Amount,
    string CurrencyCode,
    string? ProviderMessage,
    DateTimeOffset CreatedAt);

/// <summary>Manager order support (M8b, B1.2, Q3, Q4). Managers never cancel directly: they ask the patron.</summary>
public interface IManagerOrderService
{
    Task<ManagerOrderView?> GetAsync(string orderReference, CancellationToken cancellationToken);

    /// <summary>Asks the patron to accept cancelling their order, with a reason of at most 50 characters (B1.2).</summary>
    Task<RequestCancellationResult> RequestCancellationAsync(string orderReference, string? reason, CancellationToken cancellationToken);

    /// <summary>Refunds needing manual handling, newest first (at most 100).</summary>
    Task<IReadOnlyList<RefundNeedingAttention>> RefundsNeedingAttentionAsync(CancellationToken cancellationToken);
}
