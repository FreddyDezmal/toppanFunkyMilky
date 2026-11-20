using Microsoft.EntityFrameworkCore;
using Milkshake.Application.Ordering;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Infrastructure.Ordering;

/// <summary>The refund facts the payment label needs (decision R3), for a set of orders the caller may already see.</summary>
internal static class RefundFactsReader
{
    public static async Task<ILookup<int, RefundFacts>> ForOrdersAsync(MilkshakeDbContext db, IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
        {
            return Array.Empty<RefundFacts>().ToLookup(r => r.Id);
        }

        int[] ids = [.. orderIds];
        var rows = await db.Refunds
            .AsNoTracking()
            .Where(r => ids.Contains(r.OrderId))
            .Select(r => new { r.OrderId, r.Id, r.Kind, r.Status, r.FeeMinor, r.CreatedAt })
            .ToListAsync(cancellationToken);

        return rows.ToLookup(r => r.OrderId, r => new RefundFacts(r.Id, r.Kind, r.Status, r.FeeMinor, r.CreatedAt));
    }
}
