using Milkshake.Domain.Common;

namespace Milkshake.Domain.Pricing;

/// <summary>A chosen flavour, consistency or topping with the fee in force at the pricing instant.</summary>
public sealed record PriceComponent(int Id, string Name, Money Fee);

/// <summary>One milkshake to price: exactly one flavour, consistency and topping.</summary>
public sealed record DrinkToPrice(PriceComponent Flavour, PriceComponent Consistency, PriceComponent Topping);

/// <summary>
/// A frequent-customer discount to apply: a fraction of the subtotal, limited by a cap.
/// Never supplied in M6 (decision P5: the discount stays 0.00 until Q-01 to Q-04 are answered and order history exists).
/// </summary>
public sealed record DiscountTerms(int TierNumber, decimal Rate, Money Cap);

/// <summary>Everything a price calculation needs, read on the server at one instant.</summary>
public sealed record PricingInput(
    string CurrencyCode,
    decimal VatRate,
    IReadOnlyList<DrinkToPrice> Drinks,
    DiscountTerms? Discount,
    DateTimeOffset PricedAtUtc);

/// <summary>A priced milkshake: its three components and their sum.</summary>
public sealed record PricedDrink(int Number, PriceComponent Flavour, PriceComponent Consistency, PriceComponent Topping, Money Price);

/// <summary>The discount actually applied. Tier and rate are null when no discount applies.</summary>
public sealed record AppliedDiscount(Money Amount, int? TierNumber, decimal? Rate, bool CapApplied)
{
    /// <summary>No discount (shown to patrons as 0.00).</summary>
    public static AppliedDiscount None(string currencyCode) => new(new Money(0m, currencyCode), null, null, false);
}

/// <summary>
/// The full price of an order at one instant: per-drink prices, subtotal, discount, VAT and total.
/// An estimate until M7 recalculates it and stores it with the order.
/// </summary>
public sealed record PriceBreakdown(
    string CurrencyCode,
    DateTimeOffset PricedAtUtc,
    IReadOnlyList<PricedDrink> Drinks,
    Money Subtotal,
    AppliedDiscount Discount,
    Money Taxable,
    decimal VatRate,
    Money Vat,
    Money Total);