namespace Milkshake.Application.Management;

/// <summary>Where a price period stands relative to now.</summary>
public enum PriceStatus
{
    /// <summary>Ended.</summary>
    Past,

    /// <summary>In force now.</summary>
    Current,

    /// <summary>Starts in the future.</summary>
    Scheduled,
}

/// <summary>Effective-dated prices of flavours, toppings and consistencies.</summary>
public interface ICatalogPricingService
{
    /// <summary>The item and its full price history (newest first), or null if the item does not exist.</summary>
    Task<PriceHistorySummary?> GetHistoryAsync(CatalogKind kind, int id, CancellationToken cancellationToken);

    /// <summary>Schedules a price from now (EffectiveFrom null) or a future moment. Back-dating is refused.</summary>
    Task<ManagementResult<PriceHistorySummary>> ScheduleAsync(CatalogKind kind, int id, SchedulePriceCommand command, CancellationToken cancellationToken);

    /// <summary>Cancels the latest price if it has not started; the previous price continues.</summary>
    Task<ManagementResult<PriceHistorySummary>> CancelScheduledAsync(CatalogKind kind, int id, int priceId, uint version, CancellationToken cancellationToken);
}

/// <summary>A new price, based on the item version the manager saw.</summary>
public sealed record SchedulePriceCommand(decimal Amount, DateTimeOffset? EffectiveFrom, uint Version);

/// <summary>One price period.</summary>
public sealed record PricePeriodSummary(
    int Id,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    PriceStatus Status,
    bool CanCancel);

/// <summary>An item and its price history, newest first.</summary>
public sealed record PriceHistorySummary(CatalogItemSummary Item, IReadOnlyList<PricePeriodSummary> Prices);