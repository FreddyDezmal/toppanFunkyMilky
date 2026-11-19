using System.Globalization;
using System.Text.Json.Serialization;
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


/// <summary>One drink in a draft: identifiers only. Unknown fields (e.g. a price) are rejected.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DraftDrinkRequest
{
    /// <summary>An active flavour id from GET /api/ordering/options.</summary>
    public int? FlavourId { get; init; }

    /// <summary>An active consistency id.</summary>
    public int? ConsistencyId { get; init; }

    /// <summary>An active topping id.</summary>
    public int? ToppingId { get; init; }
}

/// <summary>
/// POST /api/ordering/draft/validate. The number of drinks is the length of <see cref="Drinks"/>.
/// Unknown fields are rejected; prices are never accepted from the browser.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ValidateDraftRequest
{
    /// <summary>An active restaurant id.</summary>
    public int? RestaurantId { get; init; }

    /// <summary>One entry per milkshake.</summary>
    public IReadOnlyList<DraftDrinkRequest?>? Drinks { get; init; }

    /// <summary>Maps to the application model.</summary>
    public OrderDraft ToDraft() =>
        new(
            RestaurantId,
            Drinks?.Select(d => d is null ? null : new DraftDrinkSelection(d.FlavourId, d.ConsistencyId, d.ToppingId)).ToList() ?? []);
}

/// <summary>A validated drink with the names and current fees of its choices.</summary>
public sealed record DraftDrinkResponse(
    int Number,
    OrderingOptionResponse Flavour,
    OrderingOptionResponse Consistency,
    OrderingOptionResponse Topping);

/// <summary>A draft whose selections are valid right now. Not an order: nothing has been stored or paid.</summary>
public sealed record DraftSummaryResponse(
    bool IsDraft,
    OrderingRestaurantResponse Restaurant,
    IReadOnlyList<DraftDrinkResponse> Drinks,
    string Note)
{
    /// <summary>Shown with every draft summary.</summary>
    public const string DraftNote =
        "Draft – not yet ordered or paid. Fees shown are current fees; the final price, VAT and any discount are calculated at checkout.";

    /// <summary>Maps the application model.</summary>
    public static DraftSummaryResponse From(ValidatedDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return new DraftSummaryResponse(
            IsDraft: true,
            new OrderingRestaurantResponse(draft.Restaurant.Id, draft.Restaurant.Name, draft.Restaurant.Address),
            draft.Drinks.Select(d => new DraftDrinkResponse(d.Number, Option(d.Flavour), Option(d.Consistency), Option(d.Topping))).ToList(),
            DraftNote);
    }

    private static OrderingOptionResponse Option(OrderingOption option) =>
        new(option.Id, option.Name, new FeeResponse(option.FeeAmount, option.CurrencyCode));
}