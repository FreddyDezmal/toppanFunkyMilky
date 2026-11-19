using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api.Security;
using Milkshake.IntegrationTests.Infrastructure;

namespace Milkshake.IntegrationTests;

/// <summary>Every ordering endpoint, now and in later steps, must require the Patron role.</summary>
public sealed class OrderingSecurityTests(MilkshakeApiFactory factory) : IClassFixture<MilkshakeApiFactory>
{
    [Fact]
    public void EveryOrderingEndpoint_RequiresThePatronOnlyPolicy()
    {
        RouteEndpoint[] endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("api/ordering", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, endpoint =>
        {
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), a => a.Policy == AuthorizationPolicies.PatronOnly);
            Assert.Equal(RateLimitPolicies.Ordering, endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);

        });
    }
}