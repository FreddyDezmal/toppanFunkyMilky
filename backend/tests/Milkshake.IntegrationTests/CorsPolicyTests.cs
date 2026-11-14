using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Milkshake.Api;
using Milkshake.IntegrationTests.Infrastructure;

namespace Milkshake.IntegrationTests;

public sealed class CorsPolicyTests(MilkshakeApiFactory factory) : IClassFixture<MilkshakeApiFactory>
{
    private const string AllowedOrigin = "http://localhost:5173";
    private const string UnknownOrigin = "https://not-allowed.example";

    [Fact]
    public async Task Preflight_FromAllowedOrigin_IsAccepted()
    {
        using WebApplicationFactory<ApiAssemblyMarker> app = WithAllowedOrigin();
        using HttpClient client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, new Uri("/api/health", UriKind.Relative));
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(AllowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task Request_FromUnknownOrigin_GetsNoCorsHeader()
    {
        using WebApplicationFactory<ApiAssemblyMarker> app = WithAllowedOrigin();
        using HttpClient client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/health", UriKind.Relative));
        request.Headers.Add("Origin", UnknownOrigin);

        using HttpResponseMessage response = await client.SendAsync(request);

        // The server still answers, but without the header the browser refuses to hand the response to the page.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private WebApplicationFactory<ApiAssemblyMarker> WithAllowedOrigin() =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Cors:AllowedOrigins:0"] = AllowedOrigin,
                })));
}