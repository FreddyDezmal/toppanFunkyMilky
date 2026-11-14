using Milkshake.Domain.Common;

namespace Milkshake.Domain.Catalog;

/// <summary>
/// Rules shared by every item's price history. Callers must load the existing prices first.
/// </summary>
internal static class PriceSchedule
{
    /// <summary>Closes the current price at the new price's start and appends the new price.</summary>
    public static TPrice Add<TPrice>(List<TPrice> prices, TPrice newPrice)
        where TPrice : PricePeriod
    {
        TPrice? current = prices.SingleOrDefault(price => price.EffectiveTo is null);
        if (current is not null)
        {
            if (newPrice.EffectiveFrom <= current.EffectiveFrom)
            {
                throw new InvalidOperationException("A new price must start after the current price's start date.");
            }

            current.Close(newPrice.EffectiveFrom);
        }

        prices.Add(newPrice);
        return newPrice;
    }

    /// <summary>The price in force at a moment, or null if none applies.</summary>
    public static Money? PriceAt<TPrice>(IEnumerable<TPrice> prices, DateTimeOffset moment)
        where TPrice : PricePeriod =>
        prices.SingleOrDefault(price => price.IsEffectiveAt(moment))?.Price;
}