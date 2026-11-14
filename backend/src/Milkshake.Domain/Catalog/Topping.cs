using Milkshake.Domain.Common;

namespace Milkshake.Domain.Catalog;

/// <summary>A milkshake topping (URS §3.4), e.g. Oreo crumbs.</summary>
public sealed class Topping : CatalogItem
{
    private readonly List<ToppingPrice> _prices = [];

    private Topping()
    {
    }

    /// <summary>Creates an active topping with no price yet.</summary>
    public Topping(string name, int displayOrder)
        : base(name, displayOrder)
    {
    }

    /// <summary>Full price history.</summary>
    public IReadOnlyCollection<ToppingPrice> Prices => _prices;

    /// <summary>Schedules a new price; the current price ends when it starts.</summary>
    public ToppingPrice SetPrice(Money price, DateTimeOffset effectiveFrom) =>
        PriceSchedule.Add(_prices, new ToppingPrice(this, price, effectiveFrom));

    /// <summary>The price in force at a moment, or null if none.</summary>
    public Money? PriceAt(DateTimeOffset moment) => PriceSchedule.PriceAt(_prices, moment);
}

/// <summary>One period of a topping's price.</summary>
public sealed class ToppingPrice : PricePeriod
{
    private ToppingPrice()
    {
    }

    internal ToppingPrice(Topping topping, Money price, DateTimeOffset effectiveFrom)
        : base(price, effectiveFrom)
    {
        Topping = topping;
    }

    /// <summary>Owning topping id.</summary>
    public int ToppingId { get; private set; }

    /// <summary>Owning topping.</summary>
    public Topping Topping { get; private set; } = null!;
}