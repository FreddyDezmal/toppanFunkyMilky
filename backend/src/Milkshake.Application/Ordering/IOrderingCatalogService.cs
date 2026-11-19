namespace Milkshake.Application.Ordering;

/// <summary>What a patron can choose from right now. Read-only.</summary>
public interface IOrderingCatalogService
{
    /// <summary>Active options with current fees, active restaurants, limits and the pickup window; unavailable if settings are invalid.</summary>
    Task<OrderingOptionsResult> GetOptionsAsync(CancellationToken cancellationToken);
}

/// <summary>A flavour, consistency or topping with its fee in force now.</summary>
public sealed record OrderingOption(int Id, string Name, decimal FeeAmount, string CurrencyCode);

/// <summary>An active pickup restaurant.</summary>
public sealed record OrderingRestaurant(int Id, string Name, string? Address);

/// <summary>Everything the ordering screens need, read at one moment.</summary>
public sealed record OrderingOptions(
    IReadOnlyList<OrderingOption> Flavours,
    IReadOnlyList<OrderingOption> Consistencies,
    IReadOnlyList<OrderingOption> Toppings,
    IReadOnlyList<OrderingRestaurant> Restaurants,
    OrderingSettings Settings,
    PickupWindow Pickup);

/// <summary>The options, or null when ordering is unavailable (invalid configuration).</summary>
public sealed record OrderingOptionsResult(OrderingOptions? Options)
{
    /// <summary>Ordering cannot be offered right now.</summary>
    public static OrderingOptionsResult Unavailable() => new((OrderingOptions?)null);
}

/// <summary>User-facing ordering messages.</summary>
public static class OrderingMessages
{
    /// <summary>503 title.</summary>
    public const string UnavailableTitle = "Ordering is temporarily unavailable.";

    /// <summary>503 detail. Configuration problems are logged on the server, never shown to patrons.</summary>
    public const string UnavailableDetail = "Please try again later.";
}