using Microsoft.EntityFrameworkCore;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Orders;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Infrastructure.Ordering;

/// <summary>
/// A patron's order history for frequent-customer discounts (decision Q-03, 22 Nov 2026): only orders whose payment
/// was verified and that were not later cancelled (<see cref="OrderStatus.Confirmed"/>) count. Pending, failed and
/// cancelled (and therefore refunded) orders are ignored. Each order contributes its number of drinks.
/// </summary>
internal sealed class PaidOrderHistorySource(MilkshakeDbContext dbContext) : IDiscountEligibilitySource
{
    public async Task<DiscountHistory> GetHistoryAsync(Guid patronId, CancellationToken cancellationToken)
    {
        if (patronId == Guid.Empty)
        {
            return DiscountHistory.Empty;
        }

        List<int> drinkCounts = await dbContext.Orders.AsNoTracking()
            .Where(order => order.PatronId == patronId && order.Status == OrderStatus.Confirmed)
            .OrderBy(order => order.Id)
            .Select(order => order.Items.Count)
            .ToListAsync(cancellationToken);

        return new DiscountHistory(drinkCounts);
    }
}
