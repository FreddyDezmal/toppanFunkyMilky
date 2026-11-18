using Milkshake.Domain.Catalog;
using Milkshake.Domain.Common;

namespace Milkshake.UnitTests.Catalog;

public sealed class PriceHistoryTests
{
    private static readonly DateTimeOffset January = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset February = new(2026, 2, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset March = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SetPrice_ClosesCurrentPriceWhenNewPriceStarts()
    {
        var flavour = new Flavour("Vanilla", displayOrder: 1);

        FlavourPrice first = flavour.SetPrice(new Money(30.00m, "ZAR"), January);
        flavour.SetPrice(new Money(35.00m, "ZAR"), March);

        Assert.Equal(March, first.EffectiveTo);
        Assert.Equal(30.00m, flavour.PriceAt(February)?.Amount);
        Assert.Equal(35.00m, flavour.PriceAt(March)?.Amount);
    }

    [Fact]
    public void SetPrice_RejectsStartNotAfterCurrentPrice()
    {
        var flavour = new Flavour("Vanilla", displayOrder: 1);
        flavour.SetPrice(new Money(30.00m, "ZAR"), March);

        Assert.Throws<BusinessRuleException>(() => flavour.SetPrice(new Money(25.00m, "ZAR"), January));
    }

    [Fact]
    public void PriceAt_ReturnsNullBeforeFirstPrice()
    {
        var flavour = new Flavour("Vanilla", displayOrder: 1);
        flavour.SetPrice(new Money(30.00m, "ZAR"), March);

        Assert.Null(flavour.PriceAt(January));
    }

    [Fact]
    public void Money_RejectsNegativeAmount() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Money(-0.01m, "ZAR"));

    [Fact]
    public void Money_RejectsMoreThanTwoDecimals() =>
        Assert.Throws<ArgumentException>(() => _ = new Money(10.005m, "ZAR"));

    [Fact]
    public void Money_RejectsInvalidCurrencyCode() =>
        Assert.Throws<ArgumentException>(() => _ = new Money(10.00m, "RANDS"));

    [Fact]
    public void Money_NormalisesCurrencyCodeToUpperCase() =>
        Assert.Equal("ZAR", new Money(10.00m, "zar").CurrencyCode);
}