namespace Milkshake.Application.Management;

/// <summary>
/// Whether frequent-customer discounts may be switched on. False until stakeholders answer Q-01 to Q-04
/// (rate per tier, meaning of the maximum, which orders count, before or after VAT) and the discount engine (M6)
/// implements the answers. Deliberately code, not configuration, so it cannot be switched on early by accident.
/// </summary>
public static class DiscountPolicy
{
    /// <summary>Whether tiers may be activated.</summary>
    public static bool RulesConfirmed { get; } = false;

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