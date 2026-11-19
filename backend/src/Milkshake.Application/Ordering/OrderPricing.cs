using Milkshake.Domain.Common;
using Milkshake.Domain.Pricing;

namespace Milkshake.Application.Ordering;

/// <summary>
/// A validated draft with its server-calculated price, field errors, or "ordering unavailable".
/// The price is an estimate: M7 recalculates it when the order is placed.
/// </summary>
public sealed record DraftPricingResult(
    ValidatedDraft? Draft,
    PriceBreakdown? Price,
    IReadOnlyDictionary<string, string[]> Errors,
    bool OrderingUnavailable)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    /// <summary>Valid and priced.</summary>
    public static DraftPricingResult Priced(ValidatedDraft draft, PriceBreakdown price) => new(draft, price, NoErrors, false);

    /// <summary>Some selections are missing or no longer available.</summary>
    public static DraftPricingResult Invalid(IReadOnlyDictionary<string, string[]> errors) => new(null, null, errors, false);

    /// <summary>Ordering cannot be offered right now (invalid configuration).</summary>
    public static DraftPricingResult Unavailable() => new(null, null, NoErrors, true);
}

/// <summary>
/// Prices orders on the server (M6). The browser never supplies prices, VAT or discounts: everything comes from the
/// options and settings in force at one instant. M7 will call this immediately before storing an order.
/// </summary>
public interface IOrderPricingService
{
    /// <summary>Validates a complete draft and prices it at the current instant.</summary>
    Task<DraftPricingResult> PriceDraftAsync(OrderDraft draft, CancellationToken cancellationToken);
}

/// <summary>Builds the calculator's input from server-side data only.</summary>
public static class OrderPricingInput
{
    /// <summary>
    /// The pricing input for a validated draft. Fees are those the draft was validated against, the VAT rate is the
    /// setting in force, and <paramref name="pricedAtUtc"/> is the instant both were read. No discount yet (decision P5).
    /// </summary>
    public static PricingInput For(ValidatedDraft draft, OrderingSettings settings, DateTimeOffset pricedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(settings);

        return new PricingInput(
            settings.CurrencyCode,
            settings.VatRate,
            draft.Drinks.Select(d => new DrinkToPrice(Component(d.Flavour), Component(d.Consistency), Component(d.Topping))).ToList(),
            Discount: null,
            pricedAtUtc);
    }

    /// <summary>One chosen option as a price component.</summary>
    public static PriceComponent Component(OrderingOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        return new PriceComponent(option.Id, option.Name, new Money(option.FeeAmount, option.CurrencyCode));
    }
}

/// <summary>
/// Default pricing service: loads the options once, validates the draft against them and prices it with the fees and
/// VAT rate of that same moment, so a price change during the calculation cannot mix old and new prices.
/// </summary>
public sealed class OrderPricingService(IOrderingCatalogService catalog) : IOrderPricingService
{
    /// <inheritdoc />
    public async Task<DraftPricingResult> PriceDraftAsync(OrderDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        OrderingOptionsResult current = await catalog.GetOptionsAsync(cancellationToken);
        if (current.Options is not OrderingOptions options)
        {
            return DraftPricingResult.Unavailable();
        }

        DraftValidationResult validation = OrderDraftRules.Validate(draft, options);
        if (validation.Draft is not ValidatedDraft validated)
        {
            return DraftPricingResult.Invalid(validation.Errors);
        }

        // The pickup window was computed at the instant the fees were read: that instant prices the whole order.
        PriceBreakdown price = PriceCalculator.Calculate(OrderPricingInput.For(validated, options.Settings, options.Pickup.AsOfUtc));
        return DraftPricingResult.Priced(validated, price);
    }
}