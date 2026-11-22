using Milkshake.Domain.Common;
using Milkshake.Domain.Pricing;

namespace Milkshake.UnitTests.Pricing;

/// <summary>
/// Hand-worked cases for the approved rules (P1–P4). Every expected amount was calculated by hand from the fees,
/// not by repeating the implementation's formula.
/// </summary>
public sealed class PriceCalculatorTests
{
    private const string Zar = "ZAR";
    private static readonly DateTimeOffset PricedAt = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    private static PriceComponent Fee(string name, decimal amount, string currency = Zar) => new(1, name, new Money(amount, currency));

    private static DrinkToPrice Drink(decimal flavour, decimal consistency, decimal topping) =>
        new(Fee("Flavour", flavour), Fee("Consistency", consistency), Fee("Topping", topping));

    private static PriceBreakdown Price(decimal vatRate, DiscountTerms? discount, params DrinkToPrice[] drinks) =>
        PriceCalculator.Calculate(new PricingInput(Zar, vatRate, drinks, discount, PricedAt));

    private static void AssertTotals(PriceBreakdown price, decimal subtotal, decimal discount, decimal taxable, decimal vat, decimal total)
    {
        Assert.Equal(
            (subtotal, discount, taxable, vat, total),
            (price.Subtotal.Amount, price.Discount.Amount.Amount, price.Taxable.Amount, price.Vat.Amount, price.Total.Amount));
        Assert.Equal(price.Total.Amount, price.Subtotal.Amount - price.Discount.Amount.Amount + price.Vat.Amount);
    }

    [Fact]
    public void OneDrink_IsTheSumOfItsFees_WithFifteenPercentVatAdded()
    {
        // Vanilla 30.00 + Thick 6.00 + Oreo crumbs 10.00 = 46.00; VAT 46.00 × 15% = 6.90; total 52.90.
        PriceBreakdown price = Price(0.15m, null, Drink(30.00m, 6.00m, 10.00m));

        Assert.Equal(46.00m, Assert.Single(price.Drinks).Price.Amount);
        AssertTotals(price, subtotal: 46.00m, discount: 0.00m, taxable: 46.00m, vat: 6.90m, total: 52.90m);
        Assert.Equal((Zar, 0.15m, PricedAt), (price.CurrencyCode, price.VatRate, price.PricedAtUtc));
    }

    [Fact]
    public void SeveralDrinks_AreNumberedAndPricedIndependently()
    {
        // 57.00 (38 + 10 + 9) and 40.00 (30 + 0 + 10) = 97.00; VAT 14.55; total 111.55.
        PriceBreakdown price = Price(0.15m, null, Drink(38.00m, 10.00m, 9.00m), Drink(30.00m, 0.00m, 10.00m));

        Assert.Equal([(1, 57.00m), (2, 40.00m)], price.Drinks.Select(d => (d.Number, d.Price.Amount)));
        AssertTotals(price, subtotal: 97.00m, discount: 0.00m, taxable: 97.00m, vat: 14.55m, total: 111.55m);
    }

    [Fact]
    public void ZeroVat_AddsNothing()
    {
        AssertTotals(Price(0m, null, Drink(30.00m, 6.00m, 10.00m)), subtotal: 46.00m, discount: 0.00m, taxable: 46.00m, vat: 0.00m, total: 46.00m);
    }

    [Fact]
    public void AChangedVatRate_IsUsedAsGiven()
    {
        // 46.00 × 16% = 7.36; total 53.36.
        AssertTotals(Price(0.16m, null, Drink(30.00m, 6.00m, 10.00m)), subtotal: 46.00m, discount: 0.00m, taxable: 46.00m, vat: 7.36m, total: 53.36m);
    }

    [Fact]
    public void VatOnAMidpoint_RoundsUp()
    {
        // 30.10 + 6.00 + 10.00 = 46.10; 46.10 × 15% = 6.915 → 6.92; total 53.02.
        AssertTotals(Price(0.15m, null, Drink(30.10m, 6.00m, 10.00m)), subtotal: 46.10m, discount: 0.00m, taxable: 46.10m, vat: 6.92m, total: 53.02m);
    }

    [Fact]
    public void Vat_IsRoundedOnceForTheOrder_NotPerDrink()
    {
        // Two drinks of 46.10: 92.20 × 15% = 13.83 exactly. Per drink it would be 6.92 + 6.92 = 13.84.
        PriceBreakdown price = Price(0.15m, null, Drink(30.10m, 6.00m, 10.00m), Drink(30.10m, 6.00m, 10.00m));

        AssertTotals(price, subtotal: 92.20m, discount: 0.00m, taxable: 92.20m, vat: 13.83m, total: 106.03m);
    }

    [Fact]
    public void AZeroFee_IsAllowed()
    {
        // Vanilla 30.00 + Milky 0.00 + Oreo crumbs 10.00 = 40.00; VAT 6.00; total 46.00.
        AssertTotals(Price(0.15m, null, Drink(30.00m, 0.00m, 10.00m)), subtotal: 40.00m, discount: 0.00m, taxable: 40.00m, vat: 6.00m, total: 46.00m);
    }

