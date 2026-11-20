using System.Globalization;
using System.Text.RegularExpressions;
using Milkshake.Application.Notifications;
using Milkshake.Domain.Notifications;
using Milkshake.Domain.Payments;

namespace Milkshake.UnitTests.Notifications;

/// <summary>Template version 1 of the five emails (M9 Step 6): content (AC-9.2, AC-9.3), escaping, links and wording.</summary>
public class EmailTemplateTests
{
    private const string Zone = "Africa/Johannesburg";
    private const string Script = "<script>alert('x')</script>";
    private static readonly Uri Site = new("https://shop.example.test/");
    private static readonly CultureInfo ZA = CultureInfo.GetCultureInfo("en-ZA");
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    // ---------- Order confirmed (receipt) ----------

    [Fact]
    public void Order_confirmed_shows_everything_the_receipt_needs()
    {
        RenderedEmail email = Render(NotificationKind.OrderConfirmed, Confirmed());

        Assert.Equal("Order MS-1001 confirmed – your receipt", email.Subject);
        foreach (string expected in new[]
                 {
                     "Hi Thandi,", "MS-1001", "Confirmed and paid", "Milky Shaky Rosebank", "1 Main Road, Rosebank",
                     "Milkshake 1", "Chocolate (" + Money(5m) + ")", "Thick (" + Money(3m) + ")", "Sprinkles (" + Money(2m) + ")",
                     "Milkshake 2", "Vanilla", "Subtotal: " + Money(60m), "Discount: − " + Money(6m), "VAT (15%): " + Money(7.04m),
                     "Total paid: " + Money(54m), "Payment reference: MSP-ABC123", "View your order: https://shop.example.test/orders/MS-1001",
                 })
        {
            Assert.Contains(expected, email.Text);
        }
    }

    [Fact]
    public void Order_confirmed_shows_the_pickup_time_in_the_restaurants_time_zone()
    {
        RenderedEmail email = Render(NotificationKind.OrderConfirmed, Confirmed());

        // 10:30 UTC is 12:30 in Johannesburg (UTC+2, no daylight saving).
        string expected = new DateTime(2026, 11, 21, 12, 30, 0).ToString("dddd d MMMM yyyy, HH:mm", ZA);
        Assert.Contains("Pickup time: " + expected, email.Text);
    }

    [Fact]
    public void Order_confirmed_without_discount_or_address_says_none_and_omits_the_address()
    {
        RenderedEmail email = Render(NotificationKind.OrderConfirmed, Confirmed() with { Discount = 0m, RestaurantAddress = null });

        Assert.Contains("Discount: None", email.Text);
        Assert.DoesNotContain("Address:", email.Text);
    }

    [Fact]
    public void Order_confirmed_with_one_drink_says_one_milkshake()
    {
        OrderConfirmedPayload payload = Confirmed() with { Drinks = [Drink(1, "Chocolate")] };

        Assert.Contains("1 MILKSHAKE" + Environment.NewLine, Render(NotificationKind.OrderConfirmed, payload).Text);
    }

    // ---------- Cancellation requested ----------

    [Fact]
    public void Cancellation_request_for_a_paid_order_states_the_reason_full_refund_and_deadline()
    {
        RenderedEmail email = Render(NotificationKind.CancellationRequested, Requested(isPaid: true));

        Assert.Equal("Order MS-1001: the restaurant has asked to cancel", email.Subject);
        Assert.Contains("Reason: \"The blender is broken\"", email.Text);
        Assert.Contains("everything you paid (" + Money(54m) + "), with no fee", email.Text);
        Assert.Contains("If you decline, your order stays as it is.", email.Text);
        Assert.Contains(new DateTime(2026, 11, 21, 12, 30, 0).ToString("dddd d MMMM yyyy, HH:mm", ZA), email.Text);
    }

