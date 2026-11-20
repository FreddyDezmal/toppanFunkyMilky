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

    public async Task<OrderHistoryPage> ListOwnAsync(OrderCursor? after, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, OrderHistoryQueryParser.MaxLimit);

        Guid patronId = PatronId();
        IQueryable<Order> query = db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.PatronId == patronId);

        if (after is not null)
        {
            DateTimeOffset createdAt = after.CreatedAt;
            int id = after.Id;
            query = query.Where(o => o.CreatedAt < createdAt || (o.CreatedAt == createdAt && o.Id < id));
        }

        // One extra row tells whether another page exists. Refunds are read only for this patron's orders on the page.
        List<Order> orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Take(limit + 1)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        bool more = orders.Count > limit;
        if (more)
        {
            orders.RemoveAt(limit);
        }

        ILookup<int, RefundFacts> refunds = await RefundFactsReader.ForOrdersAsync(db, orders.Select(o => o.Id).ToList(), cancellationToken);
        List<OrderHistoryEntry> items = orders
            .Select(o => new OrderHistoryEntry(o, PaymentLabels.For(o.Status, refunds[o.Id].ToList())))
            .ToList();

        return new OrderHistoryPage(items, more ? new OrderCursor(orders[^1].CreatedAt, orders[^1].Id) : null);
    }

    private Guid PatronId() =>
        currentUser.UserId ?? throw new InvalidOperationException("Reading orders requires a signed-in patron.");
}
