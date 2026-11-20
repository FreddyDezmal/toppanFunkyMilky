namespace Milkshake.Application.Notifications;

/// <summary>
/// Delivers queued emails (the outbox, decision D7). Separate from every business transaction: a provider outage only
/// delays emails, it never affects a payment, cancellation or refund.
/// </summary>
public interface IEmailDispatcher
{
    /// <summary>Sends one batch of due emails. Returns how many were handled (sent, retried later, failed or suppressed).</summary>
    Task<int> DispatchAsync(CancellationToken cancellationToken);
}