    [Fact]
    public void AllFeesZero_GivesAZeroTotal()
    {
        AssertTotals(Price(0.15m, null, Drink(0m, 0m, 0m)), subtotal: 0.00m, discount: 0.00m, taxable: 0.00m, vat: 0.00m, total: 0.00m);
    }

    [Fact]
    public void LargeOrders_KeepEveryCent()
    {
        // Ten drinks of 999.99 × 3 = 2 999.97 → 29 999.70; VAT 4 499.955 → 4 499.96; total 34 499.66.
        DrinkToPrice[] ten = Enumerable.Repeat(Drink(999.99m, 999.99m, 999.99m), 10).ToArray();

        AssertTotals(Price(0.15m, null, ten), subtotal: 29999.70m, discount: 0.00m, taxable: 29999.70m, vat: 4499.96m, total: 34499.66m);
    }

    [Fact]
    public void WithoutDiscountTerms_TheDiscountIsZeroAndNamesNoTier()
    {
        AppliedDiscount discount = Price(0.15m, null, Drink(30.00m, 6.00m, 10.00m)).Discount;

        Assert.Equal((0.00m, (int?)null, (decimal?)null, false), (discount.Amount.Amount, discount.TierNumber, discount.Rate, discount.CapApplied));
    }

    // Discount maths (Q-04, decided 22 Nov 2026): taken from the whole order before VAT, capped per order (Q-02).

    [Fact]
    public void ADiscount_IsTakenBeforeVat()
    {
        // 114.00 − 10% (11.40) = 102.60; VAT 15.39; total 117.99.
        PriceBreakdown price = Price(0.15m, new DiscountTerms(2, 0.10m, new Money(50.00m, Zar)), Drink(38.00m, 10.00m, 9.00m), Drink(38.00m, 10.00m, 9.00m));

        AssertTotals(price, subtotal: 114.00m, discount: 11.40m, taxable: 102.60m, vat: 15.39m, total: 117.99m);
        Assert.Equal((2, 0.10m, false), (price.Discount.TierNumber, price.Discount.Rate, price.Discount.CapApplied));
    }

    [Fact]
    public void ADiscount_NeverExceedsTheCap()
    {
        // 400.00 × 15% = 60.00, capped at 50.00; taxable 350.00; VAT 52.50; total 402.50.
        DrinkToPrice[] eight = Enumerable.Repeat(Drink(30.00m, 10.00m, 10.00m), 8).ToArray();

        PriceBreakdown price = Price(0.15m, new DiscountTerms(3, 0.15m, new Money(50.00m, Zar)), eight);

        AssertTotals(price, subtotal: 400.00m, discount: 50.00m, taxable: 350.00m, vat: 52.50m, total: 402.50m);
        Assert.True(price.Discount.CapApplied);
    }

    [Fact]
    public void ADiscountOnAMidpoint_RoundsUp_AndVatIsTakenOnTheRest()
    {
        // 46.10 × 5% = 2.305 → 2.31; taxable 43.79; VAT 6.5685 → 6.57; total 50.36.
        PriceBreakdown price = Price(0.15m, new DiscountTerms(1, 0.05m, new Money(50.00m, Zar)), Drink(30.10m, 6.00m, 10.00m));

        AssertTotals(price, subtotal: 46.10m, discount: 2.31m, taxable: 43.79m, vat: 6.57m, total: 50.36m);
    }

    [Fact]
    public void ADiscount_NeverMakesTheTotalNegative()
    {
        PriceBreakdown price = Price(0.15m, new DiscountTerms(3, 1.00m, new Money(1000.00m, Zar)), Drink(30.00m, 6.00m, 10.00m));

        AssertTotals(price, subtotal: 46.00m, discount: 46.00m, taxable: 0.00m, vat: 0.00m, total: 0.00m);
    }

    [Fact]
    public void AFeeInAnotherCurrency_IsRefused()
    {
        var drink = new DrinkToPrice(Fee("Vanilla", 30.00m), Fee("Thick", 6.00m, "USD"), Fee("Oreo crumbs", 10.00m));

        Assert.Throws<PricingConfigurationException>(() => Price(0.15m, null, drink));
    }

    [Fact]
    public void ACapInAnotherCurrency_IsRefused()
    {
        Assert.Throws<PricingConfigurationException>(
            () => Price(0.15m, new DiscountTerms(1, 0.05m, new Money(50.00m, "USD")), Drink(30.00m, 6.00m, 10.00m)));
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1.01")]
    public void AVatRateOutsideZeroToOne_IsRefused(string vatRate)
    {
        decimal rate = decimal.Parse(vatRate, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Throws<PricingConfigurationException>(() => Price(rate, null, Drink(30.00m, 6.00m, 10.00m)));
    }

    [Fact]
    public void ADiscountRateAboveOne_IsRefused()
    {
        Assert.Throws<PricingConfigurationException>(
            () => Price(0.15m, new DiscountTerms(1, 1.5m, new Money(50.00m, Zar)), Drink(30.00m, 6.00m, 10.00m)));
    }

    [Fact]
    public void AnOrderWithoutDrinks_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => Price(0.15m, null));
    }
}