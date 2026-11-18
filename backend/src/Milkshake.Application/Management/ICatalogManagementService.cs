namespace Milkshake.Application.Management;

/// <summary>The three priced catalogue lists (URS §3.4).</summary>
public enum CatalogKind
{
    /// <summary>Flavours.</summary>
    Flavours,

    /// <summary>Toppings.</summary>
    Toppings,

    /// <summary>Consistencies ("thick or not").</summary>
    Consistencies,
}

/// <summary>Display helpers for <see cref="CatalogKind"/>.</summary>
public static class CatalogKindExtensions
{
    /// <summary>Singular, lower-case label, e.g. "flavour".</summary>
    public static string Label(this CatalogKind kind) => kind switch
    {
        CatalogKind.Flavours => "flavour",
        CatalogKind.Toppings => "topping",
        CatalogKind.Consistencies => "consistency",
        _ => "item",
    };
}

/// <summary>Manager maintenance of flavours, toppings and consistencies. Items are deactivated, never deleted.</summary>
public interface ICatalogManagementService
{
    /// <summary>Items of one kind, ordered by display order then name, with their current price.</summary>
    Task<IReadOnlyList<CatalogItemSummary>> ListAsync(CatalogKind kind, StatusFilter status, CancellationToken cancellationToken);

    /// <summary>One item, or null.</summary>
    Task<CatalogItemSummary?> GetAsync(CatalogKind kind, int id, CancellationToken cancellationToken);

    /// <summary>Creates an active item with its initial price, effective immediately.</summary>
    Task<ManagementResult<CatalogItemSummary>> CreateAsync(CatalogKind kind, CreateCatalogItemCommand command, CancellationToken cancellationToken);

    /// <summary>Renames or reorders an item. Rejected with Conflict if <see cref="UpdateCatalogItemCommand.Version"/> is stale.</summary>
    Task<ManagementResult<CatalogItemSummary>> UpdateAsync(CatalogKind kind, int id, UpdateCatalogItemCommand command, CancellationToken cancellationToken);

    /// <summary>Activates or deactivates an item. Rejected with Conflict if <paramref name="version"/> is stale.</summary>
    Task<ManagementResult<CatalogItemSummary>> SetActiveAsync(CatalogKind kind, int id, bool active, uint version, CancellationToken cancellationToken);
}

/// <summary>New catalogue item. The currency comes from the Pricing.CurrencyCode setting.</summary>
public sealed record CreateCatalogItemCommand(string Name, int DisplayOrder, decimal InitialPrice);

/// <summary>Changes to an existing item, based on the version the manager saw.</summary>
public sealed record UpdateCatalogItemCommand(string Name, int DisplayOrder, uint Version);

/// <summary>A catalogue item as managers see it, with its current and next scheduled price.</summary>
public sealed record CatalogItemSummary(
    int Id,
    string Name,
    int DisplayOrder,
    bool IsActive,
    decimal? CurrentPrice,
    string? CurrencyCode,
    decimal? ScheduledPrice,
    DateTimeOffset? ScheduledFrom,
    uint Version);