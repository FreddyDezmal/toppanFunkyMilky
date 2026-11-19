using System.Globalization;
using Milkshake.Domain.Common;

namespace Milkshake.Domain.Pricing;

/// <summary>
/// Server-side order pricing (M6). Pure: no clock, database or HTTP. Approved rules:
/// <list type="bullet">
/// <item>P2 – drink price = flavour fee + consistency fee + topping fee; no base price; zero fees allowed.</item>
/// <item>P1 – fees are VAT-exclusive; VAT is added.</item>
/// <item>P4 – any discount is taken from the whole order's subtotal before VAT.</item>
/// <item>P3 – VAT and discount are each rounded once, at order level, to cents, half away from zero.</item>
/// </list>
/// total = (subtotal − discount) + VAT, so the parts always add up to the total.
/// </summary>
public static class PriceCalculator
{
    /// <summary>The price of one milkshake: the sum of its three fees (exact; fees are whole cents).</summary>
    public static Money PriceDrink(DrinkToPrice drink, string currencyCode)
    {
        ArgumentNullException.ThrowIfNull(drink);
        string currency = NormaliseCurrency(currencyCode);

        decimal sum = 0m;
        foreach (PriceComponent component in new[] { drink.Flavour, drink.Consistency, drink.Topping })
        {
            ArgumentNullException.ThrowIfNull(component);
            RequireCurrency(component.Fee, currency, component.Name);
            sum += component.Fee.Amount;
        }

        return new Money(sum, currency);
    }

    /// <summary>Prices a whole order.</summary>
    public static PriceBreakdown Calculate(PricingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Drinks);
        if (input.Drinks.Count == 0)
        {
            throw new ArgumentException("An order needs at least one drink to be priced.", nameof(input));
        }

        if (input.VatRate is < 0m or > 1m)
        {
            throw new PricingConfigurationException(
                string.Create(CultureInfo.InvariantCulture, $"VAT rate {input.VatRate} is outside 0 to 1."));
        }

        string currency = NormaliseCurrency(input.CurrencyCode);

        var drinks = new List<PricedDrink>(input.Drinks.Count);
        decimal subtotal = 0m;
        for (int index = 0; index < input.Drinks.Count; index++)
        {
            DrinkToPrice drink = input.Drinks[index];
            Money price = PriceDrink(drink, currency);
            subtotal += price.Amount;
            drinks.Add(new PricedDrink(index + 1, drink.Flavour, drink.Consistency, drink.Topping, price));
        }

        AppliedDiscount discount = ApplyDiscount(input.Discount, subtotal, currency);
        decimal taxable = subtotal - discount.Amount.Amount;
        decimal vat = PriceRounding.ToCents(taxable * input.VatRate);

        return new PriceBreakdown(
            currency,
            input.PricedAtUtc,
            drinks,
            new Money(subtotal, currency),
            discount,
            new Money(taxable, currency),
            input.VatRate,
            new Money(vat, currency),
            new Money(taxable + vat, currency));
    }

    private static AppliedDiscount ApplyDiscount(DiscountTerms? terms, decimal subtotal, string currency)
    {
        if (terms is null)
        {
            return AppliedDiscount.None(currency);
        }

        if (terms.Rate is < 0m or > 1m)
        {
            throw new PricingConfigurationException(
                string.Create(CultureInfo.InvariantCulture, $"Discount rate {terms.Rate} for tier {terms.TierNumber} is outside 0 to 1."));
        }

        ArgumentNullException.ThrowIfNull(terms.Cap);
        RequireCurrency(terms.Cap, currency, "the maximum discount");

        decimal byRate = PriceRounding.ToCents(subtotal * terms.Rate);
        decimal amount = Math.Min(Math.Min(byRate, terms.Cap.Amount), subtotal);   // never more than the cap or the order
        return new AppliedDiscount(new Money(amount, currency), terms.TierNumber, terms.Rate, CapApplied: amount < byRate);
    }

    private static string NormaliseCurrency(string currencyCode) => new Money(0m, currencyCode).CurrencyCode;

    private static void RequireCurrency(Money amount, string currency, string what)
    {
        ArgumentNullException.ThrowIfNull(amount);
        if (!string.Equals(amount.CurrencyCode, currency, StringComparison.Ordinal))
        {
            throw new PricingConfigurationException(
                $"The price of {what} is in {amount.CurrencyCode}, but orders are priced in {currency}.");
        }
    }
}