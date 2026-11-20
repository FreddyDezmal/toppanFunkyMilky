using Milkshake.Domain.Common;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;
using Milkshake.Domain.Pricing;

namespace Milkshake.UnitTests.Payments;

/// <summary>M8b refund rules: the B1.4 fee, full refunds for restaurant requests (Q1) and unaccepted payments (Q2), verified outcomes only (R4).</summary>
public sealed class RefundTests
{
    private const string Zar = "ZAR";
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    private static Money R(decimal amount) => new(amount, Zar);

    private static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>R52.00 + VAT R7.80 = R59.80 (5980 cents), the decision register's example.</summary>
    private static Order PlaceOrder(decimal taxable = 52m, decimal vat = 7.80m)
    {
        var drink = new PricedDrink(1,
            new PriceComponent(1, "Vanilla", R(taxable)), new PriceComponent(2, "Thick", R(0m)), new PriceComponent(3, "Oreo", R(0m)), R(taxable));
        var pricing = new PriceBreakdown(Zar, Now, [drink], R(taxable), AppliedDiscount.None(Zar), R(taxable), 0.15m, R(vat), R(taxable + vat));
        return Order.Place(Guid.NewGuid(), OrderReference.New(), new OrderRestaurant(1, "Branch A", null), Now.AddDays(1), "UTC",
            pricing, Guid.NewGuid(), new string('a', 64));
    }

    private static (Order Order, Payment Payment) PaidThenCancelled(decimal taxable = 52m, decimal vat = 7.80m)
    {
        Order order = PlaceOrder(taxable, vat);
        Payment payment = Payment.Start(order, Payment.NewReference(), Payment.PaystackProvider, Guid.NewGuid());
        long amount = MinorUnits.ToMinor(order.Total, Zar);
        payment.ApplyVerifiedOutcome(order, VerifiedOutcome.Paid, amount, Zar, 4242, "Approved", false, Now);
        order.Cancel(Now);
        return (order, payment);
    }

    private static Refund Patron() { (Order order, Payment payment) = PaidThenCancelled(); return Refund.ForCancellation(order, payment, Refund.NewReference(), 0.10m, requestedByRestaurant: false); }

    [Fact]
    public void APatronsCancellation_KeepsTenPercentOfTheVatExclusiveAmount()
    {
        Refund refund = Patron();

        // 10% of R52.00 = R5.20; R59.80 − R5.20 = R54.60.
        Assert.Equal((5980L, 520L, 5460L, 0.10m), (refund.PaidAmountMinor, refund.FeeMinor, refund.AmountMinor, refund.FeeRate));
        Assert.Equal((RefundKind.PatronCancellation, RefundStatus.Initiating, Zar), (refund.Kind, refund.Status, refund.CurrencyCode));
        Assert.True(Refund.IsValidReference(refund.Reference));
    }

    [Theory]
    [InlineData("33.33", "5.00", "0.10", 333)]   // 3.333 → 3.33
    [InlineData("33.35", "5.00", "0.10", 334)]   // 3.335 → 3.34 (half away from zero)
    [InlineData("52.00", "7.80", "0.00", 0)]
    [InlineData("52.00", "7.80", "0.50", 2600)]
    public void TheFee_IsRoundedOnceToCents_HalfAwayFromZero(string taxable, string vat, string rate, long expectedFee)
    {
        (Order order, Payment payment) = PaidThenCancelled(D(taxable), D(vat));

        Refund refund = Refund.ForCancellation(order, payment, Refund.NewReference(), D(rate), requestedByRestaurant: false);

        Assert.Equal(expectedFee, refund.FeeMinor);
        Assert.Equal(refund.PaidAmountMinor - expectedFee, refund.AmountMinor);
    }

    [Fact]
    public void ARestaurantRequestedCancellation_IsRefundedInFull()
    {
        (Order order, Payment payment) = PaidThenCancelled();

        Refund refund = Refund.ForCancellation(order, payment, Refund.NewReference(), 0.10m, requestedByRestaurant: true);

        Assert.Equal((RefundKind.RestaurantCancellation, 0L, 5980L, 0m), (refund.Kind, refund.FeeMinor, refund.AmountMinor, refund.FeeRate));
    }

    [Fact]
    public void APaymentTheOrderCouldNotAccept_IsRefundedInFull()
    {
        Order cancelledBeforePaying = PlaceOrder();
        Payment attempt = Payment.Start(cancelledBeforePaying, Payment.NewReference(), Payment.PaystackProvider, Guid.NewGuid());
        cancelledBeforePaying.Cancel(Now);
        attempt.ApplyVerifiedOutcome(cancelledBeforePaying, VerifiedOutcome.Paid, 5980, Zar, 99, null, false, Now);

        Refund refund = Refund.ForUnacceptedPayment(attempt, Refund.NewReference());

        Assert.Equal(PaymentStatus.RequiresRefund, attempt.Status);
        Assert.Equal((RefundKind.UnacceptedPayment, 0L, 5980L, 0m), (refund.Kind, refund.FeeMinor, refund.AmountMinor, refund.FeeRate));
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("0.51")]
    public void AFeeRateOutsideZeroToHalf_IsRefused(string rate)
    {
        (Order order, Payment payment) = PaidThenCancelled();

        Assert.Throws<BusinessRuleException>(() => Refund.ForCancellation(order, payment, Refund.NewReference(), D(rate), requestedByRestaurant: false));
    }

