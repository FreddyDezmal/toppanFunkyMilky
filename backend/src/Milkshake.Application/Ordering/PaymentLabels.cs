using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;

namespace Milkshake.Application.Ordering;

/// <summary>The payment label shown in patron history and manager reports (URS §3.4.4: Paid, Unpaid, Cancelled).</summary>
public enum PaymentLabel
{
    Unpaid,
    Paid,
    Cancelled,
}

/// <summary>What happened to the money of a cancelled order (M12 decision R3). <see cref="None"/> for other orders.</summary>
public enum RefundLabel
{
    None,

    /// <summary>Cancelled before any payment was accepted.</summary>
    NotPaid,

    /// <summary>A refund is stored or accepted by the provider but not yet confirmed.</summary>
    RefundInProgress,

    /// <summary>The provider confirmed the full amount was returned.</summary>
    Refunded,

    /// <summary>The provider confirmed the amount less the cancellation fee was returned (B1.4).</summary>
    RefundedLessFee,

    /// <summary>The provider could not return the money; handled manually (Q4).</summary>
    RefundFailed,
}

/// <summary>An order's payment state for display.</summary>
public sealed record PaymentState(PaymentLabel Label, RefundLabel Refund);

/// <summary>The stored facts about one refund that the label needs.</summary>
public sealed record RefundFacts(int Id, RefundKind Kind, RefundStatus Status, long FeeMinor, DateTimeOffset CreatedAt);

/// <summary>
/// One rule for patron history and manager reports (M12 decision R3), from the stored order status and refund records
/// only: never from the browser, the audit history or current prices.
/// </summary>
public static class PaymentLabels
{
    public static PaymentState For(OrderStatus status, IReadOnlyCollection<RefundFacts> refunds)
    {
        ArgumentNullException.ThrowIfNull(refunds);

        return status switch
        {
            OrderStatus.PendingPayment or OrderStatus.PaymentFailed => new PaymentState(PaymentLabel.Unpaid, RefundLabel.None),
            OrderStatus.Confirmed => new PaymentState(PaymentLabel.Paid, RefundLabel.None),
            OrderStatus.Cancelled => new PaymentState(PaymentLabel.Cancelled, CancelledRefund(refunds)),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown order status."),
        };
    }

    /// <summary>
    /// The cancellation's own refund decides; without one, the newest refund of a payment the order could not accept
    /// (e.g. money that arrived after cancelling); without any refund the order was never paid.
    /// </summary>
    private static RefundLabel CancelledRefund(IReadOnlyCollection<RefundFacts> refunds)
    {
        RefundFacts? refund = Newest(refunds.Where(r => r.Kind is RefundKind.PatronCancellation or RefundKind.RestaurantCancellation))
            ?? Newest(refunds);

        return refund?.Status switch
        {
            null => RefundLabel.NotPaid,
            RefundStatus.Initiating or RefundStatus.Pending => RefundLabel.RefundInProgress,
            RefundStatus.Processed => refund.FeeMinor > 0 ? RefundLabel.RefundedLessFee : RefundLabel.Refunded,
            RefundStatus.Failed => RefundLabel.RefundFailed,
            _ => throw new ArgumentOutOfRangeException(nameof(refunds), refund.Status, "Unknown refund status."),
        };
    }

    private static RefundFacts? Newest(IEnumerable<RefundFacts> refunds) =>
        refunds.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).FirstOrDefault();
}
