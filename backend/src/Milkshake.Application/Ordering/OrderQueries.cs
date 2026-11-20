using Milkshake.Domain.Orders;

namespace Milkshake.Application.Ordering;

/// <summary>
/// Reads the signed-in patron's own orders (AC-7.6). Every query is filtered by the current patron, so another
/// patron's order is indistinguishable from one that does not exist. Full history, filtering and reports are M11/M12.
/// </summary>
public interface IOrderQueryService
{
    /// <summary>Most recent orders shown in the patron's basic list.</summary>
    public const int RecentLimit = 20;

    /// <summary>The patron's order with this reference and its items, or null if there is none for this patron.</summary>
    Task<Order?> FindOwnAsync(string reference, CancellationToken cancellationToken);

    /// <summary>The patron's newest orders (at most <see cref="RecentLimit"/>), with their items.</summary>
    Task<IReadOnlyList<Order>> ListOwnRecentAsync(CancellationToken cancellationToken);
}
