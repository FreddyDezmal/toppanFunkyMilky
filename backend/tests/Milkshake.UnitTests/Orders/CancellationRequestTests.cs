using Milkshake.Domain.Common;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Pricing;

namespace Milkshake.UnitTests.Orders;

/// <summary>M8b manager cancellation requests (B1.2, R2, R7): a reason is required, the patron decides, unanswered requests lapse at pickup.</summary>
public sealed class CancellationRequestTests
{
    private const string Zar = "ZAR";
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Pickup = Now.AddHours(2);
    private static readonly Guid Manager = Guid.Parse("5b1e2c3d-4f50-4a61-8b72-9c83d4e5f601");

    private static Money R(decimal amount) => new(amount, Zar);

    private static Order PlaceOrder()
    {
        var drink = new PricedDrink(1,
            new PriceComponent(1, "Vanilla", R(30m)), new PriceComponent(2, "Thick", R(12m)), new PriceComponent(3, "Oreo", R(10m)), R(52m));
        var pricing = new PriceBreakdown(Zar, Now, [drink], R(52m), AppliedDiscount.None(Zar), R(52m), 0.15m, R(7.80m), R(59.80m));
        return Order.Place(Guid.NewGuid(), OrderReference.New(), new OrderRestaurant(1, "Branch A", null), Pickup, "UTC",
            pricing, Guid.NewGuid(), new string('a', 64));
    }

    [Fact]
    public void Create_StoresATrimmedReason_AndWaitsForThePatron()
    {
        Order order = PlaceOrder();

        CancellationRequest request = CancellationRequest.Create(order, Manager, "  Machine broken  ", Now);

        Assert.Equal(("Machine broken", CancellationRequestStatus.Pending, Manager), (request.Reason, request.Status, request.RequestedBy));
        Assert.True(request.IsOpenAt(order, Now));
        Assert.Null(request.ClosedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("This reason is far too long to fit in fifty chars!!")]   // 51 characters
    [InlineData("Line one\nline two")]
    [InlineData("Tab\there")]
    public void Create_RequiresAShortPlainReason(string reason)
    {
        Assert.Throws<BusinessRuleException>(() => CancellationRequest.Create(PlaceOrder(), Manager, reason, Now));
    }

    [Fact]
    public void Create_AcceptsExactlyFiftyCharacters()
    {
        string reason = new('x', CancellationRequest.ReasonMaxLength);

        Assert.Equal(reason, CancellationRequest.Create(PlaceOrder(), Manager, reason, Now).Reason);
    }

    [Fact]
    public void Create_IsRefusedForCancelledOrders_AndOnceThePickupTimeHasPassed()
    {
        Order cancelled = PlaceOrder();
        cancelled.Cancel(Now);

        Assert.Throws<BusinessRuleException>(() => CancellationRequest.Create(cancelled, Manager, "Closed", Now));
        Assert.Throws<BusinessRuleException>(() => CancellationRequest.Create(PlaceOrder(), Manager, "Closed", Pickup));
        Assert.Throws<ArgumentException>(() => CancellationRequest.Create(PlaceOrder(), Guid.Empty, "Closed", Now));
    }

    [Fact]
    public void Create_IsAllowedForPaidOrders()
    {
        Order paid = PlaceOrder();
        paid.MarkConfirmed();

        Assert.Equal(CancellationRequestStatus.Pending, CancellationRequest.Create(paid, Manager, "Out of milk", Now).Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ThePatron_AcceptsOrDeclines_OnlyOnce(bool accept)
    {
        Order order = PlaceOrder();
        CancellationRequest request = CancellationRequest.Create(order, Manager, "Out of milk", Now);

        if (accept) { request.Accept(order, Now); } else { request.Decline(order, Now); }

        Assert.Equal(accept ? CancellationRequestStatus.Accepted : CancellationRequestStatus.Declined, request.Status);
        Assert.Equal(Now, request.ClosedAt);
        Assert.False(request.IsOpenAt(order, Now));
        Assert.Throws<BusinessRuleException>(() => request.Accept(order, Now));
        Assert.Throws<BusinessRuleException>(() => request.Decline(order, Now));
    }

    [Fact]
    public void AnUnansweredRequest_LapsesAtThePickupTime_AndCanThenNotBeAnswered()
    {
        Order order = PlaceOrder();
        CancellationRequest request = CancellationRequest.Create(order, Manager, "Out of milk", Now);

        Assert.False(request.LapseIfDue(order, Pickup.AddTicks(-1)));
        Assert.False(request.IsOpenAt(order, Pickup));
        Assert.Throws<BusinessRuleException>(() => request.Accept(order, Pickup));

        Assert.True(request.LapseIfDue(order, Pickup));
        Assert.False(request.LapseIfDue(order, Pickup));
        Assert.Equal((CancellationRequestStatus.Lapsed, Pickup), (request.Status, request.ClosedAt));
    }

    [Fact]
    public void ARequestForAnOrderThePatronCancelledThemselves_Lapses()
    {
        Order order = PlaceOrder();
        CancellationRequest request = CancellationRequest.Create(order, Manager, "Out of milk", Now);
        order.Cancel(Now);

        Assert.Throws<BusinessRuleException>(() => request.Accept(order, Now));
        Assert.True(request.LapseIfDue(order, Now));
        Assert.Equal(CancellationRequestStatus.Lapsed, request.Status);
    }

    [Fact]
    public void AnsweringWithAnotherOrder_IsRefused()
    {
        Order order = PlaceOrder();
        CancellationRequest request = CancellationRequest.Create(order, Manager, "Out of milk", Now);
        Order other = Order.Place(Guid.NewGuid(), OrderReference.New(), new OrderRestaurant(1, "Branch A", null), Pickup, "UTC",
            new PriceBreakdown(Zar, Now, [new PricedDrink(1, new PriceComponent(1, "V", R(1m)), new PriceComponent(2, "T", R(0m)), new PriceComponent(3, "O", R(0m)), R(1m))],
                R(1m), AppliedDiscount.None(Zar), R(1m), 0.15m, R(0.15m), R(1.15m)),
            Guid.NewGuid(), new string('a', 64));
        typeof(Order).GetProperty(nameof(Order.Id))!.SetValue(other, 99);

        Assert.Throws<ArgumentException>(() => request.Accept(other, Now));
    }
}
