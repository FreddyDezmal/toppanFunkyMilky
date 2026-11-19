using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Catalog;
using Milkshake.Domain.Common;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Infrastructure.Ordering;

/// <summary>
/// Read-only customer catalogue. Active items without a current price in the pricing currency are excluded and
/// logged (decision E1) – never offered at zero. Invalid settings make ordering unavailable instead of guessing.
/// </summary>
internal sealed partial class OrderingCatalogService(
    MilkshakeDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<OrderingCatalogService> logger) : IOrderingCatalogService
{
    // Flavour, Topping and Consistency all name their price history "Prices".
    private const string PricesNavigation = nameof(Flavour.Prices);

    public async Task<OrderingOptionsResult> GetOptionsAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, string> stored = await dbContext.SystemSettings.AsNoTracking()
            .ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.Ordinal, cancellationToken);

        OrderingSettingsResult read = OrderingSettingsReader.Read(stored);
        if (read.Settings is not OrderingSettings settings)
        {
            LogInvalidSettings(logger, string.Join(" ", read.Problems));
            return OrderingOptionsResult.Unavailable();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        IReadOnlyList<OrderingOption> flavours = await ActiveOptionsAsync(dbContext.Flavours, "Flavour", now, settings.CurrencyCode, cancellationToken);
        IReadOnlyList<OrderingOption> consistencies = await ActiveOptionsAsync(dbContext.Consistencies, "Consistency", now, settings.CurrencyCode, cancellationToken);
        IReadOnlyList<OrderingOption> toppings = await ActiveOptionsAsync(dbContext.Toppings, "Topping", now, settings.CurrencyCode, cancellationToken);

        List<OrderingRestaurant> restaurants = await dbContext.Restaurants.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.Name)
            .Select(r => new OrderingRestaurant(r.Id, r.Name, r.Address))
            .ToListAsync(cancellationToken);

        return new OrderingOptionsResult(new OrderingOptions(
            flavours, consistencies, toppings, restaurants, settings, PickupRules.WindowFor(settings, now)));
    }

    private async Task<IReadOnlyList<OrderingOption>> ActiveOptionsAsync<TItem>(
        DbSet<TItem> items,
        string kind,
        DateTimeOffset now,
        string currencyCode,
        CancellationToken cancellationToken)
        where TItem : CatalogItem
    {
        List<TItem> active = await items.AsNoTracking()
            .Include(PricesNavigation)
            .Where(item => item.IsActive)
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Name)
            .ToListAsync(cancellationToken);

        var options = new List<OrderingOption>(active.Count);
        foreach (TItem item in active)
        {
            Money? fee = item.PriceAt(now);
            if (fee is null || fee.CurrencyCode != currencyCode)
            {
                LogUnpricedItem(logger, kind, item.Id, item.Name, currencyCode);
                continue;
            }

            options.Add(new OrderingOption(item.Id, item.Name, fee.Amount, fee.CurrencyCode));
        }

        return options;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Kind} {ItemId} '{Name}' is active but has no current price in {CurrencyCode}; it is hidden from patrons.")]
    private static partial void LogUnpricedItem(ILogger logger, string kind, int itemId, string name, string currencyCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Ordering is unavailable because the settings are invalid: {Problems}")]
    private static partial void LogInvalidSettings(ILogger logger, string problems);
}