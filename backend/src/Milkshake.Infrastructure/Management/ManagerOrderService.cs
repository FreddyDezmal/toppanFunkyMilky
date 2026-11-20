using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Milkshake.Application.Abstractions;
using Milkshake.Application.Management;
using Milkshake.Application.Ordering;
using Milkshake.Application.Payments;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;
using Milkshake.Infrastructure.Ordering;
using Milkshake.Infrastructure.Persistence;
using Npgsql;

namespace Milkshake.Infrastructure.Management;

/// <summary>
/// Management → Orders (M8b). A manager finds an order by its reference and may ask the patron to accept cancelling it
/// (B1.2); the order changes only if the patron accepts. One pending request per order is enforced by the database.
/// </summary>
internal sealed partial class ManagerOrderService(
    MilkshakeDbContext db,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<ManagerOrderService> logger) : IManagerOrderService
{
    private const string OnePendingIndex = "ux_cancellation_requests_one_pending_per_order";
    private const int AttentionLimit = 100;

    public async Task<ManagerOrderView?> GetAsync(string orderReference, CancellationToken cancellationToken)
    {
        string reference = Normalise(orderReference);
        if (!OrderReference.IsValid(reference))
        {
            return null;
        }

        Order? order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Reference == reference, cancellationToken);
        return order is null ? null : await ViewAsync(order, cancellationToken);
    }

    public async Task<RequestCancellationResult> RequestCancellationAsync(string orderReference, string? reason, CancellationToken cancellationToken)
    {
        Guid managerId = currentUser.UserId ?? throw new InvalidOperationException("Requires a signed-in manager.");
        string reference = Normalise(orderReference);
        Order? order = OrderReference.IsValid(reference)
            ? await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Reference == reference, cancellationToken)
            : null;
        if (order is null)
        {
            return new RequestCancellationResult(RequestCancellationOutcome.NotFound);
        }

        if (CancellationRequest.NormaliseReason(reason) is null)
        {
            return new RequestCancellationResult(RequestCancellationOutcome.InvalidReason, await ViewAsync(order, cancellationToken));
        }

        DateTimeOffset now = clock.GetUtcNow();
        if (!order.CanBeCancelledAt(now))
        {
            return new RequestCancellationResult(RequestCancellationOutcome.NotCancellable, await ViewAsync(order, cancellationToken));
        }

        // The pickup time is still ahead, so any pending request is still waiting for the patron.
        if (await db.CancellationRequests.AnyAsync(r => r.OrderId == order.Id && r.Status == CancellationRequestStatus.Pending, cancellationToken))
        {
            return new RequestCancellationResult(RequestCancellationOutcome.AlreadyRequested, await ViewAsync(order, cancellationToken));
        }

        db.CancellationRequests.Add(CancellationRequest.Create(order, managerId, reason!, now));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: OnePendingIndex })
        {
            db.ChangeTracker.Clear();
            return new RequestCancellationResult(RequestCancellationOutcome.AlreadyRequested, await ViewAsync(order, cancellationToken));
        }

        LogRequested(logger, order.Reference);
        return new RequestCancellationResult(RequestCancellationOutcome.Requested, await ViewAsync(order, cancellationToken));
    }

    public async Task<IReadOnlyList<RefundNeedingAttention>> RefundsNeedingAttentionAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Refunds.AsNoTracking()
            .Where(r => r.Status == RefundStatus.Failed ||
                        (r.Status == RefundStatus.Pending && r.ProviderMessage == GatewayRefund.NeedsAttentionMessage))
            .Join(db.Orders.AsNoTracking(), r => r.OrderId, o => o.Id, (r, o) => new { Refund = r, OrderReference = o.Reference })
            .OrderByDescending(x => x.Refund.CreatedAt)
            .Take(AttentionLimit)
            .ToListAsync(cancellationToken);

        return rows.Select(row => new RefundNeedingAttention(
                row.OrderReference,
                row.Refund.Reference,
                row.Refund.Kind,
                row.Refund.Status,
                MinorUnits.FromMinor(row.Refund.AmountMinor, row.Refund.CurrencyCode),
                row.Refund.CurrencyCode,
                row.Refund.ProviderMessage,
                row.Refund.CreatedAt))
            .ToList();
    }

    private async Task<ManagerOrderView> ViewAsync(Order order, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        Order current = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id, cancellationToken);
        var patron = await db.Users.AsNoTracking().Where(u => u.Id == current.PatronId)
            .Select(u => new { u.FirstName, u.Email }).SingleAsync(cancellationToken);
        Payment? paid = await db.Payments.AsNoTracking().SingleOrDefaultAsync(
            p => p.OrderId == current.Id && p.Status == PaymentStatus.Succeeded, cancellationToken);
        decimal paidAmount = paid is null ? 0m : MinorUnits.FromMinor(paid.AmountMinor, paid.CurrencyCode);

        List<CancellationRequest> requests = await db.CancellationRequests.AsNoTracking()
            .Where(r => r.OrderId == current.Id).OrderByDescending(r => r.Id).ToListAsync(cancellationToken);
        List<Refund> stored = await db.Refunds.AsNoTracking().Where(r => r.OrderId == current.Id).OrderBy(r => r.Id).ToListAsync(cancellationToken);

        bool cancellable = current.CanBeCancelledAt(now);
        return new ManagerOrderView(
            current.Reference,
            current.Status,
            paid is not null,
            patron.FirstName,
            patron.Email ?? string.Empty,
            current.RestaurantName,
            current.PickupAtUtc,
            current.PickupTimeZone,
            current.Total,
            current.CurrencyCode,
            current.CreatedAt,
            cancellable && !requests.Any(r => r.Status == CancellationRequestStatus.Pending),
            requests.Select(r => OrderCancellationService.ToView(r, r.IsOpenAt(current, now) && current.Status != OrderStatus.Cancelled, paidAmount)).ToList(),
            stored.Select(OrderCancellationService.ToView).ToList());
    }

    /// <summary>Managers may type a reference in lower case or with spaces around it.</summary>
    private static string Normalise(string? reference) => (reference ?? string.Empty).Trim().ToUpperInvariant();

    [LoggerMessage(Level = LogLevel.Information, Message = "A manager asked the patron to accept cancelling order {OrderReference}.")]
    private static partial void LogRequested(ILogger logger, string orderReference);
}
