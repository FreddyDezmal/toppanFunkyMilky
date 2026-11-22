namespace Milkshake.Application.Management;

/// <summary>
/// Whether frequent-customer discounts may be switched on. Opened on 22 Nov 2026 when the project owner decided
/// Q-01 to Q-04 for the demonstration (to be confirmed by the stakeholder): rates 5 / 10 / 15 %, the maximum discount
/// is a currency cap per order, only paid orders that were not cancelled count, and the discount comes off the whole
/// order before VAT. Deliberately code, not configuration, so it cannot be switched on or off by accident; tiers are
/// still inactive until a manager activates them.
/// </summary>
public static class DiscountPolicy
{
    /// <summary>Whether tiers may be activated.</summary>
    public static bool RulesConfirmed { get; } = true;

    /// <summary>Shown when activation is refused.</summary>
    public const string ActivationBlockedMessage =
        "Discount tiers cannot be activated until the discount rules are confirmed (open questions Q-01 to Q-04).";
}

/// <summary>Manager maintenance of the three frequent-customer discount tiers (URS §3.4.5).</summary>
public interface IDiscountTierManagementService
{
    /// <summary>Tiers 1 to 3.</summary>
    Task<IReadOnlyList<DiscountTierSummary>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Changes a tier's thresholds and rate.</summary>
    Task<ManagementResult<DiscountTierSummary>> UpdateAsync(int tierNumber, UpdateDiscountTierCommand command, CancellationToken cancellationToken);

    /// <summary>Activates a tier. Refused while <see cref="DiscountPolicy.RulesConfirmed"/> is false.</summary>
    Task<ManagementResult<DiscountTierSummary>> ActivateAsync(int tierNumber, uint version, CancellationToken cancellationToken);

    /// <summary>Deactivates a tier: from now on it gives no discount (orders already placed keep theirs).</summary>
    Task<ManagementResult<DiscountTierSummary>> DeactivateAsync(int tierNumber, uint version, CancellationToken cancellationToken);
}

/// <summary>New thresholds and rate for a tier, based on the version the manager saw.</summary>
public sealed record UpdateDiscountTierCommand(int MinPreviousOrders, int MinDrinksPerOrder, decimal DiscountRate, uint Version);

/// <summary>A discount tier as managers see it.</summary>
public sealed record DiscountTierSummary(
    int TierNumber,
    int MinPreviousOrders,
    int MinDrinksPerOrder,
    decimal DiscountRate,
    bool IsActive,
    bool CanActivate,
    string? ActivationBlockedReason,
    uint Version);