using Milkshake.Domain.Common;
using Milkshake.Domain.Pricing;

namespace Milkshake.UnitTests.Pricing;

/// <summary>
/// Roadmap AC-6.1: every flavour × consistency × topping combination (7 × 4 × 6 = 168) at 15% VAT, using the
/// development seed fees. Expected values come from an independent oracle: whole cents in integer arithmetic,
/// with VAT rounded half up as (cents × 15 + 50) / 100, so a mistake in the calculator cannot hide in the test.
/// </summary>
public sealed class SeededCombinationTests
{
    private static readonly Dictionary<string, int> FlavourCents = new()
    {
        ["Strawberry"] = 3200, ["Vanilla"] = 3000, ["Chocolate"] = 3200, ["Coffee"] = 3400,
        ["Banana"] = 3200, ["Oreo"] = 3600, ["Bar one"] = 3800,
    };

    private static readonly Dictionary<string, int> ConsistencyCents = new()
    {
        ["Double thick"] = 1000, ["Thick"] = 600, ["Milky"] = 0, ["Icy"] = 400,
    };

    private static readonly Dictionary<string, int> ToppingCents = new()
    {
        ["Frozen Strawberries"] = 1200, ["Freeze-dried banana"] = 1200, ["Oreo crumbs"] = 1000,
        ["Bar one syrup"] = 1000, ["Coffee powder with chocolate"] = 900, ["Chocolate vermicelli"] = 800,
    };

    public static TheoryData<string, string, string> Combinations()
    {
        var data = new TheoryData<string, string, string>();
        foreach (string flavour in FlavourCents.Keys)
        {
            foreach (string consistency in ConsistencyCents.Keys)
            {
                foreach (string topping in ToppingCents.Keys)
                {
                    data.Add(flavour, consistency, topping);
                }
            }
        }

        return data;
    }

    [Fact]
    public void ThereAre168Combinations()
    {
        Assert.Equal(168, Combinations().Count());
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void EveryCombination_MatchesTheIndependentOracle(string flavour, string consistency, string topping)
    {
        int drinkCents = FlavourCents[flavour] + ConsistencyCents[consistency] + ToppingCents[topping];
        int vatCents = ((drinkCents * 15) + 50) / 100;
        var drink = new DrinkToPrice(
            Component(flavour, FlavourCents[flavour]),
            Component(consistency, ConsistencyCents[consistency]),
            Component(topping, ToppingCents[topping]));

        PriceBreakdown price = PriceCalculator.Calculate(
            new PricingInput("ZAR", 0.15m, [drink], null, DateTimeOffset.UnixEpoch));

        Assert.Equal(drinkCents / 100m, Assert.Single(price.Drinks).Price.Amount);
        Assert.Equal(vatCents / 100m, price.Vat.Amount);
        Assert.Equal((drinkCents + vatCents) / 100m, price.Total.Amount);
    }

    private static PriceComponent Component(string name, int cents) => new(cents, name, new Money(cents / 100m, "ZAR"));
}