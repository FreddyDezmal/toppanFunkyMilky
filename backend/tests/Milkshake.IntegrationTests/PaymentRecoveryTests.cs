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
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;
using Milkshake.IntegrationTests.Infrastructure;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M8 recovery without webhooks (decision P7): the patron's refresh on return, and the reconciliation job that works
/// with the browser closed. A controllable clock drives the grace periods. The simulated provider answers only for the
/// test's own payment and reports every other open attempt in the shared database as still open.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class PaymentRecoveryTests(PostgresApiFactory factory) : IDisposable
{
    private readonly FakePaymentGateway _gateway = new();
    private readonly SettableTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private WebApplicationFactory<ApiAssemblyMarker>? _app;

    private WebApplicationFactory<ApiAssemblyMarker> App => _app ??= factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Payments:Paystack:ReturnBaseUrl", "https://localhost:5173/");
        builder.UseSetting("Payments:Reconciliation:BatchSize", "10000");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IPaymentGateway>(_gateway);
            services.AddSingleton<TimeProvider>(_clock);
        });
    });

    public void Dispose() => _app?.Dispose();

    // ---- patron refresh ----------------------------------------------------------------------------------------

    [Fact]
    public async Task ReturningAfterPaying_ShowsThePaymentVerifiedAndTheOrderConfirmed()
    {
        (HttpClient patron, string order, string payment) = await PendingPaymentAsync();
        using (patron)
        {
            Answer(payment, FakePaymentGateway.Paid(2990));

            PaymentStatusResponse status = await RefreshAsync(patron, order);

            Assert.Equal(("Confirmed", true, false, false), (status.OrderStatus, status.IsPaid, status.AwaitsPayment, status.Checking));
            Assert.Equal((payment, "Succeeded"), (status.Payment!.Reference, status.Payment.Status));
        }
    }

    [Fact]
    public async Task WhileThePatronIsStillPaying_TheRefreshSaysChecking_AndNeverPaid()
    {
        (HttpClient patron, string order, string payment) = await PendingPaymentAsync();
        using (patron)
        {
            PaymentStatusResponse status = await RefreshAsync(patron, order);

            Assert.Equal(("PendingPayment", false, true, true), (status.OrderStatus, status.IsPaid, status.AwaitsPayment, status.Checking));
            Assert.Equal("Pending", status.Payment!.Status);
        }
    }

    [Fact]
    public async Task AnUnpaidCheckout_CountsAsOpenForThirtyMinutes_ThenAsAbandoned_LeavingTheOrderPayable()
    {
        (HttpClient patron, string order, string payment) = await PendingPaymentAsync();
        using (patron)
        {
            Answer(payment, new GatewayVerification(true, VerifiedOutcome.Abandoned, 0, "ZAR", null, "Abandoned"));

            PaymentStatusResponse soon = await RefreshAsync(patron, order);
            _clock.Now += TimeSpan.FromMinutes(31);
            PaymentStatusResponse later = await RefreshAsync(patron, order);

            Assert.Equal(("Pending", true), (soon.Payment!.Status, soon.Checking));
            Assert.Equal(("Abandoned", false, true, "PendingPayment"), (later.Payment!.Status, later.Checking, later.AwaitsPayment, later.OrderStatus));
        }
    }

    [Fact]
    public async Task WhenTheProviderIsDown_TheRefreshShowsWhatIsStored()
    {
        (HttpClient patron, string order, _) = await PendingPaymentAsync();
        using (patron)
        {
            _gateway.OnVerify = _ => throw new PaymentGatewayException("down");

            PaymentStatusResponse status = await RefreshAsync(patron, order);

            Assert.Equal(("Pending", true, false), (status.Payment!.Status, status.Checking, status.IsPaid));
        }
    }

    [Fact]
    public async Task OtherPatronsAnonymousCallersAndRequestsWithoutCsrf_CannotRefresh()
    {
        (HttpClient owner, string order, _) = await PendingPaymentAsync();
        using HttpClient other = await RegisteredPatronAsync();
        using HttpClient anonymous = App.CreateSecureClient();
        using (owner)
        {
            using HttpResponseMessage asOther = await SendRefreshAsync(other, order, await other.GetCsrfTokenAsync());
            using HttpResponseMessage asAnonymous = await SendRefreshAsync(anonymous, order, await anonymous.GetCsrfTokenAsync());
            using HttpResponseMessage withoutCsrf = await SendRefreshAsync(owner, order, csrfToken: null);

            Assert.Equal(
                (HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest),
                (asOther.StatusCode, asAnonymous.StatusCode, withoutCsrf.StatusCode));
        }
    }

    // ---- reconciliation job ------------------------------------------------------------------------------------

    [Fact]
    public async Task APaymentWhoseWebhookNeverArrived_IsConfirmedByTheJob_WithTheBrowserClosed()
    {
        (HttpClient patron, string order, string payment) = await PendingPaymentAsync();
        patron.Dispose();
        Answer(payment, FakePaymentGateway.Paid(2990));

        await ReconcileAsync();

        Assert.Equal((PaymentStatus.Succeeded, OrderStatus.Confirmed), await StateAsync(order, payment));
    }

    [Fact]
    public async Task AFailedPayment_IsRecorded_AndTheOrderCanBeRetried()
    {
        (HttpClient patron, string order, string payment) = await PendingPaymentAsync();
        patron.Dispose();
        Answer(payment, new GatewayVerification(true, VerifiedOutcome.Failed, 2990, "ZAR", null, "Declined"));

        await ReconcileAsync();

        Assert.Equal((PaymentStatus.Failed, OrderStatus.PaymentFailed), await StateAsync(order, payment));
    }

    [Fact]
    public async Task AnAttemptInterruptedBeforeReachingTheProvider_IsLeftAloneBriefly_ThenEnded()
    {
        (HttpClient patron, string order, string payment) = await InterruptedAttemptAsync();
        patron.Dispose();
        Answer(payment, GatewayVerification.NotFound);

        await ReconcileAsync();
        PaymentStatus meanwhile = (await StateAsync(order, payment)).Payment;
        _clock.Now += TimeSpan.FromMinutes(2);
        await ReconcileAsync();

        Assert.Equal(PaymentStatus.Initiating, meanwhile);
        Assert.Equal((PaymentStatus.Abandoned, OrderStatus.PendingPayment), await StateAsync(order, payment));
    }

    [Fact]
    public async Task AnAttemptInterruptedAfterTheProviderAcceptedIt_AndThenPaid_IsConfirmed()
    {
        (HttpClient patron, string order, string payment) = await InterruptedAttemptAsync();
        patron.Dispose();
        Answer(payment, FakePaymentGateway.Paid(2990));
        _clock.Now += TimeSpan.FromMinutes(2);

        await ReconcileAsync();

        Assert.Equal((PaymentStatus.Succeeded, OrderStatus.Confirmed), await StateAsync(order, payment));
    }

    [Fact]
    public async Task ACheckoutStillOpenAfterSeventyTwoHours_IsEnded_WithoutTouchingTheOrder()
    {
        (HttpClient patron, string order, string payment) = await PendingPaymentAsync();
        patron.Dispose();
        _clock.Now += TimeSpan.FromHours(73);

        await ReconcileAsync();

        Assert.Equal((PaymentStatus.Abandoned, OrderStatus.PendingPayment), await StateAsync(order, payment));
    }

    [Fact]
    public async Task WhenTheProviderIsDown_TheJobChangesNothing_AndDoesNotFail()
    {
        (HttpClient patron, string order, string payment) = await PendingPaymentAsync();
        patron.Dispose();
        _gateway.OnVerify = _ => throw new PaymentGatewayException("down");

        int changed = await ReconcileAsync();

        Assert.Equal(0, changed);
        Assert.Equal((PaymentStatus.Pending, OrderStatus.PendingPayment), await StateAsync(order, payment));
    }

    [Fact]
    public async Task SettledAttempts_AreNotCheckedAgain()
    {
        (HttpClient patron, string order, string payment) = await PendingPaymentAsync();
        patron.Dispose();
        Answer(payment, FakePaymentGateway.Paid(2990));
        await ReconcileAsync();
        var asked = new List<string>();
        _gateway.OnVerify = reference =>
        {
            lock (asked)
            {
                asked.Add(reference);
            }

            return new GatewayVerification(true, VerifiedOutcome.Open, 0, "ZAR", null, null);
        };

        await ReconcileAsync();

        Assert.DoesNotContain(payment, asked);
        Assert.Equal((PaymentStatus.Succeeded, OrderStatus.Confirmed), await StateAsync(order, payment));
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    /// <summary>The provider reports <paramref name="result"/> for this payment and "still open" for every other one.</summary>
    private void Answer(string payment, GatewayVerification result) =>
        _gateway.OnVerify = reference => reference == payment ? result : new GatewayVerification(true, VerifiedOutcome.Open, 0, "ZAR", null, null);

    private async Task<int> ReconcileAsync()
    {
        await using AsyncServiceScope scope = App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPaymentReconciler>().ReconcileAsync(CancellationToken.None);
    }

    private async Task<PaymentStatusResponse> RefreshAsync(HttpClient patron, string order)
    {
        using HttpResponseMessage response = await SendRefreshAsync(patron, order, await patron.GetCsrfTokenAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<PaymentStatusResponse>();
    }

    private static async Task<HttpResponseMessage> SendRefreshAsync(HttpClient client, string order, string? csrfToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/ordering/orders/{order}/payments/refresh", UriKind.Relative))
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        if (csrfToken is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", csrfToken);
        }

        return await client.SendAsync(request);
    }

    /// <summary>An order whose checkout creation "timed out": its attempt stays Initiating with our reference.</summary>
    private async Task<(HttpClient Patron, string Order, string Payment)> InterruptedAttemptAsync()
    {
        (HttpClient patron, OrderResponse order) = await PatronWithOrderAsync();
        _gateway.OnCreate = _ => throw new PaymentGatewayException("timeout");
        using HttpResponseMessage failed = await PostAsync(patron, $"/api/ordering/orders/{order.Reference}/payments", "{}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        _gateway.OnCreate = request => new Uri($"https://checkout.paystack.com/{request.Reference.ToLowerInvariant()}");

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        string payment = await db.Payments
            .Where(p => p.Status == PaymentStatus.Initiating && db.Orders.Any(o => o.Id == p.OrderId && o.Reference == order.Reference))
            .Select(p => p.Reference).SingleAsync();
        return (patron, order.Reference, payment);
    }

    private async Task<(HttpClient Patron, string Order, string Payment)> PendingPaymentAsync()
    {
        (HttpClient patron, OrderResponse order) = await PatronWithOrderAsync();
        using HttpResponseMessage started = await PostAsync(patron, $"/api/ordering/orders/{order.Reference}/payments", "{}");
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        return (patron, order.Reference, (await started.ReadJsonAsync<PaymentCheckoutResponse>()).PaymentReference);
    }

    private async Task<(HttpClient Patron, OrderResponse Order)> PatronWithOrderAsync()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);

        // The menu's prices start at the real time they were created; bring the test clock up to it so they are current.
        _clock.Now = DateTimeOffset.UtcNow;
        HttpClient patron = await RegisteredPatronAsync();
        OrderingOptionsResponse options = await patron.GetFromJsonAsync<OrderingOptionsResponse>(new Uri("/api/ordering/options", UriKind.Relative))
            ?? throw new InvalidOperationException("No options returned.");
        string draft = System.Text.Json.JsonSerializer.Serialize(new
        {
            restaurantId = menu.Restaurant.Id,
            pickupLocal = options.Pickup.LatestLocal,
            drinks = new[] { new { flavourId = menu.Flavour.Id, consistencyId = menu.Consistency.Id, toppingId = menu.Topping.Id } },
            expectedTotal = 29.90m,
            expectedCurrencyCode = "ZAR",
        });
        using HttpResponseMessage placed = await PostAsync(patron, "/api/ordering/orders", draft);
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        return (patron, await placed.ReadJsonAsync<OrderResponse>());
    }

    private async Task<HttpClient> RegisteredPatronAsync()
    {
        HttpClient client = App.CreateSecureClient();
        using HttpResponseMessage registered = await client.PostWithCsrfAsync("/api/auth/register", RegisterBody.Valid());
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        return client;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-XSRF-TOKEN", await client.GetCsrfTokenAsync());
        request.Headers.Add(OrderMessages.IdempotencyKeyHeader, Guid.NewGuid().ToString());
        return await client.SendAsync(request);
    }

    private async Task<(PaymentStatus Payment, OrderStatus Order)> StateAsync(string order, string payment)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        return (
            await db.Payments.Where(p => p.Reference == payment).Select(p => p.Status).SingleAsync(),
            await db.Orders.Where(o => o.Reference == order).Select(o => o.Status).SingleAsync());
    }
}
