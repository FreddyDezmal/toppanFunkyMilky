using System.Text.Json.Serialization;
using Milkshake.Application.Management;

namespace Milkshake.Api.Contracts.Ordering;

/// <summary>POST /api/manager/orders/{reference}/cancellation-requests.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RequestCancellationRequest
{
    /// <summary>Shown to the patron: required, at most 50 characters, plain text.</summary>
    public string? Reason { get; init; }
}

/// <summary>An order as a manager sees it on Management → Orders.</summary>
public sealed record ManagerOrderResponse(
    string Reference,
    string Status,
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
    IReadOnlyList<CancellationRequestResponse> Requests,
    IReadOnlyList<RefundResponse> Refunds)
{
    public static ManagerOrderResponse From(ManagerOrderView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new ManagerOrderResponse(view.Reference, view.Status.ToString(), view.IsPaid, view.PatronFirstName, view.PatronEmail,
            view.RestaurantName, view.PickupAtUtc, view.PickupTimeZone, view.Total, view.CurrencyCode, view.PlacedAt,
            view.CanRequestCancellation, view.Requests.Select(CancellationRequestResponse.From).ToList(),
            view.Refunds.Select(RefundResponse.From).ToList());
    }
}

/// <summary>A refund needing manual handling (Q4).</summary>
public sealed record RefundNeedingAttentionResponse(
    string OrderReference,
    string RefundReference,
    string Kind,
    string Status,
    decimal Amount,
    string CurrencyCode,
    string? ProviderMessage,
    DateTimeOffset CreatedAt)
{
    public static RefundNeedingAttentionResponse From(RefundNeedingAttention refund)
    {
        ArgumentNullException.ThrowIfNull(refund);
        return new RefundNeedingAttentionResponse(refund.OrderReference, refund.RefundReference, refund.Kind.ToString(), refund.Status.ToString(),
            refund.Amount, refund.CurrencyCode, refund.ProviderMessage, refund.CreatedAt);
    }
}

public static class ManagerOrderMessages
{
    public const string ReasonInvalid = "Give a reason of 1 to 50 characters, on one line.";
    public const string AlreadyRequestedTitle = "A cancellation request is already waiting for the patron's answer.";
    public const string NotCancellableTitle = "This order can no longer be cancelled.";
    public const string NotCancellableDetail = "It is already cancelled, or its pickup time has passed.";
}
