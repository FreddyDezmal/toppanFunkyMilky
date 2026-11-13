using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Milkshake.Infrastructure;

/// <summary>
/// Registers infrastructure services: persistence, identity stores, email and payment clients.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds the infrastructure layer to the service collection.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Step 4: EF Core DbContext (Npgsql + Supabase) and the database health check.

        return services;
    }
}