using Milkshake.Application.Abstractions;
using Milkshake.Application.Management;
using Milkshake.Domain.Common;
using Milkshake.Domain.Pricing;

namespace Milkshake.Application.Ordering;

/// <summary>
/// A patron's previous orders that count towards frequent-customer tiers, as their drink counts.
/// Which orders count is decided by the source (Q-03, decided 22 Nov 2026: paid orders that were not cancelled).
/// </summary>
public sealed record DiscountHistory(IReadOnlyList<int> PreviousOrderDrinkCounts)
{
    /// <summary>No previous orders.</summary>
    public static DiscountHistory Empty { get; } = new([]);
}

/// <summary>Where frequent-customer history comes from. Always server data, never the browser.</summary>
public interface IDiscountEligibilitySource
{
    /// <summary>The patron's qualifying history.</summary>
    Task<DiscountHistory> GetHistoryAsync(Guid patronId, CancellationToken cancellationToken);
}

/// <summary>The tier settings and the maximum discount, as configured by managers (M10).</summary>
public sealed record DiscountConfiguration(IReadOnlyList<DiscountTierRule> Tiers, Money MaxDiscount);

/// <summary>Reads the discount configuration on the server.</summary>
public interface IDiscountConfigurationSource
{
    /// <summary>Tiers 1 to 3 and the maximum discount in the pricing currency.</summary>
    Task<DiscountConfiguration> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Pure discount decision: the gate, then tier selection, then the cap (applied by the calculator).</summary>
public static class DiscountRules
{
    /// <summary>
    /// The discount to apply, or null. While <paramref name="rulesConfirmed"/> is false there is never a discount,
    /// whatever the configuration or history (M10 decision D9, M6 decision P5).
    /// </summary>
    public static DiscountTerms? Resolve(bool rulesConfirmed, DiscountConfiguration configuration, DiscountHistory history)
    {
        if (!rulesConfirmed)
        {
            return null;
        }

        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(history);

        DiscountTierRule? tier = DiscountTierSelector.Select(configuration.Tiers, history.PreviousOrderDrinkCounts);
        return tier is null ? null : new DiscountTerms(tier.TierNumber, tier.Rate, configuration.MaxDiscount);
    }
}

/// <summary>Decides the frequent-customer discount for the signed-in patron.</summary>
public interface IDiscountResolver
{
    /// <summary>The discount to apply now, or null for none.</summary>
    Task<DiscountTerms?> ResolveAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Default resolver. The activation gate is checked first: while the rules are unconfirmed, neither the configuration
/// nor any history is read, so a configuration problem cannot affect ordering and nothing is guessed.
/// </summary>
public sealed class DiscountResolver(
    ICurrentUser currentUser,
    IDiscountConfigurationSource configuration,
    IDiscountEligibilitySource eligibility) : IDiscountResolver
{
    /// <inheritdoc />
    public async Task<DiscountTerms?> ResolveAsync(CancellationToken cancellationToken)
    {
        if (!DiscountPolicy.RulesConfirmed || currentUser.UserId is not Guid patronId)
        {
            return null;
        }

        DiscountConfiguration config = await configuration.GetAsync(cancellationToken);
        DiscountHistory history = await eligibility.GetHistoryAsync(patronId, cancellationToken);
        return DiscountRules.Resolve(rulesConfirmed: true, config, history);
    }
}