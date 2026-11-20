namespace Milkshake.Domain.Orders;

/// <summary>
/// Order lifecycle (M7 decision O1, from the roadmap's AC-7.3 example). Stored as text; the names are part of the
/// database check constraint, so renaming a value needs a migration.
/// </summary>
public enum OrderStatus
{
    /// <summary>Placed and priced; not paid. Every new order starts here.</summary>
    PendingPayment,

    /// <summary>Payment verified by the server (M8). Never set because the browser reports success (AC-7.4).</summary>
    Confirmed,

    /// <summary>A payment attempt failed or was abandoned (M8); the patron may retry (AC-8.3).</summary>
    PaymentFailed,

    /// <summary>Cancelled before payment. Who may cancel is open (Q-10).</summary>
    Cancelled,
}

/// <summary>The allowed status transitions. Anything not listed is rejected.</summary>
public static class OrderStatusTransitions
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new()
    {
        [OrderStatus.PendingPayment] = [OrderStatus.Confirmed, OrderStatus.PaymentFailed, OrderStatus.Cancelled],
        [OrderStatus.PaymentFailed] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [],
        [OrderStatus.Cancelled] = [],
    };

    public static bool IsAllowed(OrderStatus from, OrderStatus to) =>
        Allowed.TryGetValue(from, out OrderStatus[]? targets) && targets.Contains(to);

    /// <summary>The statuses that may still be paid for.</summary>
    public static bool AwaitsPayment(OrderStatus status) => status is OrderStatus.PendingPayment or OrderStatus.PaymentFailed;
}
