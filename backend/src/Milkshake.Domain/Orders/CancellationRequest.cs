using Milkshake.Domain.Common;

namespace Milkshake.Domain.Orders;

/// <summary>
/// A manager's request to cancel a patron's order (B1.2, R2). The order is cancelled only if the patron accepts; the
/// patron may decline; an unanswered request lapses at the pickup time. The reason is plain text shown to the patron.
/// </summary>
public sealed class CancellationRequest : IHasTimestamps, IHasRowVersion
{
    public const int ReasonMaxLength = 50;

    private CancellationRequest()
    {
    }

    public int Id { get; private set; }

    public int OrderId { get; private set; }

    /// <summary>The manager who asked.</summary>
    public Guid RequestedBy { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public CancellationRequestStatus Status { get; private set; }

    /// <summary>When the patron answered, or when the request lapsed.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; private set; }

    /// <summary>The reason as stored: trimmed, required, at most 50 characters, no control characters.</summary>
    public static string? NormaliseReason(string? reason)
    {
        string trimmed = (reason ?? string.Empty).Trim();
        return trimmed.Length is > 0 and <= ReasonMaxLength && !trimmed.Any(char.IsControl) ? trimmed : null;
    }

    public static CancellationRequest Create(Order order, Guid managerId, string reason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (managerId == Guid.Empty)
        {
            throw new ArgumentException("The requesting manager is required.", nameof(managerId));
        }

        string normalised = NormaliseReason(reason)
            ?? throw new BusinessRuleException($"A reason of 1 to {ReasonMaxLength} characters is required.");

        if (!order.CanBeCancelledAt(now))
        {
            throw new BusinessRuleException("Only an order that is not cancelled and whose pickup time has not passed can be cancelled.");
        }

        return new CancellationRequest { OrderId = order.Id, RequestedBy = managerId, Reason = normalised, Status = CancellationRequestStatus.Pending };
    }

    /// <summary>Still waiting for the patron: pending and the pickup time has not passed.</summary>
    public bool IsOpenAt(Order order, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);
        return Status == CancellationRequestStatus.Pending && now < order.PickupAtUtc;
    }

    /// <summary>The patron accepts. The caller cancels the order (with a full refund if paid) in the same transaction.</summary>
    public void Accept(Order order, DateTimeOffset now) => Close(order, now, CancellationRequestStatus.Accepted);

    public void Decline(Order order, DateTimeOffset now) => Close(order, now, CancellationRequestStatus.Declined);

    /// <summary>Marks a request left unanswered at the pickup time, or made moot because the order was cancelled.</summary>
    public bool LapseIfDue(Order order, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (Status != CancellationRequestStatus.Pending || (now < order.PickupAtUtc && order.Status != OrderStatus.Cancelled))
        {
            return false;
        }

        Status = CancellationRequestStatus.Lapsed;
        ClosedAt = now;
        return true;
    }

    private void Close(Order order, DateTimeOffset now, CancellationRequestStatus status)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.Id != OrderId)
        {
            throw new ArgumentException("The order does not belong to this request.", nameof(order));
        }

        if (!IsOpenAt(order, now) || order.Status == OrderStatus.Cancelled)
        {
            throw new BusinessRuleException("This cancellation request can no longer be answered.");
        }

        Status = status;
        ClosedAt = now;
    }
}

/// <summary>Stored as text; the names are part of a database check constraint.</summary>
public enum CancellationRequestStatus
{
    Pending,
    Accepted,
    Declined,
    Lapsed,
}
