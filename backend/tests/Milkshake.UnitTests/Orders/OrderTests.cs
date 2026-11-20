using Milkshake.Domain.Common;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Pricing;

namespace Milkshake.UnitTests.Orders;

/// <summary>M7: order creation invariants, snapshots and the status lifecycle (AC-7.2, AC-7.3, AC-7.4).</summary>
public sealed class OrderTests
{
    private const string Zar = "ZAR";
    private static readonly Guid Patron = Guid.Parse("8d3f0d4e-1f1c-4a55-9c43-3c1f8a1b2c3d");
    private static readonly Guid Key = Guid.Parse("0f5b7c2a-9e1d-4b7a-8c3e-2d4f6a8b0c1e");
    private static readonly string Fingerprint = new('a', 64);
    private static readonly DateTimeOffset PricedAt = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Pickup = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly OrderRestaurant Branch = new(5, "Branch A", "1 Main Road");

    private static Money R(decimal amount) => new(amount, Zar);

    private static PricedDrink Drink(int number, decimal flavour, decimal consistency, decimal topping) =>
        new(number,
            new PriceComponent(1, "Vanilla", R(flavour)),
            new PriceComponent(2, "Thick", R(consistency)),
            new PriceComponent(3, "Oreo crumbs", R(topping)),
            R(flavour + consistency + topping));

    /// <summary>Two drinks: 46.00 + 40.00 = 86.00; no discount; VAT 15% = 12.90; total 98.90.</summary>
    private static PriceBreakdown Breakdown() =>
        new(Zar, PricedAt, [Drink(1, 30m, 6m, 10m), Drink(2, 32m, 0m, 8m)],
            R(86m), AppliedDiscount.None(Zar), R(86m), 0.15m, R(12.90m), R(98.90m));

    private static Order Place(PriceBreakdown? pricing = null) =>
        Order.Place(Patron, "MS-7KQ2-X9TD", Branch, Pickup, "Africa/Johannesburg", pricing ?? Breakdown(), Key, Fingerprint);

    [Fact]
    public void Place_CopiesEveryAmountAndName_AndStartsUnpaid()
    {
        Order order = Place();

        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(("MS-7KQ2-X9TD", Patron, 5, "Branch A", "1 Main Road"),
            (order.Reference, order.PatronId, order.RestaurantId, order.RestaurantName, order.RestaurantAddress));
        Assert.Equal((Pickup, "Africa/Johannesburg", PricedAt), (order.PickupAtUtc, order.PickupTimeZone, order.PricedAtUtc));
        Assert.Equal((Zar, 86m, 0m, 86m, 0.15m, 12.90m, 98.90m),
            (order.CurrencyCode, order.Subtotal, order.DiscountAmount, order.Taxable, order.VatRate, order.Vat, order.Total));
        Assert.Null(order.DiscountTierNumber);
        Assert.Null(order.DiscountRate);
        Assert.Equal((Key, Fingerprint), (order.IdempotencyKey, order.RequestFingerprint));

        Assert.Equal(2, order.Items.Count);
        OrderItem first = order.Items[0];
        Assert.Equal((1, 1, "Vanilla", 30m, 2, "Thick", 6m, 3, "Oreo crumbs", 10m, 46m),
            (first.Number, first.FlavourId, first.FlavourName, first.FlavourFee, first.ConsistencyId, first.ConsistencyName,
             first.ConsistencyFee, first.ToppingId, first.ToppingName, first.ToppingFee, first.Price));
        Assert.Equal(40m, order.Items[1].Price);
    }

    [Fact]
    public void Place_KeepsAppliedDiscountDetails()
    {
        // 86.00 at a fictional 10% tier = 8.60; taxable 77.40; VAT 11.61; total 89.01.
        PriceBreakdown pricing = Breakdown() with
        {
            Discount = new AppliedDiscount(R(8.60m), 2, 0.10m, false),
            Taxable = R(77.40m),
            Vat = R(11.61m),
            Total = R(89.01m),
        };

        Order order = Place(pricing);

        Assert.Equal((8.60m, 2, 0.10m, false, 89.01m),
            (order.DiscountAmount, order.DiscountTierNumber, order.DiscountRate, order.DiscountCapApplied, order.Total));
    }

