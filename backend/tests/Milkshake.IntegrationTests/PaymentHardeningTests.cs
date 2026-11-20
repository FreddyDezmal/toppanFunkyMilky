using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api;
using Milkshake.Api.Contracts.Ordering;
using Milkshake.Application.Payments;
using Milkshake.IntegrationTests.Infrastructure;
using Milkshake.Infrastructure.Payments;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M8 Step 9 security review: the payment endpoints' remaining protections from the milestone's API security list —
/// manager refusal on refresh, body caps, rate limits (patron endpoints and the webhook) and errors that reveal nothing.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class PaymentHardeningTests(PostgresApiFactory factory)
{
    private const string WebhookPath = "/api/payments/paystack/webhook";
    private const string Secret = "sk_test_must_never_leak_0123456789";
    private const string ProviderDetail = "upstream-internal-detail-42";

    [Fact]
    public async Task AManager_CannotRefreshAPatronsPayment()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        using HttpResponseMessage response = await PostAsync(manager.Client, "/api/ordering/orders/MS-0000-0000/payments/refresh", "{}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/ordering/orders/MS-0000-0000/payments")]
    [InlineData("/api/ordering/orders/MS-0000-0000/payments/refresh")]
    public async Task AnOversizedPaymentRequest_IsRefused(string path)
    {
        using WebApplicationFactory<ApiAssemblyMarker> app = factory.WithWebHostBuilder(_ => { });
        using HttpClient patron = await RegisteredPatronAsync(app);

        using HttpResponseMessage response = await PostAsync(patron, path, "{}" + new string(' ', 4_000));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/ordering/orders/MS-0000-0000/payments")]
    [InlineData("/api/ordering/orders/MS-0000-0000/payments/refresh")]
    public async Task PaymentRequests_BeyondThePatronLimit_Return429(string path)
    {
        using WebApplicationFactory<ApiAssemblyMarker> app =
            factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:OrderingPermitLimit", "3"));
        using HttpClient patron = await RegisteredPatronAsync(app);

        for (int call = 1; call <= 3; call++)
        {
            using HttpResponseMessage allowed = await PostAsync(patron, path, "{}");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        using HttpResponseMessage limited = await PostAsync(patron, path, "{}");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task WebhookDeliveries_BeyondTheLimit_Return429()
    {
        using WebApplicationFactory<ApiAssemblyMarker> app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Payments:Paystack:SecretKey", Secret);
            builder.UseSetting("RateLimiting:WebhookPermitLimit", "2");
        });

        for (int call = 1; call <= 2; call++)
        {
            using HttpResponseMessage allowed = await DeliverAsync(app, signature: "not-a-signature");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        using HttpResponseMessage limited = await DeliverAsync(app, signature: "not-a-signature");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task ErrorResponses_RevealNoSecretsOrProviderDetails()
    {
        var gateway = new FakePaymentGateway
        {
            OnCreate = _ => throw new PaymentGatewayException($"Paystack failed using {Secret}: {ProviderDetail}"),
            OnVerify = _ => throw new PaymentGatewayException($"Paystack failed using {Secret}: {ProviderDetail}"),
        };
        using WebApplicationFactory<ApiAssemblyMarker> app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Payments:Paystack:SecretKey", Secret);
            builder.UseSetting("Payments:Paystack:ReturnBaseUrl", "https://localhost:5173/");
            builder.ConfigureTestServices(services => services.AddSingleton<IPaymentGateway>(gateway));
        });
        (HttpClient patron, string order) = await PatronWithOrderAsync(app);
        using (patron)
        {
            using HttpResponseMessage started = await PostAsync(patron, $"/api/ordering/orders/{order}/payments", "{}");
            using HttpResponseMessage refreshed = await PostAsync(patron, $"/api/ordering/orders/{order}/payments/refresh", "{}");
            using HttpResponseMessage forged = await DeliverAsync(app, signature: "0123abcd");
            using HttpResponseMessage malformed = await PostAsync(patron, $"/api/ordering/orders/{order}/payments", "{\"amount\":");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, started.StatusCode);
            Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
            foreach (HttpResponseMessage response in new[] { started, refreshed, forged, malformed })
            {
                string body = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain(Secret, body, StringComparison.Ordinal);
                Assert.DoesNotContain("sk_test", body, StringComparison.Ordinal);
                Assert.DoesNotContain(ProviderDetail, body, StringComparison.Ordinal);
                Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
                Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
            }
        }
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    private async Task<(HttpClient Patron, string Order)> PatronWithOrderAsync(WebApplicationFactory<ApiAssemblyMarker> app)
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        HttpClient patron = await RegisteredPatronAsync(app);
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
        return (patron, (await placed.ReadJsonAsync<OrderResponse>()).Reference);
    }

    private static async Task<HttpClient> RegisteredPatronAsync(WebApplicationFactory<ApiAssemblyMarker> app)
    {
        HttpClient client = app.CreateSecureClient();
        using HttpResponseMessage registered = await client.PostWithCsrfAsync("/api/auth/register", RegisterBody.Valid());
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        return client;
    }

    /// <summary>A patron-style POST with CSRF and a fresh idempotency key.</summary>
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

    /// <summary>Delivers like Paystack (no cookies, no CSRF), with the given signature header.</summary>
    private static async Task<HttpResponseMessage> DeliverAsync(WebApplicationFactory<ApiAssemblyMarker> app, string signature)
    {
        using HttpClient provider = app.CreateSecureClient(handleCookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(WebhookPath, UriKind.Relative))
        {
            Content = new StringContent("{\"event\":\"charge.success\",\"data\":{\"id\":999001,\"reference\":\"MSP-0123456789AB\"}}", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PaystackWebhookAuthenticator.SignatureHeader, signature);
        return await provider.SendAsync(request);
    }
}
