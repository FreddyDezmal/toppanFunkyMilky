using Milkshake.Domain.Common;

namespace Milkshake.Domain.Catalog;

/// <summary>A milkshake consistency, "Thick or not" in URS §3.4, e.g. Double thick.</summary>
public sealed class Consistency : CatalogItem
{
    private readonly List<ConsistencyPrice> _prices = [];

    private Consistency()
    {
    }

    /// <summary>Creates an active consistency with no price yet.</summary>
    public Consistency(string name, int displayOrder)
        : base(name, displayOrder)
    {
    }

    /// <summary>Full price history.</summary>
    public IReadOnlyCollection<ConsistencyPrice> Prices => _prices;

    /// <inheritdoc />
    public override ConsistencyPrice SetPrice(Money price, DateTimeOffset effectiveFrom) =>
        PriceSchedule.Add(_prices, new ConsistencyPrice(this, price, effectiveFrom));

    /// <inheritdoc />
    public override Money? PriceAt(DateTimeOffset moment) => PriceSchedule.PriceAt(_prices, moment);

    /// <inheritdoc />
    public override ConsistencyPrice CancelLatestScheduledPrice(DateTimeOffset now) =>
        PriceSchedule.CancelLatestScheduled(_prices, now);

    /// <inheritdoc />
    public override IReadOnlyList<PricePeriod> GetPriceHistory() => PriceSchedule.History(_prices);
}

/// <summary>One period of a consistency's price.</summary>
public sealed class ConsistencyPrice : PricePeriod
{
    private ConsistencyPrice()
    {
    }

    internal ConsistencyPrice(Consistency consistency, Money price, DateTimeOffset effectiveFrom)
        : base(price, effectiveFrom)
    {
        Consistency = consistency;
    }

    /// <summary>Owning consistency id.</summary>
    public int ConsistencyId { get; private set; }

    /// <summary>Owning consistency.</summary>
    public Consistency Consistency { get; private set; } = null!;
}