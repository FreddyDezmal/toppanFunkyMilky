using Milkshake.Domain.Pricing;

namespace Milkshake.UnitTests.Pricing;

/// <summary>Provisional tier selection (Q-03) with FICTIONAL rates; not used live (decision P5).</summary>
public sealed class DiscountTierSelectorTests
{
    private static readonly DiscountTierRule Tier1 = new(1, MinPreviousOrders: 5, MinDrinksPerOrder: 2, Rate: 0.05m, IsActive: true);
    private static readonly DiscountTierRule Tier2 = new(2, MinPreviousOrders: 10, MinDrinksPerOrder: 3, Rate: 0.10m, IsActive: true);
    private static readonly DiscountTierRule Tier3 = new(3, MinPreviousOrders: 20, MinDrinksPerOrder: 4, Rate: 0.15m, IsActive: true);
    private static readonly DiscountTierRule[] AllTiers = [Tier1, Tier2, Tier3];

    private static int[] Orders(int count, int drinksEach) => Enumerable.Repeat(drinksEach, count).ToArray();

    [Fact]
    public void NoHistory_ReachesNoTier()
    {
        Assert.Null(DiscountTierSelector.Select(AllTiers, []));
    }

    [Fact]
    public void ExactlyTheThreshold_ReachesTheTier()
    {
        Assert.Equal(Tier1, DiscountTierSelector.Select(AllTiers, Orders(5, drinksEach: 2)));
    }

    [Fact]
    public void OneOrderShort_ReachesNoTier()
    {
        Assert.Null(DiscountTierSelector.Select(AllTiers, Orders(4, drinksEach: 2)));
    }

    [Fact]
    public void OrdersWithTooFewDrinks_DoNotCount()
    {
        // Ten orders of 2 drinks reach tier 1, but count for nothing towards tier 2 (3 drinks per order).
        int[] history = [.. Orders(10, drinksEach: 2), .. Orders(3, drinksEach: 1)];

        Assert.Equal(Tier1, DiscountTierSelector.Select(AllTiers, history));
    }

    [Fact]
    public void TheHighestTierReached_Wins_AndTiersDoNotCombine()
    {
        Assert.Equal(Tier2, DiscountTierSelector.Select(AllTiers, Orders(10, drinksEach: 3)));
    }

    [Fact]
    public void AnInactiveTier_IsIgnored()
    {
        DiscountTierRule[] tiers = [Tier1, Tier2, Tier3 with { IsActive = false }];

        Assert.Equal(Tier2, DiscountTierSelector.Select(tiers, Orders(20, drinksEach: 4)));
    }
}