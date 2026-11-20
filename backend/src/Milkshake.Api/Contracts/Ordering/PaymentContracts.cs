using System.Text.Json.Serialization;

namespace Milkshake.Api.Contracts.Ordering;

/// <summary>
/// POST /api/ordering/orders/{reference}/payments takes no data: the amount, currency and order all come from the
/// server. An empty body or {} is accepted; any field (an amount, a currency, a status) is rejected.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StartPaymentRequest;

/// <summary>Where to send the patron to pay. Nothing here means the order is paid.</summary>
public sealed record PaymentCheckoutResponse(string PaymentReference, Uri CheckoutUrl);

public static class PaymentMessages
{
    public const string AlreadyPaidTitle = "This order has already been paid.";
    public const string NotPayableTitle = "This order can no longer be paid.";
    public const string PickupPassedTitle = "The pickup time for this order has passed.";
    public const string PickupPassedDetail = "Please place a new order with a later pickup time.";
    public const string InProgressTitle = "A payment for this order is already being prepared.";
    public const string InProgressDetail = "Please wait a moment and try again.";
    public const string KeyUsedTitle = "This payment request has already been used.";
    public const string KeyUsedDetail = "Start the payment again.";
    public const string UnavailableTitle = "Payments are temporarily unavailable.";
    public const string UnavailableDetail = "You have not been charged. Please try again in a few minutes.";
}
