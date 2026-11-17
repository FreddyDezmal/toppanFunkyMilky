using System.Net;
using System.Net.Http.Json;
using Milkshake.Api.Contracts.Auth;
using Milkshake.IntegrationTests.Infrastructure;

namespace Milkshake.IntegrationTests;

public sealed class SecurityPipelineTests(MilkshakeApiFactory factory) : IClassFixture<MilkshakeApiFactory>
{
    [Fact]
    public async Task ManagerEndpoint_WithoutSession_Returns401ProblemDetailsNotRedirect()
    {
        using HttpClient client = factory.CreateHttpsClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/manager/ping", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CsrfEndpoint_ReturnsTokenAndHardenedCookie()
    {
        using HttpClient client = factory.CreateHttpsClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/auth/csrf", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CsrfTokenResponse? body = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.Token));
        Assert.Equal("X-XSRF-TOKEN", body.HeaderName);

        string cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("milkshake.antiforgery=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }
}