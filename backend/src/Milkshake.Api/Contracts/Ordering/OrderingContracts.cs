using System.Globalization;
using Milkshake.Application.Ordering;

namespace Milkshake.Api.Contracts.Ordering;

/// <summary>An amount in an ISO 4217 currency.</summary>
public sealed record FeeResponse(decimal Amount, string CurrencyCode);

/// <summary>A flavour, consistency or topping a patron can choose, with its current fee (not a final price).</summary>
public sealed record OrderingOptionResponse(int Id, string Name, FeeResponse Fee);

/// <summary>An active pickup restaurant.</summary>
public sealed record OrderingRestaurantResponse(int Id, string Name, string? Address);

/// <summary>Configured drink limits.</summary>
public sealed record DrinkLimitsResponse(int Min, int Max);

/// <summary>
/// Allowed pickup times. Local values ("2026-11-19T15:30") are in <see cref="TimeZone"/>, not the browser's zone.
/// PROVISIONAL rules (Q-05, Q-06, Q-17, Q-18).
/// </summary>
public sealed record PickupWindowResponse(
    string TimeZone,
    int LeadTimeMinutes,
    int MaxAdvanceDays,
    string EarliestLocal,
    string LatestLocal,
    DateTimeOffset EarliestUtc,
    DateTimeOffset LatestUtc,
    bool HasAvailableTimes);

/// <summary>How to format amounts for display.</summary>
public sealed record DisplayResponse(string Locale, string CurrencyCode);

/// <summary>GET /api/ordering/options.</summary>
public sealed record OrderingOptionsResponse(
    IReadOnlyList<OrderingOptionResponse> Flavours,
    IReadOnlyList<OrderingOptionResponse> Consistencies,
    IReadOnlyList<OrderingOptionResponse> Toppings,
    IReadOnlyList<OrderingRestaurantResponse> Restaurants,
    DrinkLimitsResponse DrinkLimits,
    PickupWindowResponse Pickup,
    DisplayResponse Display)
{
    /// <summary>Maps the application model.</summary>
    public static OrderingOptionsResponse From(OrderingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        OrderingSettings settings = options.Settings;
        PickupWindow pickup = options.Pickup;

        return new OrderingOptionsResponse(
            options.Flavours.Select(ToResponse).ToList(),
            options.Consistencies.Select(ToResponse).ToList(),
            options.Toppings.Select(ToResponse).ToList(),
            options.Restaurants.Select(r => new OrderingRestaurantResponse(r.Id, r.Name, r.Address)).ToList(),
            new DrinkLimitsResponse(settings.MinDrinks, settings.MaxDrinks),
            new PickupWindowResponse(
                settings.TimeZoneId,
                settings.LeadTimeMinutes,
                settings.MaxAdvanceDays,
                pickup.EarliestLocal.ToString(PickupRules.LocalFormat, CultureInfo.InvariantCulture),
                pickup.LatestLocal.ToString(PickupRules.LocalFormat, CultureInfo.InvariantCulture),
                pickup.EarliestUtc,
                pickup.LatestUtc,
                !pickup.IsEmpty),
            new DisplayResponse(settings.DisplayLocale, settings.CurrencyCode));
    }

    private static OrderingOptionResponse ToResponse(OrderingOption option) =>
        new(option.Id, option.Name, new FeeResponse(option.FeeAmount, option.CurrencyCode));
}