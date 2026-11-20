using System.Globalization;
using System.Text.Json.Serialization;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Orders;

namespace Milkshake.Api.Contracts.Ordering;

/// <summary>
/// POST /api/ordering/orders: the draft plus the total the patron confirmed. Identifiers only; unknown fields (a
/// price, a status, a customer id) are rejected. The expected total is compared with the server's price and never
/// used to calculate it. The Idempotency-Key header identifies the submission.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PlaceOrderRequest
{
    /// <summary>An active restaurant id.</summary>
    public int? RestaurantId { get; init; }

    /// <summary>Pickup date and time in the business time zone, "yyyy-MM-ddTHH:mm".</summary>
    public string? PickupLocal { get; init; }

    /// <summary>One entry per milkshake.</summary>
    public IReadOnlyList<DraftDrinkRequest?>? Drinks { get; init; }

    /// <summary>The estimated total shown to the patron when they chose to place the order.</summary>
    public decimal? ExpectedTotal { get; init; }

    /// <summary>The currency of <see cref="ExpectedTotal"/>.</summary>
    public string? ExpectedCurrencyCode { get; init; }

    /// <summary>Maps to the application model.</summary>
    public OrderDraft ToDraft() => new(RestaurantId, PickupLocal, Drinks?.Select(d => d?.ToSelection()).ToList() ?? []);
}

/// <summary>A choice as it was when the order was placed.</summary>
public sealed record OrderedChoiceResponse(int Id, string Name, decimal Fee);

/// <summary>One milkshake of a placed order, from the stored snapshot.</summary>
public sealed record OrderDrinkResponse(int Number, OrderedChoiceResponse Flavour, OrderedChoiceResponse Consistency, OrderedChoiceResponse Topping, decimal Price);

/// <summary>
/// A placed order, built only from what was stored when it was placed (never from today's catalogue). Status and
/// payment are shown separately: <see cref="Status"/> is the order lifecycle state and <see cref="IsPaid"/> is true only
/// once payment has been verified by the server (M8).
/// </summary>
public sealed record OrderResponse(
    string Reference,
    string Status,
    bool IsPaid,
    bool AwaitsPayment,
    OrderingRestaurantResponse Restaurant,
    DraftPickupResponse Pickup,
    IReadOnlyList<OrderDrinkResponse> Drinks,
    OrderTotalsResponse Totals,
    DateTimeOffset PricedAtUtc,
    DateTimeOffset PlacedAtUtc,
    string Note)
{
    public const string AwaitingPaymentNote = "Your order has been placed and is awaiting payment. It has not been paid.";

    public static OrderResponse From(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(order.PickupTimeZone);
        DateTime local = TimeZoneInfo.ConvertTime(order.PickupAtUtc, zone).DateTime;
        bool awaitsPayment = OrderStatusTransitions.AwaitsPayment(order.Status);

        return new OrderResponse(
            order.Reference,
            order.Status.ToString(),
            order.Status == OrderStatus.Confirmed,
            awaitsPayment,
            new OrderingRestaurantResponse(order.RestaurantId, order.RestaurantName, order.RestaurantAddress),
            new DraftPickupResponse(local.ToString(PickupRules.LocalFormat, CultureInfo.InvariantCulture), order.PickupAtUtc, order.PickupTimeZone),
            order.Items
                .OrderBy(i => i.Number)
                .Select(i => new OrderDrinkResponse(
                    i.Number,
                    new OrderedChoiceResponse(i.FlavourId, i.FlavourName, i.FlavourFee),
                    new OrderedChoiceResponse(i.ConsistencyId, i.ConsistencyName, i.ConsistencyFee),
                    new OrderedChoiceResponse(i.ToppingId, i.ToppingName, i.ToppingFee),
                    i.Price))
                .ToList(),
            new OrderTotalsResponse(
                order.CurrencyCode,
                order.Subtotal,
                new DiscountResponse(order.DiscountAmount, order.DiscountTierNumber, order.DiscountRate, order.DiscountCapApplied),
                order.Taxable,
                order.VatRate,
                order.Vat,
                order.Total),
            order.PricedAtUtc,
            order.CreatedAt,
            awaitsPayment ? AwaitingPaymentNote : string.Empty);
    }
}

/// <summary>One milkshake in an order-history row: the names as they were when the order was placed.</summary>
public sealed record OrderedDrinkSummaryResponse(int Number, string Flavour, string Consistency, string Topping);

/// <summary>
/// One row of the patron's order history (AC-12.1), from the stored snapshot. <see cref="PaymentStatus"/> is Paid, Unpaid
/// or Cancelled (URS §3.4.4); for a cancelled order <see cref="RefundStatus"/> says what happened to the money (NotPaid,
/// RefundInProgress, Refunded, RefundedLessFee or RefundFailed), otherwise it is null.
/// </summary>
public sealed record OrderSummaryResponse(
    string Reference,
    string Status,
    bool IsPaid,
    bool AwaitsPayment,
    string PaymentStatus,
    string? RefundStatus,
    string RestaurantName,
    DraftPickupResponse Pickup,
    int DrinkCount,
    IReadOnlyList<OrderedDrinkSummaryResponse> Drinks,
    decimal Total,
    string CurrencyCode,
    DateTimeOffset PlacedAtUtc)
{
    public static OrderSummaryResponse From(OrderHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        OrderResponse full = OrderResponse.From(entry.Order);

        return new OrderSummaryResponse(
            full.Reference, full.Status, full.IsPaid, full.AwaitsPayment,
            entry.Payment.Label.ToString(),
            entry.Payment.Refund == RefundLabel.None ? null : entry.Payment.Refund.ToString(),
            full.Restaurant.Name, full.Pickup, full.Drinks.Count,
            full.Drinks.Select(d => new OrderedDrinkSummaryResponse(d.Number, d.Flavour.Name, d.Consistency.Name, d.Topping.Name)).ToList(),
            full.Totals.Total, full.Totals.CurrencyCode, full.PlacedAtUtc);
    }
}

/// <summary>A page of the patron's order history, newest first. Pass <see cref="NextCursor"/> as <c>cursor</c> for the next page; null on the last.</summary>
public sealed record OrderHistoryResponse(IReadOnlyList<OrderSummaryResponse> Items, string? NextCursor);

/// <summary>Messages for order placement problems.</summary>
public static class OrderMessages
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public const string IdempotencyKeyRequired = "Send a new UUID in the Idempotency-Key header for each order, and reuse it only to retry that order.";

    public const string ExpectedTotalRequired = "The total you confirmed is required.";

    public const string PriceChangedTitle = "The price has changed.";

    public const string PriceChangedDetail = "Prices changed since you reviewed your order. Please check the new total and confirm it to place your order.";

    public const string KeyReusedTitle = "This request does not match the order it is retrying.";

    public const string KeyReusedDetail = "Start a new order submission.";

    public const string NotFoundTitle = "Order not found.";
}
