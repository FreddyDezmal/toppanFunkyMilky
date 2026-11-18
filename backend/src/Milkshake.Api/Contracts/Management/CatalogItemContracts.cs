using System.ComponentModel.DataAnnotations;
using Milkshake.Application.Management;

namespace Milkshake.Api.Contracts.Management;

/// <summary>A flavour, topping or consistency. Send <see cref="Version"/> back with any change.</summary>
/// <summary>A flavour, topping or consistency. Send <see cref="Version"/> back with any change.</summary>
public sealed record CatalogItemResponse(
    int Id,
    string Name,
    int DisplayOrder,
    bool IsActive,
    decimal? CurrentPrice,
    string? CurrencyCode,
    decimal? ScheduledPrice,
    DateTimeOffset? ScheduledFrom,
    uint Version)
{
    /// <summary>Maps an application summary.</summary>
    public static CatalogItemResponse From(CatalogItemSummary item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new(item.Id, item.Name, item.DisplayOrder, item.IsActive, item.CurrentPrice, item.CurrencyCode,
            item.ScheduledPrice, item.ScheduledFrom, item.Version);
    }
}

/// <summary>New catalogue item. Every orderable item must have a price, so the initial price is required.</summary>
public sealed class CreateCatalogItemRequest
{
    /// <summary>Display name, unique within its list ignoring letter case.</summary>
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters.")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Position in drop-down lists (0 or more).</summary>
    [Required(ErrorMessage = "Display order is required.")]
    public int? DisplayOrder { get; init; }

    /// <summary>Price effective immediately, in the configured pricing currency; at most two decimal places.</summary>
    [Required(ErrorMessage = "Price is required.")]
    public decimal? InitialPrice { get; init; }
}

/// <summary>Rename or reorder an item.</summary>
public sealed class UpdateCatalogItemRequest
{
    /// <summary>Display name.</summary>
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters.")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Position in drop-down lists.</summary>
    [Required(ErrorMessage = "Display order is required.")]
    public int? DisplayOrder { get; init; }

    /// <summary>The version returned when the item was loaded.</summary>
    [Required(ErrorMessage = "Version is required. Reload and try again.")]
    public uint? Version { get; init; }
}

/// <summary>Activate or deactivate a record.</summary>
public sealed class ChangeStatusRequest
{
    /// <summary>The version returned when the record was loaded.</summary>
    [Required(ErrorMessage = "Version is required. Reload and try again.")]
    public uint? Version { get; init; }
}