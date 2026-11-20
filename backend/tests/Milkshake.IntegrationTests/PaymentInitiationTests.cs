using System.Globalization;
using System.Net;
using System.Net.Http.Json;
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
/// M8 POST /api/ordering/orders/{reference}/payments against a simulated Paystack (decision P10): the stored total in
/// cents, idempotent retries, resuming an open checkout instead of creating another, settling an open attempt with the
/// provider first (P4), timeouts, an unexpected checkout address, eligibility (A2) and access control.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class PaymentInitiationTests(PostgresApiFactory factory) : IDisposable
{
    private readonly FakePaymentGateway _gateway = new();
    private WebApplicationFactory<ApiAssemblyMarker>? _app;

    private WebApplicationFactory<ApiAssemblyMarker> App => _app ??= factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Payments:Paystack:ReturnBaseUrl", "https://localhost:5173/");
        builder.ConfigureTestServices(services => services.AddSingleton<IPaymentGateway>(_gateway));
    });

    public void Dispose() => _app?.Dispose();

    [Fact]
    public async Task StartingAPayment_CreatesACheckoutForTheStoredTotalInCents()
    {
        (HttpClient patron, string email, OrderResponse order) = await PatronWithOrderAsync();
        using (patron)
        {
            using HttpResponseMessage response = await StartAsync(patron, order.Reference, Guid.NewGuid());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            PaymentCheckoutResponse checkout = await response.ReadJsonAsync<PaymentCheckoutResponse>();
            Assert.Equal("checkout.paystack.com", checkout.CheckoutUrl.Host);
            Assert.True(Payment.IsValidReference(checkout.PaymentReference));

            // 12.00 + 6.00 + 8.00 = 26.00; VAT 3.90; total 29.90 = 2990 cents.
            CheckoutRequest sent = Assert.Single(_gateway.Created);
            Assert.Equal((checkout.PaymentReference, 2990L, "ZAR", email), (sent.Reference, sent.AmountMinor, sent.CurrencyCode, sent.CustomerEmail));
            Assert.Equal($"https://localhost:5173/orders/{order.Reference}?payment=return", sent.CallbackUrl.AbsoluteUri);

            Payment stored = Assert.Single(await PaymentsAsync(order.Reference));
            Assert.Equal((PaymentStatus.Pending, 2990L, checkout.CheckoutUrl.AbsoluteUri), (stored.Status, stored.AmountMinor, stored.CheckoutUrl));
            Assert.Equal(OrderStatus.PendingPayment, await OrderStatusAsync(order.Reference));
        }
    }

    [Fact]
    public async Task TheSameKey_ReturnsTheSameCheckout_WithoutASecondAttempt()
    {
        (HttpClient patron, _, OrderResponse order) = await PatronWithOrderAsync();
        using (patron)
        {
            var key = Guid.NewGuid();
            using HttpResponseMessage first = await StartAsync(patron, order.Reference, key);
            using HttpResponseMessage retry = await StartAsync(patron, order.Reference, key);

            Assert.Equal(
                (await first.ReadJsonAsync<PaymentCheckoutResponse>()).CheckoutUrl,
                (await retry.ReadJsonAsync<PaymentCheckoutResponse>()).CheckoutUrl);
            Assert.Single(_gateway.Created);
            Assert.Single(await PaymentsAsync(order.Reference));
        }
    }

    [Fact]
    public async Task PayingAgainWhileACheckoutIsStillOpen_ResumesIt_InsteadOfCreatingAnother()
    {
        (HttpClient patron, _, OrderResponse order) = await PatronWithOrderAsync();
        using (patron)
        {
            using HttpResponseMessage first = await StartAsync(patron, order.Reference, Guid.NewGuid());
            using HttpResponseMessage again = await StartAsync(patron, order.Reference, Guid.NewGuid());

            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            Assert.Equal(
                (await first.ReadJsonAsync<PaymentCheckoutResponse>()).PaymentReference,
                (await again.ReadJsonAsync<PaymentCheckoutResponse>()).PaymentReference);
            Assert.Single(_gateway.Created);
        }
    }

    [Fact]
    public async Task AnAbandonedCheckout_IsEnded_AndANewOneIsCreated_WithoutTouchingTheOrder()
    {
        (HttpClient patron, _, OrderResponse order) = await PatronWithOrderAsync();
        using (patron)
        {
            using HttpResponseMessage first = await StartAsync(patron, order.Reference, Guid.NewGuid());
            _gateway.OnVerify = _ => new GatewayVerification(true, VerifiedOutcome.Abandoned, 0, "ZAR", 77, "Abandoned");
            using HttpResponseMessage second = await StartAsync(patron, order.Reference, Guid.NewGuid());

            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal(2, _gateway.Created.Count);
            List<Payment> attempts = await PaymentsAsync(order.Reference);
            Assert.Equal(new[] { PaymentStatus.Abandoned, PaymentStatus.Pending }, attempts.OrderBy(p => p.Id).Select(p => p.Status));
            Assert.Equal(OrderStatus.PendingPayment, await OrderStatusAsync(order.Reference));
        }
    }

    [Fact]
    public async Task AnOpenAttemptThatWasPaid_ConfirmsTheOrder_AndNoNewCheckoutIsCreated()
    {
        (HttpClient patron, _, OrderResponse order) = await PatronWithOrderAsync();
        using (patron)
        {
            using HttpResponseMessage first = await StartAsync(patron, order.Reference, Guid.NewGuid());
            _gateway.OnVerify = _ => FakePaymentGateway.Paid(2990);
            using HttpResponseMessage again = await StartAsync(patron, order.Reference, Guid.NewGuid());

            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            Assert.Contains(PaymentMessages.AlreadyPaidTitle, await again.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Single(_gateway.Created);
            Assert.Equal(PaymentStatus.Succeeded, Assert.Single(await PaymentsAsync(order.Reference)).Status);
            Assert.Equal(OrderStatus.Confirmed, await OrderStatusAsync(order.Reference));
        }
    }

    [Fact]
    public async Task AProviderTimeout_ChargesNothing_KeepsTheAttemptForVerification_AndBlocksADuplicateMeanwhile()
    {
        (HttpClient patron, _, OrderResponse order) = await PatronWithOrderAsync();
        using (patron)
        {
            _gateway.OnCreate = _ => throw new PaymentGatewayException("timeout");
            using HttpResponseMessage failed = await StartAsync(patron, order.Reference, Guid.NewGuid());
            using HttpResponseMessage immediately = await StartAsync(patron, order.Reference, Guid.NewGuid());

            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.Contains("not been charged", await failed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.Conflict, immediately.StatusCode);
            Assert.Equal(PaymentStatus.Initiating, Assert.Single(await PaymentsAsync(order.Reference)).Status);
        }
    }

    [Fact]
    public async Task AnUnexpectedCheckoutAddress_IsNeverReturned_AndTheAttemptIsEnded()
    {
        (HttpClient patron, _, OrderResponse order) = await PatronWithOrderAsync();
        using (patron)
        {
            _gateway.OnCreate = _ => new Uri("https://checkout.paystack.com.evil.example/pay");
            using HttpResponseMessage response = await StartAsync(patron, order.Reference, Guid.NewGuid());

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.DoesNotContain("evil", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(PaymentStatus.Abandoned, Assert.Single(await PaymentsAsync(order.Reference)).Status);
        }
    }

    [Fact]
    public async Task SimultaneousPayClicks_CreateOneAttemptAndOneCheckout()
    {
        (HttpClient patron, _, OrderResponse order) = await PatronWithOrderAsync();
        using (patron)
        {
            string token = await patron.GetCsrfTokenAsync();
            HttpResponseMessage[] responses = await Task.WhenAll(
                Enumerable.Range(0, 5).Select(_ => StartAsync(patron, order.Reference, Guid.NewGuid(), token)));
            try
            {
                Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
                Assert.Single(_gateway.Created);
                Assert.Single(await PaymentsAsync(order.Reference));
            }
            finally
            {
                foreach (HttpResponseMessage response in responses)
                {
                    response.Dispose();
                }
            }
        }
    }

    [Fact]
    public async Task APastPickupTime_IsRefused_WithoutCallingTheProvider()
    {
        (HttpClient patron, _, OrderResponse order) = await PatronWithOrderAsync();
        using (patron)
        {
            await ExecuteAsync("UPDATE milkshake.orders SET pickup_at_utc = now() - interval '1 minute' WHERE reference = {0}", order.Reference);
            using HttpResponseMessage response = await StartAsync(patron, order.Reference, Guid.NewGuid());

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Empty(_gateway.Created);
            Assert.Empty(await PaymentsAsync(order.Reference));
        }
    }

    [Theory]
    [InlineData("UPDATE milkshake.orders SET status = 'Cancelled' WHERE reference = {0}", HttpStatusCode.Conflict)]
    [InlineData("UPDATE milkshake.orders SET status = 'Confirmed' WHERE reference = {0}", HttpStatusCode.Conflict)]
    public async Task OrdersThatDoNotAwaitPayment_CannotBePaid(string sql, HttpStatusCode expected)
    {
        (HttpClient patron, _, OrderResponse order) = await PatronWithOrderAsync();
        using (patron)
        {
            await ExecuteAsync(sql, order.Reference);
            using HttpResponseMessage response = await StartAsync(patron, order.Reference, Guid.NewGuid());

            Assert.Equal(expected, response.StatusCode);
            Assert.Empty(_gateway.Created);
        }
    }

    [Fact]
    public async Task AnotherPatronsOrder_AndUnknownReferences_AreNotFound()
    {
        (HttpClient owner, _, OrderResponse order) = await PatronWithOrderAsync();
        using HttpClient other = await RegisteredPatronAsync(RegisterBody.UniqueEmail());
        using (owner)
        {
            foreach (string reference in new[] { order.Reference, "MS-0000-0000", "not-a-reference" })
            {
                using HttpResponseMessage response = await StartAsync(other, reference, Guid.NewGuid());
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }

            Assert.Empty(_gateway.Created);
        }
    }

    [Theory]
    [InlineData("""{"amount":1}""")]
    [InlineData("""{"amountMinor":100}""")]
    [InlineData("""{"currencyCode":"USD"}""")]
    [InlineData("""{"status":"Confirmed"}""")]
    public async Task AnyFieldInTheBody_IsRejected(string json)
    {
        (HttpClient patron, _, OrderResponse order) = await PatronWithOrderAsync();
        using (patron)
        {
            using HttpResponseMessage response = await StartAsync(patron, order.Reference, Guid.NewGuid(), json: json);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(_gateway.Created);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task AMissingOrInvalidIdempotencyKey_IsRefused(string? key)
    {
        (HttpClient patron, _, OrderResponse order) = await PatronWithOrderAsync();
        using (patron)
        {
            using HttpResponseMessage response = await SendAsync(patron, order.Reference, key, "{}", await patron.GetCsrfTokenAsync());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(_gateway.Created);
        }
    }

    [Fact]
    public async Task AnonymousManagersAndRequestsWithoutCsrf_AreRefused()
    {
        (HttpClient patron, _, OrderResponse order) = await PatronWithOrderAsync();
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        using HttpClient anonymous = App.CreateSecureClient();
        using (patron)
        {
            using HttpResponseMessage asAnonymous = await StartAsync(anonymous, order.Reference, Guid.NewGuid());
            using HttpResponseMessage asManager = await StartAsync(manager.Client, order.Reference, Guid.NewGuid());
            using HttpResponseMessage withoutCsrf = await SendAsync(patron, order.Reference, Guid.NewGuid().ToString(), "{}", csrfToken: null);

            Assert.Equal(
                (HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.BadRequest),
                (asAnonymous.StatusCode, asManager.StatusCode, withoutCsrf.StatusCode));
            Assert.Empty(_gateway.Created);
        }
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    private async Task<(HttpClient Patron, string Email, OrderResponse Order)> PatronWithOrderAsync()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        string email = RegisterBody.UniqueEmail();
        HttpClient patron = await RegisteredPatronAsync(email);

        OrderingOptionsResponse options = await patron.GetFromJsonAsync<OrderingOptionsResponse>(new Uri("/api/ordering/options", UriKind.Relative))
            ?? throw new InvalidOperationException("No options returned.");
        var body = new
        {
            restaurantId = menu.Restaurant.Id,
            pickupLocal = options.Pickup.LatestLocal,
            drinks = new[] { new { flavourId = menu.Flavour.Id, consistencyId = menu.Consistency.Id, toppingId = menu.Topping.Id } },
            expectedTotal = 29.90m,
            expectedCurrencyCode = "ZAR",
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/ordering/orders", UriKind.Relative)) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", await patron.GetCsrfTokenAsync());
        request.Headers.Add(OrderMessages.IdempotencyKeyHeader, Guid.NewGuid().ToString());
        using HttpResponseMessage placed = await patron.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        return (patron, email, await placed.ReadJsonAsync<OrderResponse>());
    }

    private async Task<HttpClient> RegisteredPatronAsync(string email)
    {
        HttpClient client = App.CreateSecureClient();
        using HttpResponseMessage registered = await client.PostWithCsrfAsync("/api/auth/register", RegisterBody.Valid(email));
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        return client;
    }

    private static async Task<HttpResponseMessage> StartAsync(HttpClient client, string reference, Guid key, string? csrfToken = null, string json = "{}") =>
        await SendAsync(client, reference, key.ToString(), json, csrfToken ?? await client.GetCsrfTokenAsync());

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string reference, string? key, string json, string? csrfToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/ordering/orders/{reference}/payments", UriKind.Relative))
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        if (csrfToken is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", csrfToken);
        }

        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation(OrderMessages.IdempotencyKeyHeader, key);
        }

        return await client.SendAsync(request);
    }

    private async Task<List<Payment>> PaymentsAsync(string orderReference)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        return await db.Payments.AsNoTracking()
            .Where(p => db.Orders.Any(o => o.Id == p.OrderId && o.Reference == orderReference))
            .ToListAsync();
    }

    private async Task<OrderStatus> OrderStatusAsync(string orderReference)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>().Orders
            .Where(o => o.Reference == orderReference).Select(o => o.Status).SingleAsync();
    }

    private async Task ExecuteAsync(string sql, string orderReference)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>().Database.ExecuteSqlRawAsync(sql, orderReference);
    }
}
