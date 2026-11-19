using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api.Security;
using Milkshake.IntegrationTests.Infrastructure;

namespace Milkshake.IntegrationTests;

/// <summary>Checks every manager endpoint's metadata, so a new endpoint cannot be added without protection.</summary>
public sealed class ManagementSecurityTests(MilkshakeApiFactory factory) : IClassFixture<MilkshakeApiFactory>
{
    [Fact]
    public void EveryManagerEndpoint_RequiresTheManagerOnlyPolicy()
    {
        RouteEndpoint[] endpoints = ManagerEndpoints();
        Assert.True(endpoints.Length >= 21, $"Expected at least 21 manager endpoints but found {endpoints.Length}.");

        string[] unprotected = endpoints
            .Where(endpoint =>
                endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null ||
                !endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == AuthorizationPolicies.ManagerOnly))
            .Select(Describe)
            .ToArray();

        Assert.Empty(unprotected);
    }

    [Fact]
    public void ManagerEndpoints_NeverDeleteOrPatch()
    {
        // Records are deactivated, never deleted, so history and future order references stay valid.
        string[] deleting = ManagerEndpoints()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                .Any(method => HttpMethods.IsDelete(method) || HttpMethods.IsPatch(method)) == true)
            .Select(Describe)
            .ToArray();

        Assert.Empty(deleting);
    }

    private RouteEndpoint[] ManagerEndpoints() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("api/manager", StringComparison.Ordinal) == true)
            .ToArray();

    private static string Describe(RouteEndpoint endpoint) =>
        $"{string.Join(",", endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])} {endpoint.RoutePattern.RawText}";
}