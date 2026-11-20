using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api;
using Milkshake.Api.Contracts.Ordering;
using Milkshake.Application.Payments;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;
using Milkshake.IntegrationTests.Infrastructure;
using Milkshake.Infrastructure.Payments;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M8b refund completion (R4, R7, Q2, Q4): signed refund notifications re-verified with Paystack, and the reconciliation
/// job retrying refunds Paystack has not accepted, checking pending ones, refunding late and duplicate payments in full,
/// and closing manager requests at the pickup time. A controllable clock drives the waiting periods.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class RefundCompletionTests(PostgresApiFactory factory) : IDisposable
{
    private const string Key = "sk_test_integration_tests_only";
    private readonly FakePaymentGateway _gateway = new();
    private readonly SettableTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private WebApplicationFactory<ApiAssemblyMarker>? _app;

    private WebApplicationFactory<ApiAssemblyMarker> App => _app ??= factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Payments:Paystack:ReturnBaseUrl", "https://localhost:5173/");
        builder.UseSetting("Payments:Paystack:SecretKey", Key);
        builder.UseSetting("Payments:Reconciliation:BatchSize", "10000");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IPaymentGateway>(_gateway);
            services.AddSingleton<TimeProvider>(_clock);
        });
    });

    public void Dispose() => _app?.Dispose();

    // ---- webhooks --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AProcessedNotification_IsVerifiedWithPaystack_AndCompletesTheRefund()
    {
        (HttpClient patron, string order, _, Refund refund) = await CancelledPaidOrderAsync();
        using (patron)
        {
            _gateway.OnGetRefund = reference => reference == refund.Reference
                ? new GatewayRefund(true, VerifiedRefundOutcome.Processed, 2730, refund.ProviderRefundId, null)
                : new GatewayRefund(true, VerifiedRefundOutcome.Pending, 0, null, null);

            // Paystack may echo our reference in another case.
            using HttpResponseMessage delivered = await DeliverAsync(RefundEvent("refund.processed", refund.Reference.ToLowerInvariant(), null));
            using HttpResponseMessage repeated = await DeliverAsync(RefundEvent("refund.processed", refund.Reference, null));

            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (delivered.StatusCode, repeated.StatusCode));
            Refund stored = await RefundAsync(refund.Reference);
            Assert.Equal(RefundStatus.Processed, stored.Status);
            Assert.NotNull(stored.CompletedAt);
            Assert.Equal("Processed", Assert.Single((await GetAsync(patron, order)).Refunds).Status);
        }
    }

    [Fact]
    public async Task TheNotificationsOwnClaim_IsNotTrusted_OnlyPaystacksAnswer()
    {
        (HttpClient patron, _, _, Refund refund) = await CancelledPaidOrderAsync();
        using (patron)
        {
            // The event says "processed", but Paystack's API still says pending.
            using HttpResponseMessage delivered = await DeliverAsync(RefundEvent("refund.processed", refund.Reference, null));

            Assert.Equal(HttpStatusCode.OK, delivered.StatusCode);
            Assert.Equal(RefundStatus.Pending, (await RefundAsync(refund.Reference)).Status);
        }
    }

    [Fact]
    public async Task ANotificationWithoutOurReference_IsMatchedByThePayment_AndAFailureIsListedForManagers()
    {
        (HttpClient patron, string order, string payment, Refund refund) = await CancelledPaidOrderAsync();
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        using (patron)
        {
            _gateway.OnGetRefund = reference => reference == refund.Reference
                ? new GatewayRefund(true, VerifiedRefundOutcome.Failed, 2730, refund.ProviderRefundId, "Refund failed at Paystack.")
                : new GatewayRefund(true, VerifiedRefundOutcome.Pending, 0, null, null);

            using HttpResponseMessage delivered = await DeliverAsync(RefundEvent("refund.failed", null, payment));

            Assert.Equal(HttpStatusCode.OK, delivered.StatusCode);
            Assert.Equal(RefundStatus.Failed, (await RefundAsync(refund.Reference)).Status);
            using HttpResponseMessage listed = await manager.Client.GetAsync(new Uri("/api/manager/refunds/attention", UriKind.Relative));
            Assert.Contains(await listed.ReadJsonAsync<List<RefundNeedingAttentionResponse>>(), r => r.OrderReference == order && r.Status == "Failed");
        }
    }

    [Fact]
    public async Task UnknownRefunds_AreAcknowledged_AndUnsignedOnesRefused_WithoutChanges()
    {
        (HttpClient patron, _, _, Refund refund) = await CancelledPaidOrderAsync();
        using (patron)
        {
            _gateway.OnGetRefund = _ => new GatewayRefund(true, VerifiedRefundOutcome.Processed, 2730, null, null);

            using HttpResponseMessage unknown = await DeliverAsync(RefundEvent("refund.processed", "MSR-ZZZZZZZZZZZZ", "MSP-ZZZZZZZZZZZZ"));
            using HttpResponseMessage unsigned = await DeliverAsync(RefundEvent("refund.processed", refund.Reference, null), sign: false);

            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Unauthorized), (unknown.StatusCode, unsigned.StatusCode));
            Assert.Equal(RefundStatus.Pending, (await RefundAsync(refund.Reference)).Status);
        }
    }

    [Fact]
    public async Task WhenPaystackCannotBeAsked_TheNotificationIsRetried_AndTheRetryIsProcessed()
    {
        (HttpClient patron, _, _, Refund refund) = await CancelledPaidOrderAsync();
        using (patron)
        {
            _gateway.OnGetRefund = _ => throw new PaymentGatewayException("down");
            using HttpResponseMessage first = await DeliverAsync(RefundEvent("refund.processed", refund.Reference, null));
            _gateway.OnGetRefund = _ => new GatewayRefund(true, VerifiedRefundOutcome.Processed, 2730, refund.ProviderRefundId, null);
            using HttpResponseMessage retry = await DeliverAsync(RefundEvent("refund.processed", refund.Reference, null));

            Assert.Equal((HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK), (first.StatusCode, retry.StatusCode));
            Assert.Equal(RefundStatus.Processed, (await RefundAsync(refund.Reference)).Status);
        }
    }

    // ---- reconciliation job ----------------------------------------------------------------------------------------

    [Fact]
    public async Task ARefundPaystackDidNotTakeAtFirst_IsSentAgainWithTheSameReference()
    {
        (HttpClient patron, string order, _) = await PaidOrderAsync();
        using (patron)
        {
            _gateway.OnCreateRefund = _ => throw new PaymentGatewayException("timeout");
            (await CancelAsync(patron, order, 27.30m)).Dispose();
            Refund waiting = Assert.Single(await RefundsAsync(order));
            Assert.Equal(RefundStatus.Initiating, waiting.Status);

            _gateway.OnCreateRefund = request => new GatewayRefund(true, VerifiedRefundOutcome.Pending, request.AmountMinor, FakePaymentGateway.NextRefundId(), null);
            await ReconcileAsync();
            Assert.Equal(RefundStatus.Initiating, (await RefundAsync(waiting.Reference)).Status);   // too recent: the request may still be running

            _clock.Now += TimeSpan.FromMinutes(2);
            await ReconcileAsync();

            Assert.Equal(RefundStatus.Pending, (await RefundAsync(waiting.Reference)).Status);
            Assert.Equal(2, _gateway.RefundRequests.Count(r => r.Reference == waiting.Reference));
        }
    }

    [Fact]
    public async Task APendingRefund_IsCompletedByTheJob_WithTheBrowserClosed()
    {
        (HttpClient patron, _, _, Refund refund) = await CancelledPaidOrderAsync();
        patron.Dispose();
        _gateway.OnGetRefund = reference => reference == refund.Reference
            ? new GatewayRefund(true, VerifiedRefundOutcome.Processed, 2730, refund.ProviderRefundId, null)
            : new GatewayRefund(true, VerifiedRefundOutcome.Pending, 0, null, null);

        await ReconcileAsync();

        Assert.Equal(RefundStatus.Processed, (await RefundAsync(refund.Reference)).Status);
    }

    [Fact]
    public async Task APaymentArrivingAfterCancellation_IsRefundedInFull_Automatically()
    {
        (HttpClient patron, string order) = await PatronWithOrderAsync();
        using (patron)
        {
            string payment = await StartPaymentAsync(patron, order);
            (await CancelAsync(patron, order, 0m)).Dispose();                       // the checkout tab stays open…
            Assert.Equal(PaymentStatus.Superseded, await PaymentStatusAsync(payment));

            Answer(payment, FakePaymentGateway.Paid(2990));                          // …and the patron pays in it anyway
            using (HttpResponseMessage webhook = await DeliverAsync(
                "{\"event\":\"charge.success\",\"data\":{\"id\":" + FakePaymentGateway.NextRefundId().ToString(CultureInfo.InvariantCulture) +
                ",\"reference\":\"" + payment + "\"}}"))
            {
                Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
            }

            Assert.Equal((PaymentStatus.RequiresRefund, OrderStatus.Cancelled), (await PaymentStatusAsync(payment), await OrderStatusAsync(order)));

            await ReconcileAsync();
            await ReconcileAsync();

            Refund refund = Assert.Single(await RefundsAsync(order));
            Assert.Equal((RefundKind.UnacceptedPayment, 2990L, 0L, RefundStatus.Pending), (refund.Kind, refund.AmountMinor, refund.FeeMinor, refund.Status));
            Assert.Equal(payment, Assert.Single(_gateway.RefundRequests, r => r.Reference == refund.Reference).TransactionReference);
        }
    }

    [Fact]
    public async Task APaymentOfTheWrongAmount_IsNotRefundedAutomatically()
    {
        (HttpClient patron, string order) = await PatronWithOrderAsync();
        using (patron)
        {
            string payment = await StartPaymentAsync(patron, order);
            Answer(payment, FakePaymentGateway.Paid(1000));
            (await SendAsync(patron, $"/api/ordering/orders/{order}/payments/refresh", "{}", await patron.GetCsrfTokenAsync())).Dispose();
            Assert.Equal(PaymentStatus.RequiresRefund, await PaymentStatusAsync(payment));

            await ReconcileAsync();

            Assert.Empty(await RefundsAsync(order));
        }
    }

    [Fact]
    public async Task AnUnansweredRequest_IsClosedAtThePickupTime()
    {
        (HttpClient patron, string order) = await PatronWithOrderAsync();
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        using (patron)
        {
            using (HttpResponseMessage requested = await SendAsync(manager.Client, $"/api/manager/orders/{order}/cancellation-requests",
                "{\"reason\":\"Out of milk\"}", await manager.Client.GetCsrfTokenAsync()))
            {
                Assert.Equal(HttpStatusCode.Created, requested.StatusCode);
            }

            await ReconcileAsync();
            Assert.Equal(CancellationRequestStatus.Pending, await RequestStatusAsync(order));

            await ExecuteAsync("UPDATE milkshake.orders SET pickup_at_utc = now() - interval '1 minute' WHERE reference = {0}", order);
            _clock.Now = DateTimeOffset.UtcNow;
            await ReconcileAsync();

            Assert.Equal(CancellationRequestStatus.Lapsed, await RequestStatusAsync(order));
        }
    }

    [Fact]
    public async Task WhenPaystackIsDown_TheJobChangesNoRefund_AndDoesNotFail()
    {
        (HttpClient patron, _, _, Refund refund) = await CancelledPaidOrderAsync();
        using (patron)
        {
            _gateway.OnGetRefund = _ => throw new PaymentGatewayException("down");
            _gateway.OnCreateRefund = _ => throw new PaymentGatewayException("down");
            _gateway.OnVerify = _ => throw new PaymentGatewayException("down");

            await ReconcileAsync();

            Assert.Equal(RefundStatus.Pending, (await RefundAsync(refund.Reference)).Status);
        }
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    private async Task<int> ReconcileAsync()
    {
        await using AsyncServiceScope scope = App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPaymentReconciler>().ReconcileAsync(CancellationToken.None);
    }

    /// <summary>Delivers like Paystack: no cookies, no CSRF token, only the signature header.</summary>
    private async Task<HttpResponseMessage> DeliverAsync(string body, bool sign = true)
    {
        using HttpClient provider = App.CreateSecureClient(handleCookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/payments/paystack/webhook", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (sign)
        {
            request.Headers.TryAddWithoutValidation(PaystackWebhookAuthenticator.SignatureHeader,
                PaystackWebhookAuthenticatorTests.Sign(Encoding.UTF8.GetBytes(body), Key));
        }

        return await provider.SendAsync(request);
    }

    private static string RefundEvent(string eventType, string? refundReference, string? transactionReference) =>
        JsonSerializer.Serialize(new
        {
            @event = eventType,
            data = new { status = eventType["refund.".Length..], refund_reference = refundReference, transaction_reference = transactionReference, amount = "2730", currency = "ZAR" },
        });

    /// <summary>A paid order the patron cancelled; its refund is Pending at Paystack.</summary>
    private async Task<(HttpClient Patron, string Order, string Payment, Refund Refund)> CancelledPaidOrderAsync()
    {
        (HttpClient patron, string order, string payment) = await PaidOrderAsync();
        using (HttpResponseMessage cancelled = await CancelAsync(patron, order, 27.30m))
        {
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        }

        return (patron, order, payment, Assert.Single(await RefundsAsync(order)));
    }

    private async Task<Refund> RefundAsync(string reference)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await Db(scope).Refunds.AsNoTracking().SingleAsync(r => r.Reference == reference);
    }

    private async Task<PaymentStatus> PaymentStatusAsync(string payment)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await Db(scope).Payments.Where(p => p.Reference == payment).Select(p => p.Status).SingleAsync();
    }

    /// <summary>The simulated provider answers for this payment; every other open attempt in the shared database stays open.</summary>
    private void Answer(string paymentReference, GatewayVerification result) =>
        _gateway.OnVerify = reference => reference == paymentReference ? result : new GatewayVerification(true, VerifiedOutcome.Open, 0, "ZAR", null, null);

    private async Task<(HttpClient Patron, string Order, string Payment)> PaidOrderAsync()
    {
        (HttpClient patron, string order) = await PatronWithOrderAsync();
        string payment = await StartPaymentAsync(patron, order);
        Answer(payment, FakePaymentGateway.Paid(2990));
        using HttpResponseMessage refreshed = await SendAsync(patron, $"/api/ordering/orders/{order}/payments/refresh", "{}", await patron.GetCsrfTokenAsync());
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal(OrderStatus.Confirmed, await OrderStatusAsync(order));
        _gateway.OnVerify = _ => new GatewayVerification(true, VerifiedOutcome.Open, 0, "ZAR", null, null);
        return (patron, order, payment);
    }

    private async Task<string> StartPaymentAsync(HttpClient patron, string order)
    {
        using HttpResponseMessage started = await StartPaymentResponseAsync(patron, order);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        return (await started.ReadJsonAsync<PaymentCheckoutResponse>()).PaymentReference;
    }

    private async Task<HttpResponseMessage> StartPaymentResponseAsync(HttpClient patron, string order) =>
        await SendAsync(patron, $"/api/ordering/orders/{order}/payments", "{}", await patron.GetCsrfTokenAsync(), Guid.NewGuid().ToString());

    private async Task<(HttpClient Patron, string Order)> PatronWithOrderAsync()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);

        // The menu's prices start at the real time they were created; bring the test clock up to it so they are current.
        _clock.Now = DateTimeOffset.UtcNow;
        HttpClient patron = await RegisteredPatronAsync();
        OrderingOptionsResponse options = await patron.GetFromJsonAsync<OrderingOptionsResponse>(new Uri("/api/ordering/options", UriKind.Relative))
            ?? throw new InvalidOperationException("No options returned.");
        string draft = JsonSerializer.Serialize(new
        {
            restaurantId = menu.Restaurant.Id,
            pickupLocal = options.Pickup.LatestLocal,
            drinks = new[] { new { flavourId = menu.Flavour.Id, consistencyId = menu.Consistency.Id, toppingId = menu.Topping.Id } },
            expectedTotal = 29.90m,
            expectedCurrencyCode = "ZAR",
        });
        using HttpResponseMessage placed = await SendAsync(patron, "/api/ordering/orders", draft, await patron.GetCsrfTokenAsync(), Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        return (patron, (await placed.ReadJsonAsync<OrderResponse>()).Reference);
    }

    private async Task<HttpClient> RegisteredPatronAsync()
    {
        HttpClient client = App.CreateSecureClient();
        using HttpResponseMessage registered = await client.PostWithCsrfAsync("/api/auth/register", RegisterBody.Valid());
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        return client;
    }

    private static Uri CancellationUri(string order) => new($"/api/ordering/orders/{order}/cancellation", UriKind.Relative);

    private static async Task<CancellationResponse> GetAsync(HttpClient patron, string order)
    {
        using HttpResponseMessage response = await patron.GetAsync(CancellationUri(order));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<CancellationResponse>();
    }

    private static async Task<HttpResponseMessage> CancelAsync(HttpClient client, string order, decimal expectedRefund, string? csrfToken = null) =>
        await SendCancelAsync(client, order,
            "{\"expectedRefund\":" + expectedRefund.ToString(CultureInfo.InvariantCulture) + "}",
            csrfToken ?? await client.GetCsrfTokenAsync());

    private static Task<HttpResponseMessage> SendCancelAsync(HttpClient client, string order, string json, string? csrfToken) =>
        SendAsync(client, $"/api/ordering/orders/{order}/cancel", json, csrfToken);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string json, string? csrfToken, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (csrfToken is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", csrfToken);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add(OrderMessages.IdempotencyKeyHeader, idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    private async Task<CancellationRequestStatus> RequestStatusAsync(string order)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        return await db.CancellationRequests.Where(r => db.Orders.Any(o => o.Id == r.OrderId && o.Reference == order))
            .OrderByDescending(r => r.Id).Select(r => r.Status).FirstAsync();
    }

    private async Task<List<Refund>> RefundsAsync(string order)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        return await db.Refunds.AsNoTracking().Where(r => db.Orders.Any(o => o.Id == r.OrderId && o.Reference == order)).ToListAsync();
    }

    private async Task<OrderStatus> OrderStatusAsync(string order)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await Db(scope).Orders.Where(o => o.Reference == order).Select(o => o.Status).SingleAsync();
    }

    private async Task ExecuteAsync(string sql, string value)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await Db(scope).Database.ExecuteSqlRawAsync(sql, value);
    }

    private static MilkshakeDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
}
