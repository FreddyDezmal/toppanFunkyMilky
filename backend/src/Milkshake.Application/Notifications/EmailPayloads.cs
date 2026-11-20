using System.Text.Json;
using Milkshake.Domain.Notifications;
using Milkshake.Domain.Payments;

namespace Milkshake.Application.Notifications;

/// <summary>
/// The content data of each email kind (template version 1), copied when the event happens from the stored order,
/// payment, refund or request – never from today's catalogue or settings (AC-9.3). Stored as JSON on the notification.
/// </summary>
public static class EmailPayloads
{
    public const int CurrentTemplateVersion = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T payload) where T : class => JsonSerializer.Serialize(payload, Json);

    public static T Deserialize<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, Json) ?? throw new EmailRenderException("The email payload is empty.");
}

/// <summary>Shared by every payload: how amounts and times are shown, as the app showed them at the time.</summary>
public interface IEmailPayload
{
    string OrderReference { get; }

    string CurrencyCode { get; }

    /// <summary>e.g. en-ZA (the Pricing.DisplayLocale setting at event time).</summary>
    string Locale { get; }
}

/// <summary>One milkshake on the receipt, as ordered.</summary>
public sealed record ReceiptDrink(
    int Number,
    string Flavour,
    decimal FlavourFee,
    string Consistency,
    decimal ConsistencyFee,
    string Topping,
    decimal ToppingFee,
    decimal Price);

/// <summary>Email 1 (D1): order and payment confirmation and final receipt – everything AC-9.2 lists.</summary>
public sealed record OrderConfirmedPayload(
    string OrderReference,
    string RestaurantName,
    string? RestaurantAddress,
    DateTimeOffset PickupAtUtc,
    string PickupTimeZone,
    IReadOnlyList<ReceiptDrink> Drinks,
    decimal Subtotal,
    decimal Discount,
    decimal VatRate,
    decimal Vat,
    decimal Total,
    string CurrencyCode,
    string PaymentReference,
    DateTimeOffset PaidAt,
    string Locale) : IEmailPayload;

/// <summary>Email 2: a manager asks the patron to accept cancelling (B1.2). Refund on acceptance = everything paid (Q1).</summary>
public sealed record CancellationRequestedPayload(
    string OrderReference,
    string Reason,
    DateTimeOffset PickupAtUtc,
    string PickupTimeZone,
    bool IsPaid,
    decimal RefundAmount,
    string CurrencyCode,
    string Locale) : IEmailPayload;

/// <summary>Email 3: the order was cancelled. The refund figures come from the stored refund, never recalculated.</summary>
public sealed record OrderCancelledPayload(
    string OrderReference,
    bool WasPaid,
    bool AtRestaurantRequest,
    decimal PaidAmount,
    decimal Fee,
    decimal RefundAmount,
    string? RefundReference,
    string CurrencyCode,
    string Locale) : IEmailPayload;

/// <summary>Emails 4 and 5: a refund Paystack confirmed as processed, or could not complete.</summary>
public sealed record RefundOutcomePayload(
    string OrderReference,
    string RefundReference,
    RefundKind Kind,
    decimal Amount,
    decimal Fee,
    DateTimeOffset CompletedAt,
    string TimeZone,
    string CurrencyCode,
    string Locale) : IEmailPayload;

/// <summary>The email cannot be rendered (unknown template version, unreadable payload): retrying cannot help.</summary>
public sealed class EmailRenderException : Exception
{
    public EmailRenderException()
    {
    }

    public EmailRenderException(string message)
        : base(message)
    {
    }

    public EmailRenderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The kinds this template version can render, with their payload types.</summary>
public static class EmailKinds
{
    public static Type PayloadType(NotificationKind kind) => kind switch
    {
        NotificationKind.OrderConfirmed => typeof(OrderConfirmedPayload),
        NotificationKind.CancellationRequested => typeof(CancellationRequestedPayload),
        NotificationKind.OrderCancelled => typeof(OrderCancelledPayload),
        NotificationKind.RefundProcessed or NotificationKind.RefundFailed => typeof(RefundOutcomePayload),
        _ => throw new EmailRenderException($"No template for {kind}."),
    };
}
