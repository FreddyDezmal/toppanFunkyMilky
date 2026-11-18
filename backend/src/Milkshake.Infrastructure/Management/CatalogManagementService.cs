using Microsoft.EntityFrameworkCore;
using Milkshake.Application.Management;
using Milkshake.Domain.Catalog;
using Milkshake.Domain.Common;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Infrastructure.Management;

/// <summary>
/// Manager maintenance of flavours, toppings and consistencies. Every change is audited by the SaveChanges
/// interceptor; stale updates are rejected through the xmin row version.
/// </summary>
internal sealed class CatalogManagementService(MilkshakeDbContext dbContext, TimeProvider timeProvider)
    : ICatalogManagementService
{
    // Flavour, Topping and Consistency all name their price history "Prices".
    private const string PricesNavigation = nameof(Flavour.Prices);

    public Task<IReadOnlyList<CatalogItemSummary>> ListAsync(CatalogKind kind, StatusFilter status, CancellationToken cancellationToken) =>
        kind switch
        {
            CatalogKind.Flavours => ListAsync(dbContext.Flavours, status, cancellationToken),
            CatalogKind.Toppings => ListAsync(dbContext.Toppings, status, cancellationToken),
            CatalogKind.Consistencies => ListAsync(dbContext.Consistencies, status, cancellationToken),
            _ => throw UnknownKind(kind),
        };

    public Task<CatalogItemSummary?> GetAsync(CatalogKind kind, int id, CancellationToken cancellationToken) =>
        kind switch
        {
            CatalogKind.Flavours => GetAsync(dbContext.Flavours, id, cancellationToken),
            CatalogKind.Toppings => GetAsync(dbContext.Toppings, id, cancellationToken),
            CatalogKind.Consistencies => GetAsync(dbContext.Consistencies, id, cancellationToken),
            _ => throw UnknownKind(kind),
        };

    public Task<ManagementResult<CatalogItemSummary>> CreateAsync(CatalogKind kind, CreateCatalogItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return kind switch
        {
            CatalogKind.Flavours => CreateAsync(dbContext.Flavours, kind, command, (name, order) => new Flavour(name, order), cancellationToken),
            CatalogKind.Toppings => CreateAsync(dbContext.Toppings, kind, command, (name, order) => new Topping(name, order), cancellationToken),
            CatalogKind.Consistencies => CreateAsync(dbContext.Consistencies, kind, command, (name, order) => new Consistency(name, order), cancellationToken),
            _ => throw UnknownKind(kind),
        };
    }

    public Task<ManagementResult<CatalogItemSummary>> UpdateAsync(CatalogKind kind, int id, UpdateCatalogItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return kind switch
        {
            CatalogKind.Flavours => UpdateAsync(dbContext.Flavours, kind, id, command, cancellationToken),
            CatalogKind.Toppings => UpdateAsync(dbContext.Toppings, kind, id, command, cancellationToken),
            CatalogKind.Consistencies => UpdateAsync(dbContext.Consistencies, kind, id, command, cancellationToken),
            _ => throw UnknownKind(kind),
        };
    }

    public Task<ManagementResult<CatalogItemSummary>> SetActiveAsync(CatalogKind kind, int id, bool active, uint version, CancellationToken cancellationToken) =>
        kind switch
        {
            CatalogKind.Flavours => SetActiveAsync(dbContext.Flavours, kind, id, active, version, cancellationToken),
            CatalogKind.Toppings => SetActiveAsync(dbContext.Toppings, kind, id, active, version, cancellationToken),
            CatalogKind.Consistencies => SetActiveAsync(dbContext.Consistencies, kind, id, active, version, cancellationToken),
            _ => throw UnknownKind(kind),
        };

    private async Task<IReadOnlyList<CatalogItemSummary>> ListAsync<TItem>(DbSet<TItem> items, StatusFilter status, CancellationToken cancellationToken)
        where TItem : CatalogItem
    {
        IQueryable<TItem> query = items.AsNoTracking().Include(PricesNavigation);
        query = status switch
        {
            StatusFilter.Active => query.Where(item => item.IsActive),
            StatusFilter.Inactive => query.Where(item => !item.IsActive),
            _ => query,
        };

        List<TItem> found = await query
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Name)
            .ToListAsync(cancellationToken);

        DateTimeOffset now = timeProvider.GetUtcNow();
        return found.ConvertAll(item => ToSummary(item, now));
    }

    private async Task<CatalogItemSummary?> GetAsync<TItem>(DbSet<TItem> items, int id, CancellationToken cancellationToken)
        where TItem : CatalogItem
    {
        TItem? item = await items.AsNoTracking().Include(PricesNavigation)
            .SingleOrDefaultAsync(i => i.Id == id, cancellationToken);

        return item is null ? null : ToSummary(item, timeProvider.GetUtcNow());
    }

    private async Task<ManagementResult<CatalogItemSummary>> CreateAsync<TItem>(
        DbSet<TItem> items,
        CatalogKind kind,
        CreateCatalogItemCommand command,
        Func<string, int, TItem> create,
        CancellationToken cancellationToken)
        where TItem : CatalogItem
    {
        var errors = new Dictionary<string, string[]>();
        ManagementValidation.Name(errors, ManagementFields.Name, command.Name, CatalogItem.NameMaxLength);
        ManagementValidation.DisplayOrder(errors, ManagementFields.DisplayOrder, command.DisplayOrder);
        ManagementValidation.Amount(errors, ManagementFields.InitialPrice, command.InitialPrice);
        if (errors.Count > 0)
        {
            return ManagementResult<CatalogItemSummary>.Invalid(errors);
        }

        string name = command.Name.Trim();
        if (await IsNameTakenAsync(items, name, exceptId: null, cancellationToken))
        {
            return DuplicateName(kind);
        }

        string currency = await PricingCurrency.GetAsync(dbContext, cancellationToken);

        TItem item = create(name, command.DisplayOrder);
        item.SetPrice(new Money(command.InitialPrice, currency), timeProvider.GetUtcNow());
        items.Add(item);

        return await SaveAsync(item, kind, cancellationToken);
    }

    private async Task<ManagementResult<CatalogItemSummary>> UpdateAsync<TItem>(
        DbSet<TItem> items,
        CatalogKind kind,
        int id,
        UpdateCatalogItemCommand command,
        CancellationToken cancellationToken)
        where TItem : CatalogItem
    {
        var errors = new Dictionary<string, string[]>();
        ManagementValidation.Name(errors, ManagementFields.Name, command.Name, CatalogItem.NameMaxLength);
        ManagementValidation.DisplayOrder(errors, ManagementFields.DisplayOrder, command.DisplayOrder);
        if (errors.Count > 0)
        {
            return ManagementResult<CatalogItemSummary>.Invalid(errors);
        }

        TItem? item = await items.Include(PricesNavigation).SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return ManagementResult<CatalogItemSummary>.NotFound();
        }

        if (item.Version != command.Version)
        {
            return ManagementResult<CatalogItemSummary>.Conflict();
        }

        string name = command.Name.Trim();
        if (await IsNameTakenAsync(items, name, id, cancellationToken))
        {
            return DuplicateName(kind);
        }

        item.Rename(name);
        item.ChangeDisplayOrder(command.DisplayOrder);

        return await SaveAsync(item, kind, cancellationToken);
    }

    private async Task<ManagementResult<CatalogItemSummary>> SetActiveAsync<TItem>(
        DbSet<TItem> items,
        CatalogKind kind,
        int id,
        bool active,
        uint version,
        CancellationToken cancellationToken)
        where TItem : CatalogItem
    {
        TItem? item = await items.Include(PricesNavigation).SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return ManagementResult<CatalogItemSummary>.NotFound();
        }

        if (item.Version != version)
        {
            return ManagementResult<CatalogItemSummary>.Conflict();
        }

        if (active)
        {
            item.Activate();
        }
        else
        {
            item.Deactivate();
        }

        return await SaveAsync(item, kind, cancellationToken);
    }

    /// <summary>
    /// Saves, translating a lost race into Conflict (someone saved between our read and write) and a
    /// unique-index rejection into a duplicate-name error (someone created the same name at the same time).
    /// </summary>
    private async Task<ManagementResult<CatalogItemSummary>> SaveAsync(CatalogItem item, CatalogKind kind, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ManagementResult<CatalogItemSummary>.Conflict();
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return DuplicateName(kind);
        }

        return ManagementResult<CatalogItemSummary>.Success(ToSummary(item, timeProvider.GetUtcNow()));
    }

    private static Task<bool> IsNameTakenAsync<TItem>(IQueryable<TItem> items, string name, int? exceptId, CancellationToken cancellationToken)
        where TItem : CatalogItem
    {
        // lower(name) = lower(input): the same rule as the ux_*_lower_name unique indexes.
        string lowered = name.ToLowerInvariant();
        return items.AnyAsync(item => item.Name.ToLower() == lowered && (exceptId == null || item.Id != exceptId), cancellationToken);
    }

    

        /// <summary>Shared with the pricing service so both return the same item shape.</summary>
    internal static CatalogItemSummary ToSummary(CatalogItem item, DateTimeOffset now)
    {
        Money? price = item.PriceAt(now);
        PricePeriod? next = item.GetPriceHistory()
            .Where(period => period.EffectiveFrom > now)
            .MinBy(period => period.EffectiveFrom);

        return new CatalogItemSummary(
            item.Id, item.Name, item.DisplayOrder, item.IsActive,
            price?.Amount, price?.CurrencyCode,
            next?.Price.Amount, next?.EffectiveFrom,
            item.Version);
    }

    private static ManagementResult<CatalogItemSummary> DuplicateName(CatalogKind kind) =>
        ManagementResult<CatalogItemSummary>.Invalid(ManagementFields.Name, ManagementMessages.DuplicateName(kind.Label()));

    private static ArgumentOutOfRangeException UnknownKind(CatalogKind kind) =>
        new(nameof(kind), kind, "Unknown catalogue kind.");
}