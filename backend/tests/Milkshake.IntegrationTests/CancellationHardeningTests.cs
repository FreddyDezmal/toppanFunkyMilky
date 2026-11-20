using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api;
using Milkshake.Application.Payments;
using Milkshake.IntegrationTests.Infrastructure;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M8b security review: the remaining protections of the new endpoints – body caps, the per-patron rate limit on
/// cancelling and answering requests, and errors that reveal no secrets or provider details.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class CancellationHardeningTests(PostgresApiFactory factory)
{
    private const string Secret = "sk_test_must_never_leak_0123456789";
    private const string ProviderDetail = "upstream-internal-detail-42";

    [Fact]
    public async Task AnOversizedManagerRequest_IsRefused()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        using HttpResponseMessage response = await PostAsync(manager.Client, "/api/manager/orders/MS-0000-0000/cancellation-requests",
            "{\"reason\":\"x\"}" + new string(' ', 4_000));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/ordering/orders/MS-0000-0000/cancel", "{\"expectedRefund\":0}")]
    [InlineData("/api/ordering/orders/MS-0000-0000/cancellation-request/accept", "{\"expectedRefund\":0}")]
    [InlineData("/api/ordering/orders/MS-0000-0000/cancellation-request/decline", "{}")]
    public async Task CancellingAndAnswering_BeyondThePatronLimit_Return429(string path, string json)
    {
        using WebApplicationFactory<ApiAssemblyMarker> app =
            factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:OrderingPermitLimit", "3"));
        using HttpClient patron = await RegisteredPatronAsync(app);

        for (int call = 1; call <= 3; call++)
        {
            using HttpResponseMessage allowed = await PostAsync(patron, path, json);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        using HttpResponseMessage limited = await PostAsync(patron, path, json);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task ErrorsDuringCancellation_RevealNoSecretsOrProviderDetails()
    {
        var gateway = new FakePaymentGateway
        {
            OnVerify = _ => throw new PaymentGatewayException($"Paystack failed using {Secret}: {ProviderDetail}"),
            OnCreateRefund = _ => throw new PaymentGatewayException($"Paystack failed using {Secret}: {ProviderDetail}"),
        };
        using WebApplicationFactory<ApiAssemblyMarker> app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Payments:Paystack:SecretKey", Secret);
            builder.UseSetting("Payments:Paystack:ReturnBaseUrl", "https://localhost:5173/");
            builder.ConfigureTestServices(services => services.AddSingleton<IPaymentGateway>(gateway));
        });
        using HttpClient patron = await RegisteredPatronAsync(app);
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        using HttpResponseMessage malformed = await PostAsync(patron, "/api/ordering/orders/MS-0000-0000/cancel", "{\"expectedRefund\":");
        using HttpResponseMessage missing = await PostAsync(patron, "/api/ordering/orders/MS-0000-0000/cancel", "{\"expectedRefund\":0}");
        using HttpResponseMessage badReason = await PostAsync(manager.Client, "/api/manager/orders/MS-0000-0000/cancellation-requests", "{\"reason\":5}");

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.NotFound, HttpStatusCode.BadRequest),
            (malformed.StatusCode, missing.StatusCode, badReason.StatusCode));
        HttpResponseMessage[] responses = [malformed, missing, badReason];
        foreach (HttpResponseMessage response in responses)
        {
            string body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("sk_test", body, StringComparison.Ordinal);
            Assert.DoesNotContain(ProviderDetail, body, StringComparison.Ordinal);
            Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        }
    }

    private static async Task<HttpClient> RegisteredPatronAsync(WebApplicationFactory<ApiAssemblyMarker> app)
    {
        HttpClient client = app.CreateSecureClient();
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
        return await client.SendAsync(request);
    }
}
