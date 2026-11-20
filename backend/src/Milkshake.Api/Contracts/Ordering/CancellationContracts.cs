using System.Text.Json.Serialization;
using Milkshake.Application.Ordering;

namespace Milkshake.Api.Contracts.Ordering;

/// <summary>
/// POST /api/ordering/orders/{reference}/cancel. <see cref="ExpectedRefund"/> is the refund the patron confirmed
/// (0 for an unpaid order); it is only compared with the server's figure, never used as the amount.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CancelOrderRequest
{
    public decimal? ExpectedRefund { get; init; }
}

/// <summary>One refund as stored. A refund is complete only when <see cref="Status"/> is "Processed".</summary>
public sealed record RefundResponse(
    string Reference,
    string Kind,
    string Status,
    decimal Amount,
    decimal Fee,
    bool NeedsAttention,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    public static RefundResponse From(RefundView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new RefundResponse(view.Reference, view.Kind.ToString(), view.Status.ToString(), view.Amount, view.Fee,
            view.NeedsAttention, view.CreatedAt, view.CompletedAt);
    }
}

/// <summary>
/// GET /api/ordering/orders/{reference}/cancellation and the answer to a cancel request: whether the order can be
/// cancelled now, what the patron would get back (paid − fee), and the order's refunds. All amounts are the server's.
/// </summary>
public sealed record CancellationResponse(
    string OrderReference,
    string OrderStatus,
    bool CanCancel,
    string? CannotCancelReason,
    bool IsPaid,
    decimal PaidAmount,
    decimal FeeRate,
    decimal Fee,
    decimal RefundAmount,
    string CurrencyCode,
    IReadOnlyList<RefundResponse> Refunds)
{
    public static CancellationResponse From(OrderCancellationView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        CancellationQuote quote = view.Quote;
        return new CancellationResponse(view.OrderReference, view.OrderStatus.ToString(), quote.CanCancel, quote.Block?.ToString(),
            quote.IsPaid, quote.PaidAmount, quote.FeeRate, quote.Fee, quote.RefundAmount, quote.CurrencyCode,
            view.Refunds.Select(RefundResponse.From).ToList());
    }
}

public static class CancellationMessages
{
    public const string ExpectedRefundRequired = "Confirm the refund amount (0 for an unpaid order).";
    public const string RefundChangedTitle = "The refund for this order has changed.";
    public const string RefundChangedDetail = "Please check the new amount and confirm again. Nothing was cancelled.";
    public const string PickupPassedTitle = "The pickup time for this order has passed.";
    public const string PickupPassedDetail = "Orders can only be cancelled before their pickup time.";
    public const string PaymentInProgressTitle = "A payment for this order is being prepared.";
    public const string PaymentInProgressDetail = "Please wait a moment and try again.";
    public const string UnavailableTitle = "Cancellation is temporarily unavailable.";
    public const string UnavailableDetail = "Nothing was cancelled. Please try again in a few minutes.";
}
