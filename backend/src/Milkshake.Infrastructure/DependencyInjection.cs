using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Milkshake.Infrastructure.Persistence;
using Npgsql;

namespace Milkshake.Infrastructure;

/// <summary>
/// Registers infrastructure services: persistence, identity stores, email and payment clients.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Name of the connection string under the "ConnectionStrings" configuration section.
    /// The connection string holds everything except the password.
    /// </summary>
    public const string ConnectionStringName = "MilkshakeDb";

    /// <summary>
    /// Configuration key for the database password, stored separately from the connection string
    /// (user secrets locally; environment variable "Database__Password" or a secrets store in production).
    /// </summary>
    public const string DatabasePasswordKey = "Database:Password";

    /// <summary>
    /// Adds the infrastructure layer to the service collection.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // The connection string is built when the DbContext is first created, not at startup,
        // so the app (and the test host) can start without a database configured.
        services.AddDbContext<MilkshakeDbContext>(options =>
            options.UseNpgsql(BuildConnectionString(configuration), npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", MilkshakeDbContext.Schema);
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
            }));

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>(
                name: "database",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["db", "ready"],
                timeout: TimeSpan.FromSeconds(20));

        return services;
    }

    /// <summary>
    /// Combines the password-free connection string with the separately stored password.
    /// NpgsqlConnectionStringBuilder escapes every value correctly, whatever characters the password contains.
    /// </summary>
    private static string BuildConnectionString(IConfiguration configuration)
    {
        string baseConnectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. " +
                $"For local development, set it with: dotnet user-secrets set \"ConnectionStrings:{ConnectionStringName}\" \"<value>\" --project backend/src/Milkshake.Api");

        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString);

        string? password = configuration[DatabasePasswordKey];
        if (!string.IsNullOrEmpty(password))
        {
            builder.Password = password;
        }

        return builder.ConnectionString;
    }
}