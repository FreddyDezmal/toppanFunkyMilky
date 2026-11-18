using System.ComponentModel.DataAnnotations;
using Milkshake.Application.Management;

namespace Milkshake.Api.Contracts.Management;

/// <summary>One price period. Times are UTC (ISO 8601); the UI shows them in the manager's local time.</summary>
public sealed record PricePeriodResponse(
    int Id,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string Status,
    bool CanCancel)
{
    /// <summary>Maps an application summary.</summary>
    public static PricePeriodResponse From(PricePeriodSummary period)
    {
        ArgumentNullException.ThrowIfNull(period);
        return new(period.Id, period.Amount, period.CurrencyCode, period.EffectiveFrom, period.EffectiveTo,
            period.Status.ToString(), period.CanCancel);
    }
}

/// <summary>An item and its price history, newest first. Use Item.Version for the next change.</summary>
public sealed record PriceHistoryResponse(CatalogItemResponse Item, IReadOnlyList<PricePeriodResponse> Prices)
{
    /// <summary>Maps an application summary.</summary>
    public static PriceHistoryResponse From(PriceHistorySummary history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return new(CatalogItemResponse.From(history.Item), history.Prices.Select(PricePeriodResponse.From).ToList());
    }
}

/// <summary>A new price for an item.</summary>
public sealed class SchedulePriceRequest
{
    /// <summary>Amount in the configured pricing currency; at most two decimal places.</summary>
    [Required(ErrorMessage = "Price is required.")]
    public decimal? Amount { get; init; }

    /// <summary>When the price starts. Omit (or null) for immediately; otherwise now or later.</summary>
    public DateTimeOffset? EffectiveFrom { get; init; }

    /// <summary>The item version returned when it was loaded.</summary>
    [Required(ErrorMessage = "Version is required. Reload and try again.")]
    public uint? Version { get; init; }
}