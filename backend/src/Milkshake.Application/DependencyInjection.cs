using Microsoft.Extensions.DependencyInjection;

namespace Milkshake.Application;

/// <summary>
/// Registers application-layer services (use cases, pricing, discounts, validation).
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds the application layer to the service collection.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Application services (ordering, pricing, discount engine, lookup management)
        // are registered here in later milestones.

        return services;
    }
}