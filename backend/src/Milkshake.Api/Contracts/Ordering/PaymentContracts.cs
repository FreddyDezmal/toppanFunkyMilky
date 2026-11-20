using System.Text.Json.Serialization;
using Milkshake.Application.Payments;
using Milkshake.Domain.Orders;

namespace Milkshake.Api.Contracts.Ordering;

/// <summary>
/// POST /api/ordering/orders/{reference}/payments takes no data: the amount, currency and order all come from the
/// server. An empty body or {} is accepted; any field (an amount, a currency, a status) is rejected.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StartPaymentRequest;

/// <summary>Where to send the patron to pay. Nothing here means the order is paid.</summary>
public sealed record PaymentCheckoutResponse(string PaymentReference, Uri CheckoutUrl);

/// <summary>The latest payment attempt, as stored after checking it with the provider.</summary>
public sealed record PaymentAttemptResponse(string Reference, string Status, string? Message);

/// <summary>
/// POST /api/ordering/orders/{reference}/payments/refresh. Order status and payment attempt status are separate:
/// <see cref="IsPaid"/> is true only once a payment was verified with the provider; <see cref="Checking"/> means an
/// attempt is still open (the patron may still be paying, or verification has not finished).
/// </summary>
public sealed record PaymentStatusResponse(string OrderStatus, bool IsPaid, bool AwaitsPayment, bool Checking, PaymentAttemptResponse? Payment)
{
    public static PaymentStatusResponse From(PaymentStatusView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        bool open = view.PaymentStatus is Domain.Payments.PaymentStatus.Initiating or Domain.Payments.PaymentStatus.Pending;
        return new PaymentStatusResponse(
            view.OrderStatus.ToString(),
            view.OrderStatus == Domain.Orders.OrderStatus.Confirmed,
            OrderStatusTransitions.AwaitsPayment(view.OrderStatus),
            open && OrderStatusTransitions.AwaitsPayment(view.OrderStatus),
            view.PaymentReference is null ? null : new PaymentAttemptResponse(view.PaymentReference, view.PaymentStatus!.Value.ToString(), view.ProviderMessage));
    }
}

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
