using Milkshake.Domain.Pricing;
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

    /// <summary>Maps to the application model.</summary>
    public DraftDrinkSelection ToSelection() => new(FlavourId, ConsistencyId, ToppingId);
}

/// <summary>
/// POST /api/ordering/draft/validate. The number of drinks is the length of <see cref="Drinks"/>.
/// Unknown fields are rejected; prices, VAT rates and discounts are never accepted from the browser.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ValidateDraftRequest
{
    /// <summary>An active restaurant id.</summary>
    public int? RestaurantId { get; init; }

    /// <summary>
    /// Pickup date and time in the business time zone, "yyyy-MM-ddTHH:mm" (no seconds, no offset),
    /// between pickup.earliestLocal and pickup.latestLocal from GET /api/ordering/options.
    /// </summary>
    public string? PickupLocal { get; init; }

    /// <summary>One entry per milkshake.</summary>
    public IReadOnlyList<DraftDrinkRequest?>? Drinks { get; init; }

    /// <summary>Maps to the application model.</summary>
    public OrderDraft ToDraft() => new(RestaurantId, PickupLocal, Drinks?.Select(d => d?.ToSelection()).ToList() ?? []);
}

/// <summary>
/// POST /api/ordering/quote: the milkshakes being chosen, identifiers only (no restaurant or pickup time yet).
/// Unknown fields are rejected; prices, VAT rates, discounts and purchase counts are never accepted from the browser.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class QuoteRequest
{
    /// <summary>One entry per milkshake; choices not yet made are null.</summary>
    public IReadOnlyList<DraftDrinkRequest?>? Drinks { get; init; }

    /// <summary>Maps to the application model.</summary>
    public IReadOnlyList<DraftDrinkSelection?> ToSelections() => Drinks?.Select(d => d?.ToSelection()).ToList() ?? [];
}

/// <summary>The frequent-customer discount applied. 0.00 with no tier while discounts are deferred (decision P5).</summary>
public sealed record DiscountResponse(decimal Amount, int? TierNumber, decimal? Rate, bool CapApplied);

/// <summary>
/// Order totals calculated on the server (M6 rules P1–P4): subtotal, discount (before VAT), taxable amount, VAT rate
/// and amount, and total. Amounts are in <see cref="CurrencyCode"/>; the VAT rate is a fraction (0.15 = 15%).
/// </summary>
public sealed record OrderTotalsResponse(
    string CurrencyCode,
    decimal Subtotal,
    DiscountResponse Discount,
    decimal Taxable,
    decimal VatRate,
    decimal Vat,
    decimal Total)
{
    /// <summary>Maps the calculator's breakdown.</summary>
    public static OrderTotalsResponse From(PriceBreakdown price)
    {
        ArgumentNullException.ThrowIfNull(price);
        AppliedDiscount discount = price.Discount;

        return new OrderTotalsResponse(
            price.CurrencyCode,
            price.Subtotal.Amount,
            new DiscountResponse(discount.Amount.Amount, discount.TierNumber, discount.Rate, discount.CapApplied),
            price.Taxable.Amount,
            price.VatRate,
            price.Vat.Amount,
            price.Total.Amount);
    }
}

/// <summary>Wording shared by every price estimate.</summary>
public static class EstimateNotes
{
    /// <summary>Shown with every quote.</summary>
    public const string Estimate =
        "Estimated prices based on current fees and VAT. The final amount is calculated when you place your order.";
}

/// <summary>One milkshake in a live quote: its price, or null until all three choices are made.</summary>
public sealed record QuotedDrinkResponse(int Number, decimal? Price);

/// <summary>
/// A non-binding live estimate (POST /api/ordering/quote). <see cref="Totals"/> is null until every milkshake is
/// complete. Nothing is stored; the amount is recalculated when the order is placed.
/// </summary>
public sealed record QuoteResponse(
    bool IsEstimate,
    DateTimeOffset PricedAtUtc,
    string CurrencyCode,
    decimal VatRate,
    IReadOnlyList<QuotedDrinkResponse> Drinks,
    OrderTotalsResponse? Totals,
    string Note)
{
    /// <summary>Maps the application model.</summary>
    public static QuoteResponse From(DrinksQuote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);

        return new QuoteResponse(
            IsEstimate: true,
            quote.PricedAtUtc,
            quote.CurrencyCode,
            quote.VatRate,
            quote.Lines.Select(line => new QuotedDrinkResponse(line.Number, line.Price?.Amount)).ToList(),
            quote.Totals is null ? null : OrderTotalsResponse.From(quote.Totals),
            EstimateNotes.Estimate);
    }
}

/// <summary>A validated drink with the names and current fees of its choices, and its price (the sum of the fees).</summary>
public sealed record DraftDrinkResponse(
    int Number,
    OrderingOptionResponse Flavour,
    OrderingOptionResponse Consistency,
    OrderingOptionResponse Topping,
    decimal Price);

/// <summary>The chosen pickup time: business-local wall clock, the UTC instant, and the time zone.</summary>
public sealed record DraftPickupResponse(string Local, DateTimeOffset Utc, string TimeZone);

/// <summary>
/// A draft whose selections are valid right now, with its estimated price. Not an order: nothing has been stored or
/// paid, and the amount is recalculated when the order is placed.
/// </summary>
public sealed record DraftSummaryResponse(
    bool IsDraft,
    OrderingRestaurantResponse Restaurant,
    DraftPickupResponse Pickup,
    IReadOnlyList<DraftDrinkResponse> Drinks,
    OrderTotalsResponse Totals,
    bool IsEstimate,
    DateTimeOffset PricedAtUtc,
    string Note)
{
    /// <summary>Shown with every draft summary.</summary>
    public const string DraftNote =
        "Draft – not yet ordered or paid. Prices are an estimate based on current fees and VAT; the final amount is calculated when you place your order.";

    /// <summary>Maps the application model.</summary>
    public static DraftSummaryResponse From(ValidatedDraft draft, PriceBreakdown price)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(price);
        Dictionary<int, decimal> drinkPrices = price.Drinks.ToDictionary(d => d.Number, d => d.Price.Amount);

        return new DraftSummaryResponse(
            IsDraft: true,
            new OrderingRestaurantResponse(draft.Restaurant.Id, draft.Restaurant.Name, draft.Restaurant.Address),
            new DraftPickupResponse(
                draft.Pickup.Local.ToString(PickupRules.LocalFormat, CultureInfo.InvariantCulture),
                draft.Pickup.Utc,
                draft.Pickup.TimeZoneId),
            draft.Drinks
                .Select(d => new DraftDrinkResponse(d.Number, Option(d.Flavour), Option(d.Consistency), Option(d.Topping), drinkPrices[d.Number]))
                .ToList(),
            OrderTotalsResponse.From(price),
            IsEstimate: true,
            price.PricedAtUtc,
            DraftNote);
    }

    private static OrderingOptionResponse Option(OrderingOption option) =>
        new(option.Id, option.Name, new FeeResponse(option.FeeAmount, option.CurrencyCode));
}