    [Fact]
    public void Cancellation_request_for_an_unpaid_order_says_nothing_is_charged()
    {
        RenderedEmail email = Render(NotificationKind.CancellationRequested, Requested(isPaid: false));

        Assert.Contains("nothing is charged", email.Text);
        Assert.DoesNotContain("everything you paid", email.Text);
    }

    [Fact]
    public void Cancellation_request_has_no_accept_or_decline_link_only_the_order_page()
    {
        RenderedEmail email = Render(NotificationKind.CancellationRequested, Requested(isPaid: true));

        Assert.Equal("https://shop.example.test/orders/MS-1001", Assert.Single(Hrefs(email.Html)));
        Assert.Contains("sign in and open your order", email.Text);
        Assert.DoesNotContain("token", email.Html.ToLowerInvariant());
    }

    // ---------- Order cancelled ----------

    [Fact]
    public void Paid_cancellation_shows_fee_refund_and_that_the_refund_is_not_yet_complete()
    {
        RenderedEmail email = Render(NotificationKind.OrderCancelled, Cancelled(fee: 5.40m));

        Assert.Equal("Order MS-1001 has been cancelled", email.Subject);
        Assert.Contains("Amount paid: " + Money(54m), email.Text);
        Assert.Contains("Cancellation fee kept: " + Money(5.40m), email.Text);
        Assert.Contains("Refund: " + Money(48.60m), email.Text);
        Assert.Contains("Refund status: On its way – not yet completed", email.Text);
        Assert.Contains("Refund reference: MSR-XYZ", email.Text);
        Assert.DoesNotContain("has been refunded", email.Text);
        Assert.DoesNotContain("Amount refunded", email.Text);
    }

    [Fact]
    public void Cancellation_at_the_restaurants_request_says_so_and_shows_no_fee_row()
    {
        RenderedEmail email = Render(NotificationKind.OrderCancelled, Cancelled(fee: 0m) with { AtRestaurantRequest = true, RefundAmount = 54m });

        Assert.Contains("as you agreed to the restaurant's request", email.Text);
        Assert.DoesNotContain("Cancellation fee", email.Text);
        Assert.Contains("Refund: " + Money(54m), email.Text);
    }

    [Fact]
    public void Unpaid_cancellation_has_no_refund_section()
    {
        OrderCancelledPayload payload = Cancelled(fee: 0m) with { WasPaid = false, PaidAmount = 0m, RefundAmount = 0m, RefundReference = null };
        RenderedEmail email = Render(NotificationKind.OrderCancelled, payload);

        Assert.Contains("nothing was charged", email.Text);
        Assert.DoesNotContain("REFUND", email.Text);
    }

    // ---------- Refund outcomes ----------

    [Fact]
    public void Processed_refund_shows_amount_reference_and_completion_date()
    {
        RenderedEmail email = Render(NotificationKind.RefundProcessed, Outcome(RefundKind.PatronCancellation, fee: 5.40m));

        Assert.Equal("Refund for order MS-1001 completed", email.Subject);
        Assert.Contains("Your refund for order MS-1001 is complete", email.Text);
        Assert.Contains("Amount refunded: " + Money(48.60m), email.Text);
        Assert.Contains("Cancellation fee kept: " + Money(5.40m), email.Text);
        Assert.Contains("Refund reference: MSR-XYZ", email.Text);
        Assert.Contains("Completed on: " + new DateTime(2026, 11, 22, 11, 15, 0).ToString("dddd d MMMM yyyy, HH:mm", ZA), email.Text);
    }

    [Fact]
    public void Processed_refund_of_an_unaccepted_payment_explains_why()
    {
        RenderedEmail email = Render(NotificationKind.RefundProcessed, Outcome(RefundKind.UnacceptedPayment, fee: 0m));

        Assert.Contains("a payment for order MS-1001 that we could not accept", email.Text);
        Assert.DoesNotContain("Cancellation fee", email.Text);
    }

