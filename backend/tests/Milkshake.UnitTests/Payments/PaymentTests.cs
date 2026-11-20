using Milkshake.Domain.Common;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;
using Milkshake.Domain.Pricing;

namespace Milkshake.UnitTests.Payments;

/// <summary>M8 payment rules: exact amount from the order, verified outcomes only, one success per order (P2, P3, P6).</summary>
public sealed class PaymentTests
{
    private const string Zar = "ZAR";
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    private static Money R(decimal amount) => new(amount, Zar);

    /// <summary>An unpaid order: 52.00 + VAT 7.80 = 59.80 (5980 cents).</summary>
    private static Order UnpaidOrder()
    {
        var drink = new PricedDrink(1,
            new PriceComponent(1, "Vanilla", R(30m)), new PriceComponent(2, "Thick", R(12m)), new PriceComponent(3, "Oreo", R(10m)), R(52m));
        var pricing = new PriceBreakdown(Zar, Now, [drink], R(52m), AppliedDiscount.None(Zar), R(52m), 0.15m, R(7.80m), R(59.80m));
        return Order.Place(Guid.NewGuid(), OrderReference.New(), new OrderRestaurant(1, "Branch A", null), Now.AddDays(1), "UTC",
            pricing, Guid.NewGuid(), new string('a', 64));
    }

    private static Payment Start(Order order) => Payment.Start(order, Payment.NewReference(), Payment.PaystackProvider, Guid.NewGuid());

    private static bool Apply(Payment payment, Order order, VerifiedOutcome outcome, long amount = 5980, string currency = Zar, bool otherSuccess = false) =>
        payment.ApplyVerifiedOutcome(order, outcome, amount, currency, 4242, "Approved", otherSuccess, Now);

