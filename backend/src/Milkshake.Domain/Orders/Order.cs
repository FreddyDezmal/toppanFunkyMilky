using Milkshake.Domain.Common;
using Milkshake.Domain.Pricing;

namespace Milkshake.Domain.Orders;

/// <summary>The restaurant as it was when the order was placed (name and address are snapshots).</summary>
public sealed record OrderRestaurant(int Id, string Name, string? Address);

/// <summary>
/// A placed order (M7). Every amount is copied from the server's <see cref="PriceBreakdown"/> when the order is
/// created and never changes afterwards (AC-7.2); only <see cref="Status"/> changes, and only along the allowed
/// transitions (AC-7.3). A new order is always <see cref="OrderStatus.PendingPayment"/> (AC-7.4).
/// </summary>
public sealed class Order : IHasTimestamps, IHasRowVersion
{
    public const int RestaurantNameMaxLength = 100;

    public const int RestaurantAddressMaxLength = 250;

    public const int TimeZoneMaxLength = 64;

    public const int FingerprintLength = 64;

    private readonly List<OrderItem> _items = [];

    private Order()
    {
    }

    public int Id { get; private set; }

    /// <summary>Public reference, e.g. MS-7KQ2-X9TD. Used in URLs and shown to the patron.</summary>
    public string Reference { get; private set; } = string.Empty;

    /// <summary>The patron who placed the order (an Identity user ID).</summary>
    public Guid PatronId { get; private set; }

    public OrderStatus Status { get; private set; }

    public int RestaurantId { get; private set; }

    public string RestaurantName { get; private set; } = string.Empty;

    public string? RestaurantAddress { get; private set; }

    /// <summary>The pickup instant (UTC).</summary>
    public DateTimeOffset PickupAtUtc { get; private set; }

    /// <summary>IANA time zone the patron chose the pickup time in (the Pickup.TimeZone setting at placement).</summary>
    public string PickupTimeZone { get; private set; } = string.Empty;

    public string CurrencyCode { get; private set; } = string.Empty;

    public decimal Subtotal { get; private set; }

    public decimal DiscountAmount { get; private set; }

    public int? DiscountTierNumber { get; private set; }

    public decimal? DiscountRate { get; private set; }

    public bool DiscountCapApplied { get; private set; }

    public decimal Taxable { get; private set; }

    /// <summary>VAT rate as a fraction (0.15 = 15%).</summary>
    public decimal VatRate { get; private set; }

    public decimal Vat { get; private set; }

    /// <summary>The payable amount. Fixed at placement.</summary>
    public decimal Total { get; private set; }

    /// <summary>The instant whose fees, VAT rate and discount settings were used.</summary>
    public DateTimeOffset PricedAtUtc { get; private set; }

    /// <summary>The patron's Idempotency-Key for the request that created the order (unique per patron).</summary>
    public Guid IdempotencyKey { get; private set; }

    /// <summary>SHA-256 (hex) of the canonical request, to tell a replay from a reused key with different content.</summary>
    public string RequestFingerprint { get; private set; } = string.Empty;

