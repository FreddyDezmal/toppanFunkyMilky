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

        builder.ConfigureTestServices(services =>
            services.PostConfigure<HealthCheckServiceOptions>(options =>
            {
                HealthCheckRegistration? database = options.Registrations
                    .FirstOrDefault(registration => registration.Name == "database");

                if (database is not null)
                {
                    options.Registrations.Remove(database);
                }
            }));
    }
}