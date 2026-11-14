using System.Net;
using System.Net.Http.Json;
using Milkshake.Api.Contracts;
using Milkshake.IntegrationTests.Infrastructure;

namespace Milkshake.IntegrationTests;

public sealed class HealthEndpointTests(MilkshakeApiFactory factory) : IClassFixture<MilkshakeApiFactory>
{
    [Fact]
    public async Task GetHealth_ReturnsOkWithHealthySelfCheck()
    {
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        HealthResponse? body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("Healthy", body.Status);
        Assert.Contains(body.Checks, check => check.Name == "self" && check.Status == "Healthy");
    }
}