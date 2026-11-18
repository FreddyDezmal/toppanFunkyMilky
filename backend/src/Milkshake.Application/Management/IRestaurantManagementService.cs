namespace Milkshake.Application.Management;

/// <summary>Manager maintenance of pickup restaurants. Restaurants are deactivated, never deleted.</summary>
public interface IRestaurantManagementService
{
    /// <summary>Restaurants ordered by name.</summary>
    Task<IReadOnlyList<RestaurantSummary>> ListAsync(StatusFilter status, CancellationToken cancellationToken);

    /// <summary>One restaurant, or null.</summary>
    Task<RestaurantSummary?> GetAsync(int id, CancellationToken cancellationToken);

    /// <summary>Creates an active restaurant.</summary>
    Task<ManagementResult<RestaurantSummary>> CreateAsync(CreateRestaurantCommand command, CancellationToken cancellationToken);

    /// <summary>Changes name or address. Rejected with Conflict if the version is stale.</summary>
    Task<ManagementResult<RestaurantSummary>> UpdateAsync(int id, UpdateRestaurantCommand command, CancellationToken cancellationToken);

    /// <summary>Activates or deactivates a restaurant. Rejected with Conflict if the version is stale.</summary>
    Task<ManagementResult<RestaurantSummary>> SetActiveAsync(int id, bool active, uint version, CancellationToken cancellationToken);
}

/// <summary>New restaurant.</summary>
public sealed record CreateRestaurantCommand(string Name, string? Address);

/// <summary>Changes to a restaurant, based on the version the manager saw.</summary>
public sealed record UpdateRestaurantCommand(string Name, string? Address, uint Version);

/// <summary>A restaurant as managers see it.</summary>
public sealed record RestaurantSummary(int Id, string Name, string? Address, bool IsActive, uint Version);