    [Fact]
    public void Start_CopiesTheOrdersTotalInCents_AndStartsInitiating()
    {
        Order order = UnpaidOrder();
        Payment payment = Start(order);

        Assert.Equal((5980L, Zar, PaymentStatus.Initiating), (payment.AmountMinor, payment.CurrencyCode, payment.Status));
        Assert.True(Payment.IsValidReference(payment.Reference));
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Cancelled)]
    public void Start_RefusesOrdersThatDoNotAwaitPayment(OrderStatus status)
    {
        Order order = UnpaidOrder();
        if (status == OrderStatus.Confirmed) { order.MarkConfirmed(); } else { order.Cancel(Now); }

        Assert.Throws<BusinessRuleException>(() => Start(order));
    }

    [Fact]
    public void Start_AllowsARetryAfterAFailedPayment()
    {
        Order order = UnpaidOrder();
        order.MarkPaymentFailed();

        Assert.Equal(PaymentStatus.Initiating, Start(order).Status);
    }

    [Theory]
    [InlineData("https://checkout.paystack.com/abc123", true)]
    [InlineData("http://checkout.paystack.com/abc123", false)]
    [InlineData("https://checkout.paystack.com.evil.example/abc", false)]
    [InlineData("https://evil.example/https://checkout.paystack.com", false)]
    public void CheckoutCreated_AcceptsOnlyHttpsOnTheProvidersHost(string url, bool accepted)
    {
        Payment payment = Start(UnpaidOrder());

        if (accepted)
        {
            payment.CheckoutCreated(new Uri(url), "checkout.paystack.com");
            Assert.Equal(PaymentStatus.Pending, payment.Status);
        }
        else
        {
            Assert.Throws<BusinessRuleException>(() => payment.CheckoutCreated(new Uri(url), "checkout.paystack.com"));
            Assert.Equal(PaymentStatus.Initiating, payment.Status);
        }
    }

    [Fact]
    public void VerifiedPaid_WithTheExactAmount_ConfirmsTheOrder_Once()
    {
        Order order = UnpaidOrder();
        Payment payment = Start(order);

        Assert.True(Apply(payment, order, VerifiedOutcome.Paid));
        Assert.False(Apply(payment, order, VerifiedOutcome.Paid));

        Assert.Equal((PaymentStatus.Succeeded, OrderStatus.Confirmed, 4242L), (payment.Status, order.Status, payment.ProviderTransactionId!.Value));
        Assert.Equal(Now, payment.VerifiedAt);
    }

    [Theory]
    [InlineData(5979L, "ZAR")]
    [InlineData(5981L, "ZAR")]
    [InlineData(5980L, "USD")]
    public void VerifiedPaid_WithAMismatchedAmountOrCurrency_IsKeptForRefund_AndTheOrderStaysUnpaid(long amount, string currency)
    {
        Order order = UnpaidOrder();
        Payment payment = Start(order);

        Apply(payment, order, VerifiedOutcome.Paid, amount, currency);

        Assert.Equal((PaymentStatus.RequiresRefund, RefundReason.AmountMismatch, OrderStatus.PendingPayment),
            (payment.Status, payment.RefundReason!.Value, order.Status));
    }

    [Fact]
    public void ASecondVerifiedPayment_ForAPaidOrder_IsKeptForRefund()
    {
        Order order = UnpaidOrder();
        Payment first = Start(order);
        Apply(first, order, VerifiedOutcome.Paid);
        // Started while the order was still unpaid (ids are unset in unit tests, so any unpaid order stands in).
        Payment second = Start(UnpaidOrder());

        Apply(second, order, VerifiedOutcome.Paid, otherSuccess: true);

        Assert.Equal((PaymentStatus.RequiresRefund, RefundReason.AlreadyPaid), (second.Status, second.RefundReason!.Value));
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void ALatePayment_ForACancelledOrder_IsKeptForRefund_AndDoesNotConfirmIt()
    {
        Order order = UnpaidOrder();
        Payment payment = Start(order);
        order.Cancel(Now);

        Apply(payment, order, VerifiedOutcome.Paid);

        Assert.Equal((PaymentStatus.RequiresRefund, RefundReason.OrderNotPayable, OrderStatus.Cancelled),
            (payment.Status, payment.RefundReason!.Value, order.Status));
    }

    [Fact]
    public void VerifiedFailure_MarksTheOrderPaymentFailed_AndAllowsALaterSuccess()
    {
        Order order = UnpaidOrder();
        Payment failed = Start(order);
        Apply(failed, order, VerifiedOutcome.Failed);
        Assert.Equal((PaymentStatus.Failed, OrderStatus.PaymentFailed), (failed.Status, order.Status));

        Payment retry = Start(order);
        Apply(retry, order, VerifiedOutcome.Paid);
        Assert.Equal((PaymentStatus.Succeeded, OrderStatus.Confirmed), (retry.Status, order.Status));
    }

    [Fact]
    public void Abandonment_LeavesTheOrderUnpaid_AndAPaymentLaterReportedAsPaidStillCounts()
    {
        Order order = UnpaidOrder();
        Payment payment = Start(order);

        Apply(payment, order, VerifiedOutcome.Abandoned);
        Assert.Equal((PaymentStatus.Abandoned, OrderStatus.PendingPayment), (payment.Status, order.Status));

        // Out of order: the provider later reports the same transaction as paid; the provider is authoritative.
        Apply(payment, order, VerifiedOutcome.Paid);
        Assert.Equal((PaymentStatus.Succeeded, OrderStatus.Confirmed), (payment.Status, order.Status));
    }

    [Fact]
    public void AFailureOnASupersededAttempt_DoesNotMarkTheOrderFailed()
    {
        Order order = UnpaidOrder();
        Payment old = Start(order);
        old.Supersede();

        Apply(old, order, VerifiedOutcome.Failed);

        Assert.Equal((PaymentStatus.Failed, OrderStatus.PendingPayment), (old.Status, order.Status));
    }

    [Fact]
    public void FinalStatuses_IgnoreLaterResults_AndOpenResultsChangeNothing()
    {
        Order order = UnpaidOrder();
        Payment payment = Start(order);

        Assert.False(Apply(payment, order, VerifiedOutcome.Open));
        Assert.Equal(PaymentStatus.Initiating, payment.Status);

        Apply(payment, order, VerifiedOutcome.Paid);
        Assert.False(Apply(payment, order, VerifiedOutcome.Failed));
        Assert.False(Apply(payment, order, VerifiedOutcome.Abandoned));
        Assert.Equal((PaymentStatus.Succeeded, OrderStatus.Confirmed), (payment.Status, order.Status));
    }

    [Fact]
    public void OnlyOpenAttemptsCanBeSuperseded()
    {
        Order order = UnpaidOrder();
        Payment payment = Start(order);
        payment.Supersede();

        Assert.Throws<BusinessRuleException>(() => payment.Supersede());
    }

    [Theory]
    [InlineData(59.80, 5980L)]
    [InlineData(0.01, 1L)]
    [InlineData(1234567.89, 123456789L)]
    public void MinorUnits_AreExactForZar(double amount, long cents)
    {
        Assert.Equal(cents, MinorUnits.ToMinor((decimal)amount, Zar));
        Assert.Equal((decimal)amount, MinorUnits.FromMinor(cents, Zar));
    }

    [Fact]
    public void MinorUnits_RefuseUnknownCurrenciesFractionsOfACentAndZero()
    {
        Assert.Throws<ArgumentException>(() => MinorUnits.ToMinor(10m, "USD"));
        Assert.Throws<ArgumentException>(() => MinorUnits.ToMinor(10.005m, Zar));
        Assert.Throws<ArgumentException>(() => MinorUnits.ToMinor(0m, Zar));
    }
}
