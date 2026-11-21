using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api;
using Milkshake.Domain.Auditing;
using Milkshake.Infrastructure.Persistence;
using Milkshake.IntegrationTests.Infrastructure;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M14 Step 4 production hosting, each part switched on by configuration: forwarded headers behind the host's proxy,
/// and the Data Protection key ring stored (encrypted) in PostgreSQL so sign-ins survive a restart (decision D-3).
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class ProductionHostingTests(PostgresApiFactory factory)
{
    private const string LoginPath = "/api/auth/login";
    private const string MePath = "/api/auth/me";
    private const string SessionCookie = "milkshake.session";
    private const string ForwardedClientIp = "203.0.113.7";  // TEST-NET-3: documentation range, never a real client

    [Fact]
    public async Task ForwardedHeadersEnabled_SignInEventRecordsTheForwardedClientAddress()
    {
        using WebApplicationFactory<ApiAssemblyMarker> behindProxy =
            factory.WithWebHostBuilder(builder => builder.UseSetting("ForwardedHeaders:Enabled", "true"));
        string email = await RegisterAsync(behindProxy);

        await FailLoginFromAsync(behindProxy, email);

        Assert.Equal(ForwardedClientIp, await LoginFailedIpAsync(email));
    }

    [Fact]
    public async Task ForwardedForHeaderName_TakesTheClientAddressFromTheConfiguredHeaderOnly()
    {
        // Render (M14 E-3): the address comes from CF-Connecting-IP, set by Cloudflare; a client-supplied
        // X-Forwarded-For is ignored, so it cannot choose the address that is recorded and rate limited.
        using WebApplicationFactory<ApiAssemblyMarker> behindRender = factory.WithWebHostBuilder(builder => builder
            .UseSetting("ForwardedHeaders:Enabled", "true")
            .UseSetting("ForwardedHeaders:ForwardedForHeaderName", "CF-Connecting-IP"));
        string email = await RegisterAsync(behindRender);

        using (HttpClient client = behindRender.CreateSecureClient())
        {
            client.DefaultRequestHeaders.Add("CF-Connecting-IP", "203.0.113.9");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", ForwardedClientIp);
            client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
            using HttpResponseMessage response = await client.PostWithCsrfAsync(LoginPath, new LoginBody(email, "Wrong1Password"));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        Assert.Equal("203.0.113.9", await LoginFailedIpAsync(email));
    }

    [Fact]
    public async Task ForwardedHeadersOff_TheHeaderIsIgnored()
    {
        // Negative control: without the setting a client cannot choose the address that is recorded and rate limited.
        string email = await RegisterAsync(factory);

        await FailLoginFromAsync(factory, email);

        Assert.NotEqual(ForwardedClientIp, await LoginFailedIpAsync(email));
    }

    [Fact]
    public async Task DatabaseKeyRing_SignInSurvivesARestart_AndKeysAreStoredEncrypted()
    {
        string certificate = CreateCertificateBase64(out string password);

        string sessionCookie;
        using (WebApplicationFactory<ApiAssemblyMarker> first = WithDatabaseKeys(certificate, password))
        {
            sessionCookie = await RegisterAndSignInAsync(first);
        }

        // A second host with no shared memory or disk: only the database (and the certificate) connects them.
        using WebApplicationFactory<ApiAssemblyMarker> second = WithDatabaseKeys(certificate, password);
        Assert.Equal(HttpStatusCode.OK, await GetMeAsync(second, sessionCookie));

        using IServiceScope scope = factory.Services.CreateScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        List<string?> keys = await db.DataProtectionKeys.AsNoTracking().Select(k => k.Xml).ToListAsync();
        Assert.NotEmpty(keys);
        Assert.All(keys, xml =>
        {
            Assert.NotNull(xml);
            Assert.Contains("encryptedSecret", xml, StringComparison.Ordinal);
            Assert.DoesNotContain("<masterKey", xml, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task SeparateKeyRing_RejectsTheSessionCookie()
    {
        // Negative control for the test above: a host with its own, empty key ring cannot read the cookie, so the
        // restart test passes because of the shared database key ring and not by accident.
        string certificate = CreateCertificateBase64(out string password);
        string sessionCookie;
        using (WebApplicationFactory<ApiAssemblyMarker> first = WithDatabaseKeys(certificate, password))
        {
            sessionCookie = await RegisterAndSignInAsync(first);
        }

        string keyDirectory = Path.Combine(Path.GetTempPath(), "milkshake-dp-" + Guid.NewGuid().ToString("N"));
        try
        {
            using WebApplicationFactory<ApiAssemblyMarker> isolated = factory.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services => services.AddDataProtection()
                    .SetApplicationName("Milkshake")
                    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))));

            Assert.Equal(HttpStatusCode.Unauthorized, await GetMeAsync(isolated, sessionCookie));
        }
        finally
        {
            if (Directory.Exists(keyDirectory))
            {
                Directory.Delete(keyDirectory, recursive: true);
            }
        }
    }

    private WebApplicationFactory<ApiAssemblyMarker> WithDatabaseKeys(string certificateBase64, string password) =>
        factory.WithWebHostBuilder(builder => builder
            .UseSetting("DataProtection:StoreKeysInDatabase", "true")
            .UseSetting("DataProtection:CertificateBase64", certificateBase64)
            .UseSetting("DataProtection:CertificatePassword", password));

    /// <summary>A throwaway self-signed certificate, as base64 PKCS#12 (the form the host secret takes).</summary>
    private static string CreateCertificateBase64(out string password)
    {
        password = Guid.NewGuid().ToString("N");
        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Milkshake Data Protection (test)", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12, password));
    }

    private static async Task<string> RegisterAsync(WebApplicationFactory<ApiAssemblyMarker> app)
    {
        using HttpClient client = app.CreateSecureClient();
        RegisterBody body = RegisterBody.Valid();
        using HttpResponseMessage response = await client.PostWithCsrfAsync("/api/auth/register", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return body.Email;
    }

    /// <summary>Registers and signs in a patron; returns the session cookie as a request header value.</summary>
    private static async Task<string> RegisterAndSignInAsync(WebApplicationFactory<ApiAssemblyMarker> app)
    {
        string email = await RegisterAsync(app);
        using HttpClient client = app.CreateSecureClient();
        using HttpResponseMessage login = await client.PostWithCsrfAsync(LoginPath, new LoginBody(email, RegisterBody.ValidPassword));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return login.GetSetCookie(SessionCookie).Split(';')[0];
    }

    private static async Task<HttpStatusCode> GetMeAsync(WebApplicationFactory<ApiAssemblyMarker> app, string sessionCookie)
    {
        using HttpClient client = app.CreateSecureClient(handleCookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(MePath, UriKind.Relative));
        request.Headers.Add("Cookie", sessionCookie);
        using HttpResponseMessage response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static async Task FailLoginFromAsync(WebApplicationFactory<ApiAssemblyMarker> app, string email)
    {
        using HttpClient client = app.CreateSecureClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ForwardedClientIp);
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        using HttpResponseMessage response = await client.PostWithCsrfAsync(LoginPath, new LoginBody(email, "Wrong1Password"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<string?> LoginFailedIpAsync(string email)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        return await db.SecurityEvents.AsNoTracking()
            .Where(e => e.Email == email && e.EventType == SecurityEventType.LoginFailed)
            .Select(e => e.IpAddress)
            .SingleAsync();
    }
}