    [Fact]
    public void Failed_refund_asks_the_patron_to_contact_the_restaurant_and_never_for_card_or_bank_details()
    {
        RenderedEmail email = Render(NotificationKind.RefundFailed, Outcome(RefundKind.PatronCancellation, fee: 0m));

        Assert.Equal("Refund for order MS-1001: please contact us", email.Subject);
        Assert.Contains("Status: Not completed", email.Text);
        Assert.Contains("contact the restaurant and quote the refund reference", email.Text);
        Assert.Contains("We will never ask for your card or bank details by email.", email.Text);
        foreach (string forbidden in new[] { "account number", "card number", "reply with", "send us your", "cvv", "pin" })
        {
            Assert.DoesNotContain(forbidden, email.Text.ToLowerInvariant().Replace("never ask for your card or bank details", string.Empty, StringComparison.Ordinal));
        }
    }

    // ---------- Escaping, links and safety (every kind) ----------

    [Theory]
    [InlineData(NotificationKind.OrderConfirmed)]
    [InlineData(NotificationKind.CancellationRequested)]
    [InlineData(NotificationKind.OrderCancelled)]
    [InlineData(NotificationKind.RefundProcessed)]
    [InlineData(NotificationKind.RefundFailed)]
    public void Every_email_has_html_and_text_one_link_to_our_site_and_no_images_scripts_or_tracking(NotificationKind kind)
    {
        RenderedEmail email = Render(kind, PayloadFor(kind));

        Assert.NotEmpty(email.Text);
        Assert.Matches("^<!DOCTYPE html>", email.Html);
        Assert.Equal("https://shop.example.test/orders/MS-1001", Assert.Single(Hrefs(email.Html)));
        foreach (string forbidden in new[] { "<img", "<script", "<iframe", "<form", "<style", "src=", "javascript:", "onload", "onerror" })
        {
            Assert.DoesNotContain(forbidden, email.Html.ToLowerInvariant());
        }

        Assert.DoesNotContain("\r", email.Subject);
        Assert.DoesNotContain("\n", email.Subject);
    }

    [Fact]
    public void Script_in_the_recipient_name_is_encoded_in_html()
    {
        RenderedEmail email = Render(NotificationKind.OrderConfirmed, Confirmed(), name: "Thandi " + Script);

        Assert.DoesNotContain("<script", email.Html);
        Assert.Contains("&lt;script&gt;", email.Html);
    }

    [Fact]
    public void Script_in_the_cancellation_reason_is_encoded_in_html()
    {
        RenderedEmail email = Render(NotificationKind.CancellationRequested, Requested(isPaid: true) with { Reason = Script + "\" onmouseover=\"x" });

        Assert.DoesNotContain("<script", email.Html);
        Assert.DoesNotContain("\" onmouseover", email.Html);
        Assert.Contains("&lt;script&gt;", email.Html);
    }

    [Fact]
    public void Script_in_restaurant_and_flavour_names_is_encoded_in_html()
    {
        OrderConfirmedPayload payload = Confirmed() with { RestaurantName = Script, Drinks = [Drink(1, Script)] };
        RenderedEmail email = Render(NotificationKind.OrderConfirmed, payload);

        Assert.DoesNotContain("<script", email.Html);
    }

    [Fact]
    public void Line_breaks_in_a_reference_cannot_inject_headers_into_the_subject()
    {
        OrderCancelledPayload payload = Cancelled(fee: 0m) with { OrderReference = "MS-1\r\nBcc: victim@example.test" };
        RenderedEmail email = Render(NotificationKind.OrderCancelled, payload);

        Assert.DoesNotContain("\r", email.Subject);
        Assert.DoesNotContain("\n", email.Subject);
    }

    [Theory]
    [InlineData("../admin", "https://shop.example.test/orders/..%2Fadmin")]
    [InlineData("MS-1?x=1#y", "https://shop.example.test/orders/MS-1%3Fx%3D1%23y")]
    [InlineData("//evil.example", "https://shop.example.test/orders/%2F%2Fevil.example")]
    public void The_order_link_escapes_the_reference_and_stays_on_our_site(string reference, string expected)
    {
        Uri link = EmailTemplates.OrderLink(Site, reference);

        Assert.Equal(expected, link.AbsoluteUri);
        Assert.Equal(Site.Host, link.Host);
    }

