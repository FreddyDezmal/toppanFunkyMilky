using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Milkshake.Api;
using Milkshake.Api.Security;
using Milkshake.IntegrationTests.Infrastructure;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M14 Step 4: the production container serves the built website from wwwroot on the API's origin (decision D-2), so
/// cookies stay SameSite=Strict with no CORS. A temporary web root stands in for the Vite build.
/// </summary>
public sealed class SiteServingTests : IClassFixture<MilkshakeApiFactory>, IDisposable
{
    private const string IndexMarker = "<!-- milkshake site test -->";
    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "milkshake-site-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<ApiAssemblyMarker> _site;

    public SiteServingTests(MilkshakeApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), $"<!doctype html><html><body>{IndexMarker}</body></html>");
        File.WriteAllText(Path.Combine(_webRoot, "assets", "app-abc123.js"), "console.log('site');");
        _site = factory.WithWebHostBuilder(builder => builder.UseWebRoot(_webRoot));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/orders/MS-ABC123")]   // a client-side route: falls back to index.html
    [InlineData("/management/reports")]
    public async Task SitePaths_ServeIndexWithSiteHeadersAndNoCaching(string path)
    {
        using HttpClient client = _site.CreateSecureClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(IndexMarker, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(SecurityHeaders.SiteContentSecurityPolicy, Single(response, "Content-Security-Policy"));
        Assert.Equal("strict-origin-when-cross-origin", Single(response, "Referrer-Policy"));
        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Single(response, "X-Frame-Options"));
        Assert.Contains("no-cache", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HashedAssets_AreCachedForAYear()
    {
        using HttpClient client = _site.CreateSecureClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/assets/app-abc123.js", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string cacheControl = response.Headers.CacheControl?.ToString() ?? string.Empty;
        Assert.Contains("max-age=31536000", cacheControl, StringComparison.Ordinal);
        Assert.Contains("immutable", cacheControl, StringComparison.Ordinal);
        Assert.Equal(SecurityHeaders.SiteContentSecurityPolicy, Single(response, "Content-Security-Policy"));
    }

    [Fact]
    public async Task UnknownApiPath_IsA404WithApiHeaders_NotTheSite()
    {
        using HttpClient client = _site.CreateSecureClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/no-such-endpoint", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(IndexMarker, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(SecurityHeaders.ApiContentSecurityPolicy, Single(response, "Content-Security-Policy"));
        Assert.Equal("no-referrer", Single(response, "Referrer-Policy"));
    }

    [Fact]
    public async Task ApiEndpoints_StillAnswerAlongsideTheSite()
    {
        using HttpClient client = _site.CreateSecureClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(SecurityHeaders.ApiContentSecurityPolicy, Single(response, "Content-Security-Policy"));
    }

    [Fact]
    public async Task WithoutAWebRoot_TheApiServesNoSite()
    {
        // Development and the other tests: no wwwroot, so "/" is not the website. The app's deny-by-default
        // authorization policy answers it (401), as before M14.
        using WebApplicationFactory<ApiAssemblyMarker> apiOnly = _site.WithWebHostBuilder(builder =>
            builder.UseWebRoot(Path.Combine(_webRoot, "does-not-exist")));
        using HttpClient client = apiOnly.CreateSecureClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/", UriKind.Relative));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(IndexMarker, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _site.Dispose();
        if (Directory.Exists(_webRoot))
        {
            Directory.Delete(_webRoot, recursive: true);
        }
    }

    private static string Single(HttpResponseMessage response, string header) =>
        Assert.Single(response.Headers.GetValues(header));
}