    public static TheoryData<string> Inconsistent() => new()
    {
        "subtotal", "taxable", "total", "drink price", "numbering", "currency", "discount over subtotal", "tier without rate", "vat rate",
    };

    [Theory]
    [MemberData(nameof(Inconsistent))]
    public void Place_RejectsAnInconsistentBreakdown(string fault)
    {
        PriceBreakdown good = Breakdown();
        PriceBreakdown bad = fault switch
        {
            "subtotal" => good with { Subtotal = R(86.01m) },
            "taxable" => good with { Taxable = R(85m) },
            "total" => good with { Total = R(99m) },
            "drink price" => good with { Drinks = [good.Drinks[0] with { Price = R(45m) }, good.Drinks[1]] },
            "numbering" => good with { Drinks = [good.Drinks[0], good.Drinks[1] with { Number = 3 }] },
            "currency" => good with { Vat = new Money(12.90m, "USD") },
            "discount over subtotal" => good with { Discount = new AppliedDiscount(R(90m), 1, 0.05m, true), Taxable = R(0m), Vat = R(0m), Total = R(0m) },
            "tier without rate" => good with { Discount = new AppliedDiscount(R(0m), 1, null, false) },
            "vat rate" => good with { VatRate = 1.5m },
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };

        Assert.Throws<ArgumentException>(() => Place(bad));
    }

    [Fact]
    public void Place_RejectsAnEmptyOrder()
    {
        PriceBreakdown empty = Breakdown() with { Drinks = [], Subtotal = R(0m), Taxable = R(0m), Vat = R(0m), Total = R(0m) };

        Assert.Throws<ArgumentException>(() => Place(empty));
    }

    [Theory]
    [InlineData("MS-7KQ2-X9T")]
    [InlineData("ms-7kq2-x9td")]
    [InlineData("MS-7KQ2-X9TI")]
    [InlineData("XX-7KQ2-X9TD")]
    public void Place_RejectsAMalformedReference(string reference)
    {
        Assert.Throws<ArgumentException>(() =>
            Order.Place(Patron, reference, Branch, Pickup, "Africa/Johannesburg", Breakdown(), Key, Fingerprint));
    }

    [Fact]
    public void Place_RejectsMissingIdentityKeyFingerprintOrNonUtcPickup()
    {
        PriceBreakdown pricing = Breakdown();
        Assert.Throws<ArgumentException>(() => Order.Place(Guid.Empty, "MS-7KQ2-X9TD", Branch, Pickup, "UTC", pricing, Key, Fingerprint));
        Assert.Throws<ArgumentException>(() => Order.Place(Patron, "MS-7KQ2-X9TD", Branch, Pickup, "UTC", pricing, Guid.Empty, Fingerprint));
        Assert.Throws<ArgumentException>(() => Order.Place(Patron, "MS-7KQ2-X9TD", Branch, Pickup, "UTC", pricing, Key, new string('A', 64)));
        Assert.Throws<ArgumentException>(() =>
            Order.Place(Patron, "MS-7KQ2-X9TD", Branch, Pickup.ToOffset(TimeSpan.FromHours(2)), "UTC", pricing, Key, Fingerprint));
    }

    [Fact]
    public void Snapshot_IsACopy_NotAReferenceToTheBreakdown()
    {
        PriceBreakdown pricing = Breakdown();
        Order order = Place(pricing);

        // A later price (a different breakdown) cannot reach the stored order: it holds plain values only.
        _ = pricing with { Total = R(1m) };

        Assert.Equal(98.90m, order.Total);
        Assert.Equal("Vanilla", order.Items[0].FlavourName);
    }

    public static TheoryData<OrderStatus, OrderStatus> AllowedMoves() => new()
    {
        { OrderStatus.PendingPayment, OrderStatus.Confirmed },
        { OrderStatus.PendingPayment, OrderStatus.PaymentFailed },
        { OrderStatus.PendingPayment, OrderStatus.Cancelled },
        { OrderStatus.PaymentFailed, OrderStatus.Confirmed },
        { OrderStatus.PaymentFailed, OrderStatus.Cancelled },
        { OrderStatus.Confirmed, OrderStatus.Cancelled },
    };

