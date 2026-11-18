using Microsoft.EntityFrameworkCore;
using Milkshake.Application.Management;
using Milkshake.Domain.Catalog;
using Milkshake.Domain.Common;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Infrastructure.Management;

/// <summary>
/// Effective-dated pricing. Price rows that have taken effect are never edited; a new price ends the latest one
/// at its start. Every insert, end-date change and cancellation is audited by the SaveChanges interceptor.
/// </summary>
internal sealed class CatalogPricingService(MilkshakeDbContext dbContext, TimeProvider timeProvider) : ICatalogPricingService
{
    // Flavour, Topping and Consistency all name their price history "Prices".
    private const string PricesNavigation = nameof(Flavour.Prices);

    public async Task<PriceHistorySummary?> GetHistoryAsync(CatalogKind kind, int id, CancellationToken cancellationToken)
    {
        CatalogItem? item = await FindAsync(kind, id, tracked: false, cancellationToken);
        return item is null ? null : ToHistory(item, timeProvider.GetUtcNow());
    }

    public async Task<ManagementResult<PriceHistorySummary>> ScheduleAsync(
        CatalogKind kind,
        int id,
        SchedulePriceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new Dictionary<string, string[]>();
        ManagementValidation.Amount(errors, ManagementFields.Amount, command.Amount);
        if (errors.Count > 0)
        {
            return ManagementResult<PriceHistorySummary>.Invalid(errors);
        }

        CatalogItem? item = await FindAsync(kind, id, tracked: true, cancellationToken);
        if (item is null)
        {
            return ManagementResult<PriceHistorySummary>.NotFound();
        }

        if (item.Version != command.Version)
        {
            return ManagementResult<PriceHistorySummary>.Conflict();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        string currency = await PricingCurrency.GetAsync(dbContext, cancellationToken);

        try
        {
            item.SchedulePrice(new Money(command.Amount, currency), command.EffectiveFrom ?? now, now);
        }
        catch (BusinessRuleException exception)
        {
            return ManagementResult<PriceHistorySummary>.Invalid(ManagementFields.EffectiveFrom, exception.Message);
        }

        return await SaveAsync(item, cancellationToken);
    }

    public async Task<ManagementResult<PriceHistorySummary>> CancelScheduledAsync(
        CatalogKind kind,
        int id,
        int priceId,
        uint version,
        CancellationToken cancellationToken)
    {
        CatalogItem? item = await FindAsync(kind, id, tracked: true, cancellationToken);
        if (item is null)
        {
            return ManagementResult<PriceHistorySummary>.NotFound();
        }

        if (item.Version != version)
        {
            return ManagementResult<PriceHistorySummary>.Conflict();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();

        // The manager must be cancelling exactly the latest, not-yet-started price they saw.
        PricePeriod? latest = item.GetPriceHistory().FirstOrDefault(period => period.EffectiveTo is null);
        if (latest is null || latest.Id != priceId || latest.EffectiveFrom <= now)
        {
            return ManagementResult<PriceHistorySummary>.Invalid(ManagementFields.PriceId, ManagementMessages.PriceNotCancellable);
        }

        PricePeriod cancelled;
        try
        {
            cancelled = item.CancelLatestScheduledPrice(now);
        }
        catch (BusinessRuleException exception)
        {
            return ManagementResult<PriceHistorySummary>.Invalid(ManagementFields.PriceId, exception.Message);
        }

        // Never in force, so no order used it; the audit log keeps a full record of the deleted row.
        dbContext.Remove(cancelled);
        return await SaveAsync(item, cancellationToken);
    }

    private async Task<ManagementResult<PriceHistorySummary>> SaveAsync(CatalogItem item, CancellationToken cancellationToken)
    {
        // "Touch" the item so its xmin version check applies to price changes too: of two managers working
        // from the same version, the second gets 409 instead of both changing the price.
        dbContext.Entry((object)item).Property(nameof(CatalogItem.UpdatedAt)).IsModified = true;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ManagementResult<PriceHistorySummary>.Conflict();
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // A simultaneous price change hit the "one open price" or "one price per start time" index.
            return ManagementResult<PriceHistorySummary>.Conflict();
        }

        return ManagementResult<PriceHistorySummary>.Success(ToHistory(item, timeProvider.GetUtcNow()));
    }

    private Task<CatalogItem?> FindAsync(CatalogKind kind, int id, bool tracked, CancellationToken cancellationToken) =>
        kind switch
        {
            CatalogKind.Flavours => FindAsync(dbContext.Flavours, id, tracked, cancellationToken),
            CatalogKind.Toppings => FindAsync(dbContext.Toppings, id, tracked, cancellationToken),
            CatalogKind.Consistencies => FindAsync(dbContext.Consistencies, id, tracked, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown catalogue kind."),
        };

    private static async Task<CatalogItem?> FindAsync<TItem>(DbSet<TItem> items, int id, bool tracked, CancellationToken cancellationToken)
        where TItem : CatalogItem
    {
        IQueryable<TItem> query = tracked ? items : items.AsNoTracking();
        return await query.Include(PricesNavigation).SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    private static PriceHistorySummary ToHistory(CatalogItem item, DateTimeOffset now) =>
        new(
            CatalogManagementService.ToSummary(item, now),
            item.GetPriceHistory().Select(period => ToPeriod(period, now)).ToList());

    private static PricePeriodSummary ToPeriod(PricePeriod period, DateTimeOffset now)
    {
        PriceStatus status = period.EffectiveFrom > now
            ? PriceStatus.Scheduled
            : period.IsEffectiveAt(now) ? PriceStatus.Current : PriceStatus.Past;

        return new PricePeriodSummary(
            period.Id,
            period.Price.Amount,
            period.Price.CurrencyCode,
            period.EffectiveFrom,
            period.EffectiveTo,
            status,
            CanCancel: status == PriceStatus.Scheduled && period.EffectiveTo is null);
    }
}