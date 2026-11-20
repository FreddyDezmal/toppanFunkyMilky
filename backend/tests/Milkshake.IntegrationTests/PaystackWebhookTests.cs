using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api;
using Milkshake.Api.Contracts.Ordering;
using Milkshake.Application.Payments;
using Milkshake.Domain.Auditing;
using Milkshake.Domain.Notifications;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;
using Milkshake.IntegrationTests.Infrastructure;
using Milkshake.Infrastructure.Payments;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M8 Paystack webhook (decision P5): signature over the raw body, re-verification with the provider, amount and
/// currency checks, cancelled orders, duplicates and concurrent duplicates (AC-8.4), provider outages, no session or
/// CSRF needed, and nothing revealed in responses.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class PaystackWebhookTests(PostgresApiFactory factory) : IDisposable
{
    private const string WebhookPath = "/api/payments/paystack/webhook";
    private const string Key = "sk_test_integration_tests_only";
    private readonly FakePaymentGateway _gateway = new();
    private WebApplicationFactory<ApiAssemblyMarker>? _app;

    private WebApplicationFactory<ApiAssemblyMarker> App => _app ??= factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Payments:Paystack:ReturnBaseUrl", "https://localhost:5173/");
        builder.UseSetting("Payments:Paystack:SecretKey", Key);
        builder.ConfigureTestServices(services => services.AddSingleton<IPaymentGateway>(_gateway));
    });

    public void Dispose() => _app?.Dispose();

    [Fact]
    public async Task ASignedSuccess_VerifiedWithTheProvider_ConfirmsTheOrder()
    {
        (string order, string payment) = await PendingPaymentAsync();
        _gateway.OnVerify = _ => FakePaymentGateway.Paid(2990);

        using HttpResponseMessage response = await DeliverAsync(Event(payment, transactionId: 501));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        Assert.Equal((PaymentStatus.Succeeded, OrderStatus.Confirmed), await StateAsync(order, payment));
        Assert.Equal(1, await ReceiptsAsync(501));
        Assert.Equal([AuditActors.PaystackWebhook], await PaymentSuccessActorsAsync(payment));   // M11: never the browser user
    }

    [Fact]
    public async Task ARepeatedNotification_IsAcknowledged_ButProcessedOnlyOnce()
    {
        (string order, string payment) = await PendingPaymentAsync();
        _gateway.OnVerify = _ => FakePaymentGateway.Paid(2990);
        string body = Event(payment, transactionId: 502);

        using HttpResponseMessage first = await DeliverAsync(body);
        using HttpResponseMessage second = await DeliverAsync(body);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.StatusCode, second.StatusCode));
        Assert.Equal(1, _gateway.VerifyCalls);
        Assert.Equal(1, await ReceiptsAsync(502));
        Assert.Equal(1, await PaymentUpdateAuditsAsync(payment));
        Assert.Equal((PaymentStatus.Succeeded, OrderStatus.Confirmed), await StateAsync(order, payment));
        Assert.Equal(1, await ConfirmationEmailsAsync(order));   // M9: one receipt, however often Paystack notifies
    }

    [Fact]
    public async Task SimultaneousDuplicates_StoreOneReceipt_AndConfirmOnce()
    {
        (string order, string payment) = await PendingPaymentAsync();
        _gateway.OnVerify = _ => FakePaymentGateway.Paid(2990);
        string body = Event(payment, transactionId: 503);

        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => DeliverAsync(body)));
        try
        {
            // Each is either processed, a duplicate, or (if it lost a race on the order row) asked to be retried later.
            Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable }));
            Assert.Equal(1, await ReceiptsAsync(503));
            Assert.Equal((PaymentStatus.Succeeded, OrderStatus.Confirmed), await StateAsync(order, payment));
            Assert.Equal(1, await ConfirmationEmailsAsync(order));   // M9: simultaneous duplicates queue one receipt
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong key")]
    [InlineData("tampered body")]
    [InlineData("malformed")]
    public async Task UnsignedOrWronglySignedNotifications_AreRefused_AndChangeNothing(string fault)
    {
        (string order, string payment) = await PendingPaymentAsync();
        _gateway.OnVerify = _ => FakePaymentGateway.Paid(2990);
        string body = Event(payment, transactionId: 504);
        string? signature = fault switch
        {
            "missing" => null,
            "wrong key" => PaystackWebhookAuthenticatorTests.Sign(Encoding.UTF8.GetBytes(body), "sk_test_attacker"),
            "tampered body" => PaystackWebhookAuthenticatorTests.Sign(Encoding.UTF8.GetBytes(body.Replace("504", "999", StringComparison.Ordinal)), Key),
            _ => "not-hex",
        };

        using HttpResponseMessage response = await DeliverAsync(body, signature, sign: false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _gateway.VerifyCalls);
        Assert.Equal((PaymentStatus.Pending, OrderStatus.PendingPayment), await StateAsync(order, payment));
        Assert.Equal(0, await ReceiptsAsync(504));
    }

    [Fact]
    public async Task TheNotificationsOwnClaims_AreNotTrusted_OnlyTheProvidersVerification()
    {
        (string order, string payment) = await PendingPaymentAsync();

        // The (validly signed) notification says success, but the provider's verification says the payment failed.
        _gateway.OnVerify = _ => new GatewayVerification(true, VerifiedOutcome.Failed, 2990, "ZAR", 505, "Declined");
        using HttpResponseMessage response = await DeliverAsync(Event(payment, transactionId: 505));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((PaymentStatus.Failed, OrderStatus.PaymentFailed), await StateAsync(order, payment));
        Assert.Equal(0, await ConfirmationEmailsAsync(order));   // M9: a failed payment sends no receipt
    }

    [Theory]
    [InlineData(2989L, "ZAR")]
    [InlineData(2990L, "USD")]
    public async Task AMismatchedAmountOrCurrency_IsKeptForRefund_AndDoesNotConfirm(long amount, string currency)
    {
        (string order, string payment) = await PendingPaymentAsync();
        _gateway.OnVerify = _ => FakePaymentGateway.Paid(amount, currency);

        // Each case needs its own notification id: a repeated id is (correctly) treated as a duplicate.
        using HttpResponseMessage response = await DeliverAsync(Event(payment, transactionId: currency == "ZAR" ? 506 : 516));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((PaymentStatus.RequiresRefund, OrderStatus.PendingPayment), await StateAsync(order, payment));
        Assert.Equal(0, await ConfirmationEmailsAsync(order));   // M9: an unaccepted payment sends no receipt
    }

    [Fact]
    public async Task ALatePaymentForACancelledOrder_IsKeptForRefund_AndTheOrderStaysCancelled()
    {
        (string order, string payment) = await PendingPaymentAsync();
        await ExecuteAsync("UPDATE milkshake.orders SET status = 'Cancelled' WHERE reference = {0}", order);
        _gateway.OnVerify = _ => FakePaymentGateway.Paid(2990);

        using HttpResponseMessage response = await DeliverAsync(Event(payment, transactionId: 507));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((PaymentStatus.RequiresRefund, OrderStatus.Cancelled), await StateAsync(order, payment));
    }

    [Fact]
    public async Task WhenTheProviderCannotBeReached_PaystackIsAskedToRetry_AndTheRetryIsProcessed()
    {
        (string order, string payment) = await PendingPaymentAsync();
        string body = Event(payment, transactionId: 508);
        _gateway.OnVerify = _ => throw new PaymentGatewayException("down");

        using HttpResponseMessage first = await DeliverAsync(body);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(0, await ReceiptsAsync(508));

        _gateway.OnVerify = _ => FakePaymentGateway.Paid(2990);
        using HttpResponseMessage retry = await DeliverAsync(body);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal((PaymentStatus.Succeeded, OrderStatus.Confirmed), await StateAsync(order, payment));
    }

    [Fact]
    public async Task UnknownReferencesAndOtherEvents_AreAcknowledged_WithoutChanges()
    {
        using HttpResponseMessage unknown = await DeliverAsync(Event("MSP-ZZZZZZZZZZZZ", transactionId: 509));
        using HttpResponseMessage other = await DeliverAsync(Event("MSP-ZZZZZZZZZZZZ", transactionId: 510, eventType: "transfer.success"));

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (unknown.StatusCode, other.StatusCode));
        Assert.Equal(0, _gateway.VerifyCalls);
        Assert.Equal(0, await ReceiptsAsync(510));
    }

    [Fact]
    public async Task AnOversizedBody_IsRefused()
    {
        string body = Event("MSP-0123456789AB", transactionId: 511) + new string(' ', 70_000);

        using HttpResponseMessage response = await DeliverAsync(body);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    /// <summary>A Paystack-style event; the extra fields (amount, customer) are present but must never be relied on.</summary>
    private static string Event(string reference, long transactionId, string eventType = "charge.success") =>
        "{\"event\":\"" + eventType + "\",\"data\":{\"id\":" + transactionId.ToString(CultureInfo.InvariantCulture) +
        ",\"status\":\"success\",\"reference\":\"" + reference + "\",\"amount\":2990,\"currency\":\"ZAR\"," +
        "\"customer\":{\"email\":\"x@example.test\"}}}";

    /// <summary>Delivers like Paystack: no cookies, no CSRF token, only the signature header.</summary>
    private async Task<HttpResponseMessage> DeliverAsync(string body, string? signature = null, bool sign = true)
    {
        using HttpClient provider = App.CreateSecureClient(handleCookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(WebhookPath, UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        string? header = sign ? PaystackWebhookAuthenticatorTests.Sign(Encoding.UTF8.GetBytes(body), Key) : signature;
        if (header is not null)
        {
            request.Headers.TryAddWithoutValidation(PaystackWebhookAuthenticator.SignatureHeader, header);
        }

        return await provider.SendAsync(request);
    }

    /// <summary>A placed order with a Pending payment attempt created through the API.</summary>
    private async Task<(string Order, string Payment)> PendingPaymentAsync()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        using HttpClient patron = App.CreateSecureClient();
        using (HttpResponseMessage registered = await patron.PostWithCsrfAsync("/api/auth/register", RegisterBody.Valid()))
        {
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        }

        OrderingOptionsResponse options = await patron.GetFromJsonAsync<OrderingOptionsResponse>(new Uri("/api/ordering/options", UriKind.Relative))
            ?? throw new InvalidOperationException("No options returned.");
        var draft = new
        {
            restaurantId = menu.Restaurant.Id,
            pickupLocal = options.Pickup.LatestLocal,
            drinks = new[] { new { flavourId = menu.Flavour.Id, consistencyId = menu.Consistency.Id, toppingId = menu.Topping.Id } },
            expectedTotal = 29.90m,
            expectedCurrencyCode = "ZAR",
        };
        OrderResponse order = await PostAsync<OrderResponse>(patron, "/api/ordering/orders", JsonContent.Create(draft), HttpStatusCode.Created);
        PaymentCheckoutResponse checkout = await PostAsync<PaymentCheckoutResponse>(
            patron, $"/api/ordering/orders/{order.Reference}/payments", new StringContent("{}", Encoding.UTF8, "application/json"), HttpStatusCode.OK);
        return (order.Reference, checkout.PaymentReference);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, HttpContent content, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = content };
        request.Headers.Add("X-XSRF-TOKEN", await client.GetCsrfTokenAsync());
        request.Headers.Add(OrderMessages.IdempotencyKeyHeader, Guid.NewGuid().ToString());
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        return await response.ReadJsonAsync<T>();
    }

    private async Task<(PaymentStatus Payment, OrderStatus Order)> StateAsync(string orderReference, string paymentReference)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        PaymentStatus payment = await db.Payments.Where(p => p.Reference == paymentReference).Select(p => p.Status).SingleAsync();
        OrderStatus order = await db.Orders.Where(o => o.Reference == orderReference).Select(o => o.Status).SingleAsync();
        return (payment, order);
    }

    /// <summary>M9: order-confirmation emails queued for the order.</summary>
    private async Task<int> ConfirmationEmailsAsync(string orderReference)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        int orderId = await db.Orders.Where(o => o.Reference == orderReference).Select(o => o.Id).SingleAsync();
        return await db.EmailNotifications.CountAsync(n => n.OrderId == orderId && n.Kind == NotificationKind.OrderConfirmed);
    }

    private async Task<int> ReceiptsAsync(long transactionId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>().PaymentEvents.CountAsync(e => e.ProviderTransactionId == transactionId);
    }

    /// <summary>M11: who the payment's change to Succeeded is attributed to in the audit trail.</summary>
    private async Task<List<string?>> PaymentSuccessActorsAsync(string paymentReference)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        string id = (await db.Payments.Where(p => p.Reference == paymentReference).Select(p => p.Id).SingleAsync())
            .ToString(CultureInfo.InvariantCulture);
        List<AuditLog> updates = await db.AuditLogs
            .Where(a => a.TableName == "payments" && a.RecordId == id && a.Action == AuditAction.Update)
            .ToListAsync();
        return updates.Where(a => a.Changes.Contains("Succeeded", StringComparison.Ordinal)).Select(a => a.Actor).ToList();
    }

    private async Task<int> PaymentUpdateAuditsAsync(string paymentReference)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        string id = (await db.Payments.Where(p => p.Reference == paymentReference).Select(p => p.Id).SingleAsync())
            .ToString(CultureInfo.InvariantCulture);
        List<string> changes = await db.AuditLogs
            .Where(a => a.TableName == "payments" && a.RecordId == id && a.Action == AuditAction.Update)
            .Select(a => a.Changes)
            .ToListAsync();
        return changes.Count(c => c.Contains("Succeeded", StringComparison.Ordinal));
    }

    private async Task ExecuteAsync(string sql, string reference)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>().Database.ExecuteSqlRawAsync(sql, reference);
    }
}