    [Theory]
    [MemberData(nameof(AllowedMoves))]
    public void Transitions_AllowedMovesSucceed(OrderStatus from, OrderStatus to)
    {
        Order order = InStatus(from);

        Move(order, to);

        Assert.Equal(to, order.Status);
    }

    [Fact]
    public void Transitions_EveryOtherMoveIsRejected()
    {
        var allowed = AllowedMoves().Select(row => ((OrderStatus)row[0]!, (OrderStatus)row[1]!)).ToHashSet();
        int rejected = 0;

        foreach (OrderStatus from in Enum.GetValues<OrderStatus>())
        {
            foreach (OrderStatus to in Enum.GetValues<OrderStatus>().Where(to => to != OrderStatus.PendingPayment))
            {
                if (allowed.Contains((from, to)))
                {
                    continue;
                }

                Order order = InStatus(from);
                Assert.Throws<BusinessRuleException>(() => Move(order, to));
                Assert.Equal(from, order.Status);
                rejected++;
            }
        }

        // 4 statuses × 3 targets = 12 moves; 6 allowed (M8b adds Confirmed → Cancelled), 6 rejected (Cancelled is final).
        Assert.Equal(6, rejected);
        Assert.False(OrderStatusTransitions.IsAllowed(OrderStatus.Confirmed, OrderStatus.PendingPayment));
    }

    [Theory]
    [InlineData(OrderStatus.PendingPayment)]
    [InlineData(OrderStatus.PaymentFailed)]
    [InlineData(OrderStatus.Confirmed)]
    public void Cancel_IsAllowedUntilThePickupTime_ButNotFromThenOn(OrderStatus status)
    {
        Order early = InStatus(status);
        Order late = InStatus(status);

        Assert.True(early.CanBeCancelledAt(Pickup.AddTicks(-1)));
        Assert.False(late.CanBeCancelledAt(Pickup));
        early.Cancel(Pickup.AddTicks(-1));
        Assert.Throws<BusinessRuleException>(() => late.Cancel(Pickup));

        Assert.Equal((OrderStatus.Cancelled, status), (early.Status, late.Status));
        Assert.False(early.CanBeCancelledAt(PricedAt));
        Assert.Throws<BusinessRuleException>(() => early.Cancel(PricedAt));
    }

    [Fact]
    public void AwaitsPayment_OnlyForUnpaidOpenOrders()
    {
        Assert.True(OrderStatusTransitions.AwaitsPayment(OrderStatus.PendingPayment));
        Assert.True(OrderStatusTransitions.AwaitsPayment(OrderStatus.PaymentFailed));
        Assert.False(OrderStatusTransitions.AwaitsPayment(OrderStatus.Confirmed));
        Assert.False(OrderStatusTransitions.AwaitsPayment(OrderStatus.Cancelled));
    }

    [Fact]
    public void Reference_NewValuesMatchTheFormatAndDiffer()
    {
        var references = Enumerable.Range(0, 200).Select(_ => OrderReference.New()).ToList();

        Assert.All(references, reference => Assert.True(OrderReference.IsValid(reference)));
        Assert.Equal(200, references.Distinct().Count());
        Assert.Equal(OrderReference.Length, references[0].Length);
    }

    private static Order InStatus(OrderStatus status)
    {
        Order order = Place();
        switch (status)
        {
            case OrderStatus.Confirmed: order.MarkConfirmed(); break;
            case OrderStatus.PaymentFailed: order.MarkPaymentFailed(); break;
            case OrderStatus.Cancelled: order.Cancel(PricedAt); break;
        }

        return order;
    }

    private static void Move(Order order, OrderStatus to)
    {
        switch (to)
        {
            case OrderStatus.Confirmed: order.MarkConfirmed(); break;
            case OrderStatus.PaymentFailed: order.MarkPaymentFailed(); break;
            case OrderStatus.Cancelled: order.Cancel(PricedAt); break;
            default: throw new ArgumentOutOfRangeException(nameof(to));
        }
    }
}
