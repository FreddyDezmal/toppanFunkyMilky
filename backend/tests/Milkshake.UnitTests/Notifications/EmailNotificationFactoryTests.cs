using Milkshake.Application.Notifications;
using Milkshake.Domain.Common;
using Milkshake.Domain.Notifications;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;
using Milkshake.Domain.Pricing;

namespace Milkshake.UnitTests.Notifications;

/// <summary>M9 Step 7: each outbox row is built from the stored order, payment, refund or request (AC-9.3).</summary>
public class EmailNotificationFactoryTests
{
    private const string Zar = "ZAR";
    private const string Locale = "en-ZA";
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly NotificationRecipient Thandi = new("thandi@example.test", "Thandi");

    [Fact]
    public void Order_confirmed_copies_the_receipt_from_the_order_and_payment()
    {
        (Order order, Payment payment) = Paid();

        EmailNotification email = EmailNotificationFactory.OrderConfirmed(order, payment, Thandi, Locale, Now)!;

        Assert.Equal((NotificationKind.OrderConfirmed, "order-confirmed:" + order.Id, "thandi@example.test", "Thandi"),
            (email.Kind, email.NotificationKey, email.RecipientEmail, email.RecipientName));
        Assert.Equal(EmailPayloads.CurrentTemplateVersion, email.TemplateVersion);
        Assert.Null(email.RefundId);
        Assert.Null(email.CancellationRequestId);

        OrderConfirmedPayload payload = EmailPayloads.Deserialize<OrderConfirmedPayload>(email.Payload);
        Assert.Equal((order.Reference, "Branch A", payment.Reference, 59.80m, 52m, 7.80m, 0.15m, Locale),
            (payload.OrderReference, payload.RestaurantName, payload.PaymentReference, payload.Total, payload.Subtotal, payload.Vat, payload.VatRate, payload.Locale));
        ReceiptDrink drink = Assert.Single(payload.Drinks);
        Assert.Equal(new ReceiptDrink(1, "Vanilla", 52m, "Thick", 0m, "Oreo", 0m, 52m), drink);
        Assert.Equal(order.PickupAtUtc, payload.PickupAtUtc);
        Assert.Equal(Now, payload.PaidAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("a@example.test\r\nBcc: b@example.test")]
    [InlineData("a@example.test, b@example.test")]
    [InlineData("Thandi <a@example.test>")]
    public void An_invalid_recipient_gives_no_email(string address)
    {
        (Order order, Payment payment) = Paid();

        Assert.Null(EmailNotificationFactory.OrderConfirmed(order, payment, Thandi with { Email = address }, Locale, Now));
    }

    [Fact]
    public void Cancellation_request_on_a_paid_order_offers_back_everything_paid()
    {
        (Order order, Payment payment) = Paid();
        CancellationRequest request = CancellationRequest.Create(order, Guid.NewGuid(), "Blender broken", Now);

        EmailNotification email = EmailNotificationFactory.CancellationRequested(order, request, payment, Thandi, Locale, Now)!;

        Assert.Equal((NotificationKind.CancellationRequested, "cancellation-requested:" + request.Id), (email.Kind, email.NotificationKey));
        Assert.Equal(request.Id, email.CancellationRequestId);
        CancellationRequestedPayload payload = EmailPayloads.Deserialize<CancellationRequestedPayload>(email.Payload);
        Assert.Equal(("Blender broken", true, 59.80m, Zar), (payload.Reason, payload.IsPaid, payload.RefundAmount, payload.CurrencyCode));
    }

    [Fact]
    public void Cancellation_request_on_an_unpaid_order_offers_no_refund()
    {
        Order order = Placed();
        CancellationRequest request = CancellationRequest.Create(order, Guid.NewGuid(), "Closed early", Now);

        CancellationRequestedPayload payload = EmailPayloads.Deserialize<CancellationRequestedPayload>(
            EmailNotificationFactory.CancellationRequested(order, request, null, Thandi, Locale, Now)!.Payload);

        Assert.Equal((false, 0m), (payload.IsPaid, payload.RefundAmount));
    }

    [Fact]
    public void Patron_cancellation_copies_the_fee_and_refund_from_the_stored_refund()
    {
        (Order order, Payment payment) = Paid();
        order.Cancel(Now);
        Refund refund = Refund.ForCancellation(order, payment, Refund.NewReference(), 0.10m, requestedByRestaurant: false);

        EmailNotification email = EmailNotificationFactory.OrderCancelled(order, refund, false, Thandi, Locale, Now)!;

        Assert.Equal((NotificationKind.OrderCancelled, "order-cancelled:" + order.Id), (email.Kind, email.NotificationKey));
        Assert.Null(email.RefundId);
        OrderCancelledPayload payload = EmailPayloads.Deserialize<OrderCancelledPayload>(email.Payload);
        Assert.Equal((true, false, 59.80m, 5.20m, 54.60m, refund.Reference),
            (payload.WasPaid, payload.AtRestaurantRequest, payload.PaidAmount, payload.Fee, payload.RefundAmount, payload.RefundReference));
    }

    [Fact]
    public void Restaurant_cancellation_is_a_full_refund_at_the_restaurants_request()
    {
        (Order order, Payment payment) = Paid();
        order.Cancel(Now);
        Refund refund = Refund.ForCancellation(order, payment, Refund.NewReference(), 0.10m, requestedByRestaurant: true);

        OrderCancelledPayload payload = EmailPayloads.Deserialize<OrderCancelledPayload>(
            EmailNotificationFactory.OrderCancelled(order, refund, false, Thandi, Locale, Now)!.Payload);

        Assert.Equal((true, 0m, 59.80m), (payload.AtRestaurantRequest, payload.Fee, payload.RefundAmount));
    }

    [Fact]
    public void Unpaid_cancellation_has_no_refund_figures()
    {
        Order order = Placed();
        order.Cancel(Now);

        OrderCancelledPayload payload = EmailPayloads.Deserialize<OrderCancelledPayload>(
            EmailNotificationFactory.OrderCancelled(order, null, true, Thandi, Locale, Now)!.Payload);

        Assert.Equal((false, true, 0m, 0m, (string?)null), (payload.WasPaid, payload.AtRestaurantRequest, payload.RefundAmount, payload.Fee, payload.RefundReference));
    }

    [Theory]
    [InlineData(VerifiedRefundOutcome.Processed, NotificationKind.RefundProcessed, "refund-processed:")]
    [InlineData(VerifiedRefundOutcome.Failed, NotificationKind.RefundFailed, "refund-failed:")]
    public void Refund_outcome_uses_the_refund_status(VerifiedRefundOutcome outcome, NotificationKind kind, string keyPrefix)
    {
        (Order order, Refund refund) = Refunded();
        refund.ApplyVerifiedOutcome(outcome, refund.AmountMinor, 777, null, Now.AddHours(2));

        EmailNotification email = EmailNotificationFactory.RefundOutcome(order, refund, Thandi, Locale, Now)!;

        Assert.Equal((kind, keyPrefix + refund.Id, (int?)refund.Id), (email.Kind, email.NotificationKey, email.RefundId));
        RefundOutcomePayload payload = EmailPayloads.Deserialize<RefundOutcomePayload>(email.Payload);
        Assert.Equal((refund.Reference, RefundKind.PatronCancellation, 54.60m, 5.20m, Now.AddHours(2), "UTC"),
            (payload.RefundReference, payload.Kind, payload.Amount, payload.Fee, payload.CompletedAt, payload.TimeZone));
    }

    [Fact]
    public void A_refund_still_in_progress_has_no_outcome_email()
    {
        (Order order, Refund refund) = Refunded();

        Assert.Throws<InvalidOperationException>(() => EmailNotificationFactory.RefundOutcome(order, refund, Thandi, Locale, Now));
    }

    [Fact]
    public void Every_factory_email_renders()
    {
        (Order order, Payment payment) = Paid();
        CancellationRequest request = CancellationRequest.Create(order, Guid.NewGuid(), "Blender broken", Now);
        var site = new Uri("https://shop.example.test/");

        Assert.Contains(order.Reference, EmailTemplates.Render(EmailNotificationFactory.OrderConfirmed(order, payment, Thandi, Locale, Now)!, site).Text);
        Assert.Contains("Blender broken", EmailTemplates.Render(EmailNotificationFactory.CancellationRequested(order, request, payment, Thandi, Locale, Now)!, site).Text);

        (Order cancelled, Refund refund) = Refunded();
        Assert.Contains(refund.Reference, EmailTemplates.Render(EmailNotificationFactory.OrderCancelled(cancelled, refund, false, Thandi, Locale, Now)!, site).Text);
        refund.ApplyVerifiedOutcome(VerifiedRefundOutcome.Processed, refund.AmountMinor, 777, null, Now);
        Assert.Contains(refund.Reference, EmailTemplates.Render(EmailNotificationFactory.RefundOutcome(cancelled, refund, Thandi, Locale, Now)!, site).Text);
    }

    // ---------- Helpers ----------

    private static Money R(decimal amount) => new(amount, Zar);

    /// <summary>R52.00 + VAT R7.80 = R59.80; the patron's 10% fee is R5.20.</summary>
    private static Order Placed()
    {
        var drink = new PricedDrink(1,
            new PriceComponent(1, "Vanilla", R(52m)), new PriceComponent(2, "Thick", R(0m)), new PriceComponent(3, "Oreo", R(0m)), R(52m));
        var pricing = new PriceBreakdown(Zar, Now, [drink], R(52m), AppliedDiscount.None(Zar), R(52m), 0.15m, R(7.80m), R(59.80m));
        return Order.Place(Guid.NewGuid(), OrderReference.New(), new OrderRestaurant(1, "Branch A", null), Now.AddDays(1), "UTC",
            pricing, Guid.NewGuid(), new string('a', 64));
    }

    private static (Order Order, Payment Payment) Paid()
    {
        Order order = Placed();
        Payment payment = Payment.Start(order, Payment.NewReference(), Payment.PaystackProvider, Guid.NewGuid());
        payment.ApplyVerifiedOutcome(order, VerifiedOutcome.Paid, MinorUnits.ToMinor(order.Total, Zar), Zar, 4242, "Approved", false, Now);
        return (order, payment);
    }

    private static (Order Order, Refund Refund) Refunded()
    {
        (Order order, Payment payment) = Paid();
        order.Cancel(Now);
        return (order, Refund.ForCancellation(order, payment, Refund.NewReference(), 0.10m, requestedByRestaurant: false));
    }
}
