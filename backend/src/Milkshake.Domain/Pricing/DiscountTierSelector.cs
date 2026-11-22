namespace Milkshake.Domain.Pricing;

/// <summary>A frequent-customer tier as the discount rules see it (from the M10 discount-tier settings).</summary>
public sealed record DiscountTierRule(int TierNumber, int MinPreviousOrders, int MinDrinksPerOrder, decimal Rate, bool IsActive);

/// <summary>
/// Tier selection (roadmap AC-6.3; Q-03 decided 22 Nov 2026: only paid, not-cancelled orders are passed in). A previous order qualifies for a tier when it had at
/// least that tier's <c>MinDrinksPerOrder</c> drinks; a patron reaches the tier with at least <c>MinPreviousOrders</c>
/// qualifying orders. Of the active tiers reached, the highest tier number wins; tiers never combine.
/// Applies live once a manager activates a tier (the code gate, DiscountPolicy.RulesConfirmed, is open).
/// </summary>
public static class DiscountTierSelector
{
    /// <summary>The tier that applies, or null when none does.</summary>
    public static DiscountTierRule? Select(IEnumerable<DiscountTierRule> tiers, IReadOnlyList<int> previousOrderDrinkCounts)
    {
        ArgumentNullException.ThrowIfNull(tiers);
        ArgumentNullException.ThrowIfNull(previousOrderDrinkCounts);

        return tiers
            .Where(tier => tier.IsActive && previousOrderDrinkCounts.Count(drinks => drinks >= tier.MinDrinksPerOrder) >= tier.MinPreviousOrders)
            .OrderByDescending(tier => tier.TierNumber)
            .FirstOrDefault();
    }
}