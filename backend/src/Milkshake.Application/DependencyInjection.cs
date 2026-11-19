using Microsoft.Extensions.DependencyInjection;
using Milkshake.Application.Ordering;

namespace Milkshake.Application;

/// <summary>
/// Registers application-layer services (use cases and rules that depend only on interfaces).
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds the application layer to the service collection.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IOrderDraftValidator, OrderDraftValidator>();
        services.AddScoped<IOrderPricingService, OrderPricingService>();
        services.AddScoped<IDiscountResolver, DiscountResolver>();

        // No order history exists until M7/M8; replace this registration with a source that reads paid orders.
        services.AddScoped<IDiscountEligibilitySource, NoOrderHistoryYet>();


        return services;
    }
}