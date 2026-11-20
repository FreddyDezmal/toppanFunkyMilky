using Microsoft.EntityFrameworkCore;
using Milkshake.Application.Abstractions;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Orders;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Infrastructure.Ordering;

/// <summary>Read-only, patron-scoped order queries. Reads the stored snapshots only, never today's catalogue.</summary>
internal sealed class OrderQueryService(MilkshakeDbContext db, ICurrentUser currentUser) : IOrderQueryService
{
    public async Task<Order?> FindOwnAsync(string reference, CancellationToken cancellationToken)
    {
        if (!OrderReference.IsValid(reference))
        {
            return null;
        }

        Guid patronId = PatronId();
        return await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.PatronId == patronId && o.Reference == reference, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> ListOwnRecentAsync(CancellationToken cancellationToken)
    {
        Guid patronId = PatronId();
        return await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.PatronId == patronId)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Take(IOrderQueryService.RecentLimit)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }

    private Guid PatronId() =>
        currentUser.UserId ?? throw new InvalidOperationException("Reading orders requires a signed-in patron.");
}
