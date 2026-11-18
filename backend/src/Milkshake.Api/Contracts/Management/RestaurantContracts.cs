using System.ComponentModel.DataAnnotations;
using Milkshake.Application.Management;

namespace Milkshake.Api.Contracts.Management;

/// <summary>A pickup restaurant. Send <see cref="Version"/> back with any change.</summary>
public sealed record RestaurantResponse(int Id, string Name, string? Address, bool IsActive, uint Version)
{
    /// <summary>Maps an application summary.</summary>
    public static RestaurantResponse From(RestaurantSummary restaurant)
    {
        ArgumentNullException.ThrowIfNull(restaurant);
        return new(restaurant.Id, restaurant.Name, restaurant.Address, restaurant.IsActive, restaurant.Version);
    }
}

/// <summary>New restaurant.</summary>
public sealed class CreateRestaurantRequest
{
    /// <summary>Display name, unique ignoring letter case.</summary>
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters.")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Street address shown to patrons (optional).</summary>
    [StringLength(250, ErrorMessage = "Address cannot exceed 250 characters.")]
    public string? Address { get; init; }
}

/// <summary>Change a restaurant's name or address.</summary>
public sealed class UpdateRestaurantRequest
{
    /// <summary>Display name.</summary>
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters.")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Street address (optional).</summary>
    [StringLength(250, ErrorMessage = "Address cannot exceed 250 characters.")]
    public string? Address { get; init; }

    /// <summary>The version returned when the restaurant was loaded.</summary>
    [Required(ErrorMessage = "Version is required. Reload and try again.")]
    public uint? Version { get; init; }
}