    [Fact]
    public void The_order_link_keeps_a_base_path()
    {
        Assert.Equal("https://shop.example.test/app/orders/MS-1", EmailTemplates.OrderLink(new Uri("https://shop.example.test/app/"), "MS-1").AbsoluteUri);
    }

    [Fact]
    public void A_missing_name_gets_a_neutral_greeting()
    {
        Assert.Matches("^Hello,", Render(NotificationKind.OrderConfirmed, Confirmed(), name: " ").Text);
    }

    // ---------- Formatting ----------

    [Fact]
    public void A_currency_other_than_the_locales_is_shown_with_its_code()
    {
        RenderedEmail email = Render(NotificationKind.RefundFailed, Outcome(RefundKind.PatronCancellation, fee: 0m) with { CurrencyCode = "USD" });

        Assert.Contains("Amount: USD " + 54m.ToString("N2", ZA), email.Text);
    }

    [Fact]
    public void An_unknown_time_zone_falls_back_to_utc()
    {
        RenderedEmail email = Render(NotificationKind.RefundProcessed, Outcome(RefundKind.PatronCancellation, fee: 0m) with { TimeZone = "Mars/Olympus" });

        Assert.Contains(new DateTime(2026, 11, 22, 9, 15, 0).ToString("dddd d MMMM yyyy, HH:mm", ZA) + " (UTC)", email.Text);
    }

    [Fact]
    public void An_unknown_locale_still_renders()
    {
        RenderedEmail email = Render(NotificationKind.RefundFailed, Outcome(RefundKind.PatronCancellation, fee: 0m) with { Locale = "xx-NOPE-zz" });

        Assert.Contains("Amount: ZAR 54.00", email.Text);
    }

    // ---------- Versions and payloads ----------

    [Fact]
    public void An_unknown_template_version_cannot_be_rendered()
    {
        EmailNotification notification = Notification(NotificationKind.OrderConfirmed, EmailPayloads.Serialize(Confirmed()), version: 2);

        Assert.Throws<EmailRenderException>(() => EmailTemplates.Render(notification, Site));
    }

    [Fact]
    public void An_unreadable_payload_cannot_be_rendered()
    {
        EmailNotification notification = Notification(NotificationKind.OrderConfirmed, "{not json");

        Assert.Throws<EmailRenderException>(() => EmailTemplates.Render(notification, Site));
    }

    [Theory]
    [InlineData(NotificationKind.OrderCancelled, "{}")]
    [InlineData(NotificationKind.OrderConfirmed, "{\"orderReference\":\"MS-1\",\"total\":29.90}")]
    [InlineData(NotificationKind.CancellationRequested, "{\"orderReference\":\"MS-1\",\"reason\":null,\"pickupAtUtc\":\"2026-11-21T10:30:00Z\",\"pickupTimeZone\":\"UTC\",\"isPaid\":true,\"refundAmount\":1,\"currencyCode\":\"ZAR\",\"locale\":\"en-ZA\"}")]
    public void A_payload_with_missing_or_null_fields_cannot_be_rendered(NotificationKind kind, string payload)
    {
        EmailNotification notification = Notification(kind, payload);

        Assert.Throws<EmailRenderException>(() => EmailTemplates.Render(notification, Site));
    }

    [Fact]
    public void A_null_payload_cannot_be_rendered()
    {
        EmailNotification notification = Notification(NotificationKind.RefundFailed, "null");

        Assert.Throws<EmailRenderException>(() => EmailTemplates.Render(notification, Site));
    }

