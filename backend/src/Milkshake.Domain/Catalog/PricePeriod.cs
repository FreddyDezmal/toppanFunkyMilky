using Milkshake.Domain.Common;

namespace Milkshake.Domain.Catalog;

/// <summary>
/// One price valid from <see cref="EffectiveFrom"/> (inclusive) until <see cref="EffectiveTo"/> (exclusive).
/// An open-ended period (EffectiveTo = null) is the current price.
/// </summary>
public abstract class PricePeriod : IHasTimestamps
{
    /// <summary>For EF Core.</summary>
    protected PricePeriod()
    {
        Price = null!;
    }

    /// <summary>Creates an open-ended price period.</summary>
    protected PricePeriod(Money price, DateTimeOffset effectiveFrom)
    {
        ArgumentNullException.ThrowIfNull(price);
        Price = price;
        EffectiveFrom = effectiveFrom;
    }

    /// <summary>Database identifier.</summary>
    public int Id { get; private set; }

    /// <summary>Amount and currency.</summary>
    public Money Price { get; private set; }

    /// <summary>Start of validity (inclusive, UTC).</summary>
    public DateTimeOffset EffectiveFrom { get; private set; }

    /// <summary>End of validity (exclusive, UTC); null while this is the current price.</summary>
    public DateTimeOffset? EffectiveTo { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>True if this price applies at the given moment.</summary>
    public bool IsEffectiveAt(DateTimeOffset moment) =>
        EffectiveFrom <= moment && (EffectiveTo is null || moment < EffectiveTo);

    /// <summary>Ends this period when the next price starts.</summary>
    internal void Close(DateTimeOffset effectiveTo)
    {
        if (EffectiveTo is not null)
        {
            throw new InvalidOperationException("This price period is already closed.");
        }

        if (effectiveTo <= EffectiveFrom)
        {
            throw new InvalidOperationException("A price period must end after it starts.");
        }

        EffectiveTo = effectiveTo;
    }
}