using Milkshake.Domain.Common;

namespace Milkshake.Domain.Catalog;

/// <summary>
/// Shared shape of a selectable milkshake option (flavour, topping, consistency).
/// Items are deactivated, never deleted, so historical orders keep valid references.
/// </summary>
public abstract class CatalogItem : IHasTimestamps
{
    /// <summary>Maximum length of an item name.</summary>
    public const int NameMaxLength = 100;

    /// <summary>For EF Core.</summary>
    protected CatalogItem()
    {
    }

    /// <summary>Creates an active item.</summary>
    protected CatalogItem(string name, int displayOrder)
    {
        Rename(name);
        ChangeDisplayOrder(displayOrder);
        IsActive = true;
    }

    /// <summary>Database identifier.</summary>
    public int Id { get; private set; }

    /// <summary>Name shown to patrons, e.g. "Vanilla".</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Position in drop-down lists (ascending).</summary>
    public int DisplayOrder { get; private set; }

    /// <summary>Inactive items are hidden from new orders but kept for history.</summary>
    public bool IsActive { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Changes the display name.</summary>
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
        {
            throw new ArgumentException($"Name cannot exceed {NameMaxLength} characters.", nameof(name));
        }

        Name = trimmed;
    }

    /// <summary>Changes the position in drop-down lists.</summary>
    public void ChangeDisplayOrder(int displayOrder)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(displayOrder);
        DisplayOrder = displayOrder;
    }

    /// <summary>Hides the item from new orders.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Makes the item available for new orders again.</summary>
    public void Activate() => IsActive = true;
}