    [Fact]
    public void Payloads_round_trip_through_json()
    {
        OrderConfirmedPayload payload = Confirmed();

        OrderConfirmedPayload copy = EmailPayloads.Deserialize<OrderConfirmedPayload>(EmailPayloads.Serialize(payload));

        Assert.Equal(payload.Total, copy.Total);
        Assert.Equal(payload.PickupAtUtc, copy.PickupAtUtc);
        Assert.Equal(payload.Drinks.Count, copy.Drinks.Count);
        Assert.Equal(payload.Drinks[1], copy.Drinks[1]);
    }

    [Fact]
    public void Each_kind_has_a_payload_type()
    {
        Assert.Equal(typeof(OrderConfirmedPayload), EmailKinds.PayloadType(NotificationKind.OrderConfirmed));
        Assert.Equal(typeof(CancellationRequestedPayload), EmailKinds.PayloadType(NotificationKind.CancellationRequested));
        Assert.Equal(typeof(OrderCancelledPayload), EmailKinds.PayloadType(NotificationKind.OrderCancelled));
        Assert.Equal(typeof(RefundOutcomePayload), EmailKinds.PayloadType(NotificationKind.RefundProcessed));
        Assert.Equal(typeof(RefundOutcomePayload), EmailKinds.PayloadType(NotificationKind.RefundFailed));
        Assert.Throws<EmailRenderException>(() => EmailKinds.PayloadType((NotificationKind)99));
    }

    // ---------- Helpers ----------

    private static string Money(decimal amount) => amount.ToString("C", ZA);

    private static List<string> Hrefs(string html) =>
        Regex.Matches(html, "href=\"([^\"]*)\"").Select(m => m.Groups[1].Value).ToList();

    private static RenderedEmail Render<T>(NotificationKind kind, T payload, string name = "Thandi")
        where T : class =>
        EmailTemplates.Render(Notification(kind, EmailPayloads.Serialize(payload), name: name), Site);

    private static EmailNotification Notification(NotificationKind kind, string payload, int version = EmailPayloads.CurrentTemplateVersion, string name = "Thandi") =>
        EmailNotification.Create(kind, "test:1", 1, null, null, "thandi@example.test", name, version, payload, Now);

    private static object PayloadFor(NotificationKind kind) => kind switch
    {
        NotificationKind.OrderConfirmed => Confirmed(),
        NotificationKind.CancellationRequested => Requested(isPaid: true),
        NotificationKind.OrderCancelled => Cancelled(fee: 5.40m),
        _ => Outcome(RefundKind.PatronCancellation, fee: 5.40m),
    };

    private static ReceiptDrink Drink(int number, string flavour) => new(number, flavour, 5m, "Thick", 3m, "Sprinkles", 2m, 30m);

    private static OrderConfirmedPayload Confirmed() => new(
        "MS-1001",
        "Milky Shaky Rosebank",
        "1 Main Road, Rosebank",
        new DateTimeOffset(2026, 11, 21, 10, 30, 0, TimeSpan.Zero),
        Zone,
        [Drink(1, "Chocolate"), Drink(2, "Vanilla")],
        60m,
        6m,
        0.15m,
        7.04m,
        54m,
        "ZAR",
        "MSP-ABC123",
        new DateTimeOffset(2026, 11, 20, 8, 5, 0, TimeSpan.Zero),
        "en-ZA");

    private static CancellationRequestedPayload Requested(bool isPaid) => new(
        "MS-1001", "The blender is broken", new DateTimeOffset(2026, 11, 21, 10, 30, 0, TimeSpan.Zero), Zone, isPaid, isPaid ? 54m : 0m, "ZAR", "en-ZA");

    private static OrderCancelledPayload Cancelled(decimal fee) => new("MS-1001", true, false, 54m, fee, 54m - fee, "MSR-XYZ", "ZAR", "en-ZA");

    private static RefundOutcomePayload Outcome(RefundKind kind, decimal fee) => new(
        "MS-1001", "MSR-XYZ", kind, 54m - fee, fee, new DateTimeOffset(2026, 11, 22, 9, 15, 0, TimeSpan.Zero), Zone, "ZAR", "en-ZA");
}
