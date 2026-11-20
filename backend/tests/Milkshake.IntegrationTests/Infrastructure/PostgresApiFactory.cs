using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api;
using Milkshake.Infrastructure;
using Milkshake.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Milkshake.IntegrationTests.Infrastructure;

/// <summary>
/// Runs the API against a throwaway PostgreSQL container (Docker), migrated with the real migrations.
/// Never touches Supabase. One container is shared by all tests in <see cref="PostgresTestGroup"/>.
/// </summary>
public sealed class PostgresApiFactory : WebApplicationFactory<ApiAssemblyMarker>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Starts the container and applies migrations (which also seed the roles).</summary>
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        using IServiceScope scope = Services.CreateScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        await db.Database.MigrateAsync();
    }

    /// <summary>Client over https so Secure cookies are issued and sent back.</summary>
    public HttpClient CreateHttpsClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Testing");
        builder.UseSetting($"ConnectionStrings:{DependencyInjection.ConnectionStringName}", _postgres.GetConnectionString());
        builder.UseSetting("RateLimiting:AuthPermitLimit", "1000");
        builder.UseSetting("RateLimiting:OrderingPermitLimit", "1000");

        // Tests call the payment reconciler directly; the timer-driven job would only add noise.
        builder.UseSetting("Payments:Reconciliation:Enabled", "false");
        builder.UseSetting("Email:Dispatch:Enabled", "false");
    }
}

/// <summary>Tests that share one PostgreSQL container; they run one after another.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresTestGroup : ICollectionFixture<PostgresApiFactory>
{
    /// <summary>Collection name used in [Collection(...)].</summary>
    public const string Name = "Postgres";
}