    [Fact]
    public void OnlyACancelledOrdersSuccessfulPayment_CanBeRefundedOnCancellation()
    {
        Order order = PlaceOrder();
        Payment payment = Payment.Start(order, Payment.NewReference(), Payment.PaystackProvider, Guid.NewGuid());
        payment.ApplyVerifiedOutcome(order, VerifiedOutcome.Paid, 5980, Zar, 1, null, false, Now);

        // Paid but not cancelled.
        Assert.Throws<BusinessRuleException>(() => Refund.ForCancellation(order, payment, Refund.NewReference(), 0.10m, false));

        // Cancelled but never paid.
        Order unpaid = PlaceOrder();
        Payment open = Payment.Start(unpaid, Payment.NewReference(), Payment.PaystackProvider, Guid.NewGuid());
        unpaid.Cancel(Now);
        Assert.Throws<BusinessRuleException>(() => Refund.ForCancellation(unpaid, open, Refund.NewReference(), 0.10m, false));

        // A succeeded payment is not "unaccepted".
        Assert.Throws<BusinessRuleException>(() => Refund.ForUnacceptedPayment(payment, Refund.NewReference()));
    }

    [Fact]
    public void AnInvalidReference_IsRefused()
    {
        (Order order, Payment payment) = PaidThenCancelled();

        Assert.Throws<ArgumentException>(() => Refund.ForCancellation(order, payment, "MSP-0123456789AB", 0.10m, false));
    }

    [Fact]
    public void AcceptedByTheProvider_IsPendingNotProcessed()
    {
        Refund refund = Patron();

        refund.Submitted(777);
        refund.Submitted(777);   // a repeated answer is harmless

        Assert.Equal((RefundStatus.Pending, 777L), (refund.Status, refund.ProviderRefundId));
        Assert.Null(refund.CompletedAt);
        Assert.Throws<BusinessRuleException>(() => refund.Submitted(778));
    }

    [Fact]
    public void OnlyAVerifiedResult_CompletesTheRefund_AndItIsFinal()
    {
        Refund refund = Patron();
        refund.Submitted(777);

        Assert.False(refund.ApplyVerifiedOutcome(VerifiedRefundOutcome.Pending, 5460, 777, null, Now));
        Assert.True(refund.ApplyVerifiedOutcome(VerifiedRefundOutcome.Processed, 5460, 777, "Processed", Now));
        Assert.False(refund.ApplyVerifiedOutcome(VerifiedRefundOutcome.Processed, 5460, 777, "Processed", Now));
        Assert.False(refund.ApplyVerifiedOutcome(VerifiedRefundOutcome.Failed, 5460, 777, "Failed", Now));

        Assert.Equal((RefundStatus.Processed, Now), (refund.Status, refund.CompletedAt));
    }

    [Fact]
    public void AFailedRefund_IsRecordedForManualHandling()
    {
        Refund refund = Patron();
        refund.Submitted(777);

        Assert.True(refund.ApplyVerifiedOutcome(VerifiedRefundOutcome.Failed, 5460, 777, "Card closed", Now));

        Assert.Equal((RefundStatus.Failed, "Card closed"), (refund.Status, refund.ProviderMessage));
    }

    [Fact]
    public void ARefundInterruptedBeforeTheProviderAnswered_IsCompletedByVerification()
    {
        Refund refund = Patron();

        Assert.True(refund.ApplyVerifiedOutcome(VerifiedRefundOutcome.Pending, 5460, 777, null, Now));
        Assert.Equal(RefundStatus.Pending, refund.Status);

        Refund other = Patron();
        Assert.True(other.ApplyVerifiedOutcome(VerifiedRefundOutcome.Processed, 5460, 778, null, Now));
        Assert.Equal((RefundStatus.Processed, 778L), (other.Status, other.ProviderRefundId));
    }

    [Fact]
    public void APendingResult_RecordsAChangedMessage_WithoutCompletingTheRefund()
    {
        Refund refund = Patron();
        refund.Submitted(777);

        Assert.True(refund.ApplyVerifiedOutcome(VerifiedRefundOutcome.Pending, 5460, 777, "Needs attention at Paystack.", Now));
        Assert.False(refund.ApplyVerifiedOutcome(VerifiedRefundOutcome.Pending, 5460, 777, "Needs attention at Paystack.", Now));

        Assert.Equal((RefundStatus.Pending, "Needs attention at Paystack."), (refund.Status, refund.ProviderMessage));
        Assert.Null(refund.CompletedAt);
    }

    [Fact]
    public void AProcessedAmountDifferentFromOurs_IsNeverAccepted()
    {
        Refund refund = Patron();
        refund.Submitted(777);

        Assert.Throws<BusinessRuleException>(() => refund.ApplyVerifiedOutcome(VerifiedRefundOutcome.Processed, 5980, 777, null, Now));
        Assert.Equal(RefundStatus.Pending, refund.Status);
    }

    [Fact]
    public void References_MatchTheFormatAndDiffer()
    {
        var references = Enumerable.Range(0, 200).Select(_ => Refund.NewReference()).ToList();

        Assert.All(references, reference => Assert.Matches(Refund.ReferencePattern, reference));
        Assert.Equal(200, references.Distinct().Count());
    }
}
