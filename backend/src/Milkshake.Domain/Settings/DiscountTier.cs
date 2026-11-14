using Milkshake.Domain.Common;

namespace Milkshake.Domain.Settings;

/// <summary>
/// One frequent-customer discount tier (URS §3.4.5). The discount rate per tier is not in the URS
/// (open question Q-01), so tiers are seeded inactive until confirmed.
/// </summary>
public sealed class DiscountTier : IHasTimestamps
{
    /// <summary>Highest tier number defined by the URS.</summary>
    public const int MaxTierNumber = 3;

    private DiscountTier()
    {
    }

    /// <summary>Creates a tier.</summary>
    public DiscountTier(int tierNumber, int minPreviousOrders, int minDrinksPerOrder, decimal discountRate, bool isActive)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tierNumber, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tierNumber, MaxTierNumber);

        TierNumber = tierNumber;
        Update(minPreviousOrders, minDrinksPerOrder, discountRate);
        IsActive = isActive;
    }

    /// <summary>Database identifier.</summary>
    public int Id { get; private set; }

    /// <summary>1, 2 or 3.</summary>
    public int TierNumber { get; private set; }

    /// <summary>Paid orders a patron needs before this tier applies.</summary>
    public int MinPreviousOrders { get; private set; }

    /// <summary>Drinks an order needs to count towards this tier.</summary>
    public int MinDrinksPerOrder { get; private set; }

    /// <summary>Discount as a fraction, e.g. 0.05 = 5%.</summary>
    public decimal DiscountRate { get; private set; }

    /// <summary>Inactive tiers are ignored by the discount engine.</summary>
    public bool IsActive { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Changes the tier's thresholds and rate.</summary>
    public void Update(int minPreviousOrders, int minDrinksPerOrder, decimal discountRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minPreviousOrders);
        ArgumentOutOfRangeException.ThrowIfLessThan(minDrinksPerOrder, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(discountRate);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(discountRate, 1m);

        MinPreviousOrders = minPreviousOrders;
        MinDrinksPerOrder = minDrinksPerOrder;
        DiscountRate = discountRate;
    }

    /// <summary>Enables the tier.</summary>
    public void Activate() => IsActive = true;

    /// <summary>Disables the tier.</summary>
    public void Deactivate() => IsActive = false;
}