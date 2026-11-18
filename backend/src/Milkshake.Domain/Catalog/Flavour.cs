using Milkshake.Domain.Common;

namespace Milkshake.Domain.Catalog;

/// <summary>A milkshake flavour (URS §3.4), e.g. Strawberry or Bar One.</summary>
public sealed class Flavour : CatalogItem
{
    private readonly List<FlavourPrice> _prices = [];

    private Flavour()
    {
    }

    /// <summary>Creates an active flavour with no price yet.</summary>
    public Flavour(string name, int displayOrder)
        : base(name, displayOrder)
    {
    }

    /// <summary>Full price history.</summary>
    public IReadOnlyCollection<FlavourPrice> Prices => _prices;

    /// <inheritdoc />
    public override FlavourPrice SetPrice(Money price, DateTimeOffset effectiveFrom) =>
        PriceSchedule.Add(_prices, new FlavourPrice(this, price, effectiveFrom));

    /// <inheritdoc />
    public override Money? PriceAt(DateTimeOffset moment) => PriceSchedule.PriceAt(_prices, moment);

    /// <inheritdoc />
    public override FlavourPrice CancelLatestScheduledPrice(DateTimeOffset now) =>
        PriceSchedule.CancelLatestScheduled(_prices, now);

    /// <inheritdoc />
    public override IReadOnlyList<PricePeriod> GetPriceHistory() => PriceSchedule.History(_prices);
}

/// <summary>One period of a flavour's price.</summary>
public sealed class FlavourPrice : PricePeriod
{
    private FlavourPrice()
    {
    }

    internal FlavourPrice(Flavour flavour, Money price, DateTimeOffset effectiveFrom)
        : base(price, effectiveFrom)
    {
        Flavour = flavour;
    }

    /// <summary>Owning flavour id.</summary>
    public int FlavourId { get; private set; }

    /// <summary>Owning flavour.</summary>
    public Flavour Flavour { get; private set; } = null!;
}