using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;

namespace Milkshake.Application.Payments;

/// <summary>An order's status with its latest payment attempt, after checking an open attempt with the provider.</summary>
public sealed record PaymentStatusView(OrderStatus OrderStatus, string? PaymentReference, PaymentStatus? PaymentStatus, string? ProviderMessage);

/// <summary>
/// Patron-triggered recovery (decision P7a): when the patron returns from checkout, or views an order with an open
/// attempt, the server asks the provider. Never relies on what the return URL says.
/// </summary>
public interface IPaymentStatusService
{
    /// <summary>The patron's own order, refreshed from the provider when it has an open attempt; null if not theirs.</summary>
    Task<PaymentStatusView?> RefreshAsync(string orderReference, CancellationToken cancellationToken);
}

/// <summary>
/// Background recovery (decision P7b): settles open attempts with the provider, so payments are confirmed even if every
/// webhook is lost and the patron's browser is closed.
/// </summary>
public interface IPaymentReconciler
{
    /// <summary>Checks one batch of open attempts; returns how many changed.</summary>
    Task<int> ReconcileAsync(CancellationToken cancellationToken);
}
