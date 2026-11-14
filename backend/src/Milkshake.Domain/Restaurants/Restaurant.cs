using Milkshake.Domain.Common;

namespace Milkshake.Domain.Restaurants;

/// <summary>A pickup location (URS §3.1). Deactivated rather than deleted.</summary>
public sealed class Restaurant : IHasTimestamps
{
    /// <summary>Maximum length of a restaurant name.</summary>
    public const int NameMaxLength = 100;

    /// <summary>Maximum length of an address.</summary>
    public const int AddressMaxLength = 250;

    private Restaurant()
    {
    }

    /// <summary>Creates an active restaurant.</summary>
    public Restaurant(string name, string? address)
    {
        Rename(name);
        ChangeAddress(address);
        IsActive = true;
    }

    /// <summary>Database identifier.</summary>
    public int Id { get; private set; }

    /// <summary>Display name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Street address shown to patrons.</summary>
    public string? Address { get; private set; }

    /// <summary>Inactive restaurants cannot be chosen for new orders.</summary>
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

    /// <summary>Changes or clears the address.</summary>
    public void ChangeAddress(string? address)
    {
        string? trimmed = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        if (trimmed?.Length > AddressMaxLength)
        {
            throw new ArgumentException($"Address cannot exceed {AddressMaxLength} characters.", nameof(address));
        }

        Address = trimmed;
    }

    /// <summary>Hides the restaurant from new orders.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Makes the restaurant available again.</summary>
    public void Activate() => IsActive = true;
}