using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Milkshake.Api;

namespace Milkshake.IntegrationTests.Infrastructure;

/// <summary>
/// Starts the API in memory for integration tests. Uses the "Testing" environment, so user
/// secrets are not loaded and the shared Supabase database is never touched.
/// </summary>
public sealed class MilkshakeApiFactory : WebApplicationFactory<ApiAssemblyMarker>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");
        builder.UseSetting("Payments:Reconciliation:Enabled", "false");
        builder.UseSetting("Email:Dispatch:Enabled", "false");

        builder.ConfigureTestServices(services =>
            services.PostConfigure<HealthCheckServiceOptions>(options =>
            {
                // No database here: drop every check that reads it ("database", and the M14 "workers" check).
                foreach (HealthCheckRegistration registration in options.Registrations
                             .Where(registration => registration.Tags.Contains("db")).ToList())
                {
                    options.Registrations.Remove(registration);
                }
            }));
    }

    /// <summary>
    /// Client that talks to the in-memory API over https, so Secure cookies are issued and sent back.
    /// </summary>
    public HttpClient CreateHttpsClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
}