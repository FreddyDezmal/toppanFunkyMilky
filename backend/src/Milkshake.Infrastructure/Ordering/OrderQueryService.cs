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

        // One extra row tells whether another page exists.
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

        ILookup<int, RefundFacts> refunds = await RefundsAsync(orders.Select(o => o.Id).ToArray(), cancellationToken);
        List<OrderHistoryEntry> items = orders
            .Select(o => new OrderHistoryEntry(o, PaymentLabels.For(o.Status, refunds[o.Id].ToList())))
            .ToList();

        return new OrderHistoryPage(items, more ? new OrderCursor(orders[^1].CreatedAt, orders[^1].Id) : null);
    }

    /// <summary>Refunds of these orders only (already limited to the patron's own).</summary>
    private async Task<ILookup<int, RefundFacts>> RefundsAsync(int[] orderIds, CancellationToken cancellationToken)
    {
        if (orderIds.Length == 0)
        {
            return Array.Empty<RefundFacts>().ToLookup(r => r.Id);
        }

        var rows = await db.Refunds
            .AsNoTracking()
            .Where(r => orderIds.Contains(r.OrderId))
            .Select(r => new { r.OrderId, r.Id, r.Kind, r.Status, r.FeeMinor, r.CreatedAt })
            .ToListAsync(cancellationToken);

        return rows.ToLookup(r => r.OrderId, r => new RefundFacts(r.Id, r.Kind, r.Status, r.FeeMinor, r.CreatedAt));
    }

    private Guid PatronId() =>
        currentUser.UserId ?? throw new InvalidOperationException("Reading orders requires a signed-in patron.");
}