    public IReadOnlyList<OrderItem> Items => _items;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; private set; }

    /// <summary>Creates a new, unpaid order from a server-calculated price breakdown.</summary>
    public static Order Place(
        Guid patronId,
        string reference,
        OrderRestaurant restaurant,
        DateTimeOffset pickupAtUtc,
        string pickupTimeZone,
        PriceBreakdown pricing,
        Guid idempotencyKey,
        string requestFingerprint)
    {
        ArgumentNullException.ThrowIfNull(restaurant);
        ArgumentNullException.ThrowIfNull(pricing);
        ArgumentException.ThrowIfNullOrWhiteSpace(pickupTimeZone);
        ArgumentException.ThrowIfNullOrWhiteSpace(restaurant.Name);

        if (patronId == Guid.Empty)
        {
            throw new ArgumentException("An order needs a patron.", nameof(patronId));
        }

        if (!OrderReference.IsValid(reference))
        {
            throw new ArgumentException("The order reference is not in the MS-XXXX-XXXX format.", nameof(reference));
        }

        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        if (requestFingerprint is null || requestFingerprint.Length != FingerprintLength || !requestFingerprint.All(char.IsAsciiHexDigitLower))
        {
            throw new ArgumentException("The request fingerprint must be a lowercase SHA-256 hex string.", nameof(requestFingerprint));
        }

        if (pickupAtUtc.Offset != TimeSpan.Zero || pricing.PricedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Instants must be in UTC.", nameof(pickupAtUtc));
        }

        CheckPricing(pricing);

        var order = new Order
        {
            Reference = reference,
            PatronId = patronId,
            Status = OrderStatus.PendingPayment,
            RestaurantId = restaurant.Id,
            RestaurantName = restaurant.Name,
            RestaurantAddress = restaurant.Address,
            PickupAtUtc = pickupAtUtc,
            PickupTimeZone = pickupTimeZone,
            CurrencyCode = pricing.CurrencyCode,
            Subtotal = pricing.Subtotal.Amount,
            DiscountAmount = pricing.Discount.Amount.Amount,
            DiscountTierNumber = pricing.Discount.TierNumber,
            DiscountRate = pricing.Discount.Rate,
            DiscountCapApplied = pricing.Discount.CapApplied,
            Taxable = pricing.Taxable.Amount,
            VatRate = pricing.VatRate,
            Vat = pricing.Vat.Amount,
            Total = pricing.Total.Amount,
            PricedAtUtc = pricing.PricedAtUtc,
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = requestFingerprint,
        };

        foreach (PricedDrink drink in pricing.Drinks)
        {
            order._items.Add(new OrderItem(
                drink.Number,
                drink.Flavour.Id, drink.Flavour.Name, drink.Flavour.Fee.Amount,
                drink.Consistency.Id, drink.Consistency.Name, drink.Consistency.Fee.Amount,
                drink.Topping.Id, drink.Topping.Name, drink.Topping.Fee.Amount,
                drink.Price.Amount));
        }

        return order;
    }

    /// <summary>Payment verified by the server (M8 only).</summary>
    public void MarkConfirmed() => MoveTo(OrderStatus.Confirmed);

    /// <summary>A payment attempt failed (M8 only).</summary>
    public void MarkPaymentFailed() => MoveTo(OrderStatus.PaymentFailed);

    /// <summary>Whether the order may still be cancelled at this moment: not cancelled, and before the pickup time (B1.1).</summary>
    public bool CanBeCancelledAt(DateTimeOffset now) =>
        OrderStatusTransitions.IsAllowed(Status, OrderStatus.Cancelled) && now < PickupAtUtc;

    /// <summary>
    /// Cancels the order before its pickup time (B1.1). A paid order's refund is created by the caller in the same
    /// transaction (M8b).
    /// </summary>
    public void Cancel(DateTimeOffset now)
    {
        if (Status != OrderStatus.Cancelled && now >= PickupAtUtc)
        {
            throw new BusinessRuleException("An order cannot be cancelled once its pickup time has passed.");
        }

        MoveTo(OrderStatus.Cancelled);
    }

    private void MoveTo(OrderStatus next)
    {
        if (!OrderStatusTransitions.IsAllowed(Status, next))
        {
            throw new BusinessRuleException($"An order cannot change from {Status} to {next}.");
        }

        Status = next;
    }

    /// <summary>
    /// The breakdown must be internally consistent, so an order can never be stored with amounts that do not add up.
    /// The database repeats these checks as constraints.
    /// </summary>
    private static void CheckPricing(PriceBreakdown pricing)
    {
        if (pricing.Drinks.Count == 0)
        {
            throw new ArgumentException("An order needs at least one milkshake.", nameof(pricing));
        }

        Money[] amounts = [pricing.Subtotal, pricing.Discount.Amount, pricing.Taxable, pricing.Vat, pricing.Total];
        if (amounts.Concat(pricing.Drinks.Select(d => d.Price)).Any(m => m.CurrencyCode != pricing.CurrencyCode))
        {
            throw new ArgumentException("Every amount must be in the order's currency.", nameof(pricing));
        }

        bool numbered = pricing.Drinks.Select(d => d.Number).SequenceEqual(Enumerable.Range(1, pricing.Drinks.Count));
        bool drinksAddUp = pricing.Drinks.All(d =>
            d.Price.Amount == d.Flavour.Fee.Amount + d.Consistency.Fee.Amount + d.Topping.Fee.Amount);
        bool totalsAddUp =
            pricing.Subtotal.Amount == pricing.Drinks.Sum(d => d.Price.Amount) &&
            pricing.Discount.Amount.Amount <= pricing.Subtotal.Amount &&
            pricing.Taxable.Amount == pricing.Subtotal.Amount - pricing.Discount.Amount.Amount &&
            pricing.Total.Amount == pricing.Taxable.Amount + pricing.Vat.Amount;
        bool rateValid = pricing.VatRate is >= 0m and <= 1m &&
                         (pricing.Discount.TierNumber is null) == (pricing.Discount.Rate is null);

        if (!numbered || !drinksAddUp || !totalsAddUp || !rateValid)
        {
            throw new ArgumentException("The price breakdown is inconsistent.", nameof(pricing));
        }
    }
}
