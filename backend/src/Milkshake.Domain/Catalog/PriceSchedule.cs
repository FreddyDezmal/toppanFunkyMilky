using Milkshake.Domain.Common;

namespace Milkshake.Domain.Catalog;

/// <summary>
/// Rules shared by every item's price history. Callers must load the existing prices first.
/// Periods that have taken effect are never edited: the latest period only gets an end time.
/// </summary>
internal static class PriceSchedule
{
    /// <summary>Closes the latest price at the new price's start and appends the new price.</summary>
    public static TPrice Add<TPrice>(List<TPrice> prices, TPrice newPrice)
        where TPrice : PricePeriod
    {
        TPrice? latest = prices.SingleOrDefault(price => price.EffectiveTo is null);
        if (latest is not null)
        {
            if (newPrice.EffectiveFrom <= latest.EffectiveFrom)
            {
                throw new BusinessRuleException(PricingMessages.MustStartAfterLatest);
            }

            latest.Close(newPrice.EffectiveFrom);
        }

        prices.Add(newPrice);
        return newPrice;
    }

    /// <summary>
    /// Removes the latest price if it has not started yet and reopens the price before it.
    /// Returns the removed period so the caller can delete it from storage.
    /// </summary>
    public static TPrice CancelLatestScheduled<TPrice>(List<TPrice> prices, DateTimeOffset now)
        where TPrice : PricePeriod
    {
        TPrice? latest = prices.SingleOrDefault(price => price.EffectiveTo is null);
        if (latest is null || latest.EffectiveFrom <= now)
        {
            throw new BusinessRuleException(PricingMessages.NothingScheduled);
        }

        TPrice previous = prices.SingleOrDefault(price => price.EffectiveTo == latest.EffectiveFrom)
            ?? throw new BusinessRuleException(PricingMessages.CannotCancelOnlyPrice);

        prices.Remove(latest);
        previous.Reopen();
        return latest;
    }

    /// <summary>The price in force at a moment, or null if none applies.</summary>
    public static Money? PriceAt<TPrice>(IEnumerable<TPrice> prices, DateTimeOffset moment)
        where TPrice : PricePeriod =>
        prices.SingleOrDefault(price => price.IsEffectiveAt(moment))?.Price;

    /// <summary>All periods, newest first.</summary>
    public static IReadOnlyList<PricePeriod> History<TPrice>(IEnumerable<TPrice> prices)
        where TPrice : PricePeriod =>
        prices.OrderByDescending(price => price.EffectiveFrom).ToList<PricePeriod>();
}