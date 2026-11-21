using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Api.Hosting;

/// <summary>
/// Production hosting (M14 Step 4): one container serves the built website and the API on one origin behind the
/// host's HTTPS proxy. Every part is switched on by configuration, so development, the tests and the E2E stack keep
/// their current behaviour unless a test turns a part on.
/// </summary>
public static class ProductionHosting
{
    /// <summary>Configuration section for the Data Protection key ring.</summary>
    public const string DataProtectionSection = "DataProtection";

    /// <summary>
    /// Behind the host's proxy, trusts one hop of the client-address header and <c>X-Forwarded-Proto</c>, so rate limits
    /// and sign-in events see the client's address and the app knows the request was HTTPS (HTTPS redirection, HSTS,
    /// Secure cookies). Off unless <c>ForwardedHeaders:Enabled</c> is true: the app must only ever be reachable through
    /// that proxy when it is on, or a client could forge its address.
    /// <para>
    /// <c>ForwardedHeaders:ForwardedForHeaderName</c> (default <c>X-Forwarded-For</c>) names the header that carries the
    /// client's address. On Render (M14 E-3) it is <c>CF-Connecting-IP</c>: Render's proxy only appends to
    /// <c>X-Forwarded-For</c>, so its last entry is a proxy, while Cloudflare, in front of every Render service, sets
    /// <c>CF-Connecting-IP</c> itself from the real connection.
    /// </para>
    /// </summary>
    public static IServiceCollection AddForwardedHeadersFromConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue("ForwardedHeaders:Enabled", false))
        {
            return services;
        }

        return services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            string? forwardedForHeader = configuration["ForwardedHeaders:ForwardedForHeaderName"];
            if (!string.IsNullOrWhiteSpace(forwardedForHeader))
            {
                options.ForwardedForHeaderName = forwardedForHeader.Trim();
            }

            // The proxy's address is not known in advance on a shared platform; the app is reachable only through it.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });
    }

    /// <summary>Applies the forwarded headers first in the pipeline when they are enabled.</summary>
    public static IApplicationBuilder UseForwardedHeadersFromConfiguration(this IApplicationBuilder app, IConfiguration configuration) =>
        configuration.GetValue("ForwardedHeaders:Enabled", false) ? app.UseForwardedHeaders() : app;

    /// <summary>
    /// Decision D-3: the key ring that protects session and anti-forgery cookies is stored in PostgreSQL
    /// (<c>milkshake.data_protection_keys</c>), so sign-ins survive restarts and redeploys on a host without a
    /// persistent disk. Keys are encrypted with a certificate given as base64 PKCS#12
    /// (<c>DataProtection:CertificateBase64</c>, optional <c>DataProtection:CertificatePassword</c>). Off unless
    /// <c>DataProtection:StoreKeysInDatabase</c> is true; in Production a store without a certificate is refused, so
    /// keys are never stored unencrypted there.
    /// </summary>
    public static IServiceCollection AddDataProtectionFromConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        IConfigurationSection section = configuration.GetSection(DataProtectionSection);
        if (!section.GetValue("StoreKeysInDatabase", false))
        {
            return services;
        }

        IDataProtectionBuilder dataProtection = services.AddDataProtection()
            .SetApplicationName("Milkshake")
            .PersistKeysToDbContext<MilkshakeDbContext>();

        string? certificateBase64 = section["CertificateBase64"];
        if (string.IsNullOrWhiteSpace(certificateBase64))
        {
            if (environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "DataProtection:CertificateBase64 is required in Production when keys are stored in the database.");
            }

            return services;
        }

        X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12(
            Convert.FromBase64String(certificateBase64),
            section["CertificatePassword"],
            X509KeyStorageFlags.EphemeralKeySet);
        dataProtection.ProtectKeysWithCertificate(certificate).UnprotectKeysWithAnyCertificate(certificate);
        return services;
    }

    /// <summary>True when the built website (<c>wwwroot/index.html</c>) is present, as in the production container.</summary>
    public static bool HasSite(this IWebHostEnvironment environment) =>
        environment.WebRootFileProvider.GetFileInfo("index.html").Exists;

    /// <summary>
    /// Serves the built website's files. Hashed assets (<c>/assets/…</c>) are cached for a year; everything else,
    /// including <c>index.html</c>, is revalidated so a new release is picked up at once.
    /// </summary>
    public static IApplicationBuilder UseSiteFiles(this IApplicationBuilder app)
    {
        app.UseDefaultFiles();
        return app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                bool hashed = context.Context.Request.Path.StartsWithSegments("/assets", StringComparison.Ordinal);
                context.Context.Response.Headers.CacheControl = hashed ? "public, max-age=31536000, immutable" : "no-cache";
            },
        });
    }

    /// <summary>
    /// Client-side routes (<c>/orders/MS-…</c>, <c>/manager/reports</c>) return <c>index.html</c>; unknown <c>/api</c>
    /// paths stay a 404 problem response and never return the page.
    /// </summary>
    public static void MapSiteFallback(this WebApplication app)
    {
        app.Map("/api/{**path}", () => Results.NotFound()).AllowAnonymous();
        app.MapFallbackToFile("index.html", new StaticFileOptions
        {
            OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
        }).AllowAnonymous();
    }
}
