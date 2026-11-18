using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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

    
    [Fact]
    public void OnlyLoginAndRegisterAcceptAnonymousStateChangingRequests()
    {
        EndpointDataSource endpoints = factory.Services.GetRequiredService<EndpointDataSource>();

        string[] anonymousWrites = endpoints.Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Where(endpoint => endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                .Any(method => !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method)) == true)
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Anything else here would be an unauthenticated way to change data - for example a manager sign-up.
        Assert.Equal(new[] { "api/auth/login", "api/auth/register" }, anonymousWrites);
    }

    
    [Fact]
    public void SessionCookie_IsHttpOnlySecureStrictAndExpiresAfterTwoIdleHours()
    {
        CookieAuthenticationOptions options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.Equal("milkshake.session", options.Cookie.Name);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Strict, options.Cookie.SameSite);
        Assert.Equal(TimeSpan.FromHours(2), options.ExpireTimeSpan);
        Assert.True(options.SlidingExpiration);
    }

    [Fact]
    public void IdentityPolicy_MatchesTheAgreedPasswordLockoutAndSessionRules()
    {
        IdentityOptions identity = factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;
        SecurityStampValidatorOptions stamp = factory.Services.GetRequiredService<IOptions<SecurityStampValidatorOptions>>().Value;

        Assert.Equal(8, identity.Password.RequiredLength);
        Assert.True(identity.Password.RequireDigit);
        Assert.True(identity.Password.RequireLowercase);
        Assert.True(identity.Password.RequireUppercase);
        Assert.True(identity.User.RequireUniqueEmail);
        Assert.Equal(5, identity.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), identity.Lockout.DefaultLockoutTimeSpan);
        Assert.Equal(TimeSpan.FromMinutes(5), stamp.ValidationInterval);
    }
}