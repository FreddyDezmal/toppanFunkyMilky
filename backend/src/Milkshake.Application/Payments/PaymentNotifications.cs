namespace Milkshake.Application.Payments;

/// <summary>
/// A provider notification whose signature has already been verified. Only identifiers are taken from it: the payment's
/// outcome, amount and currency are always re-read from the provider's verification API (decision P5).
/// </summary>
public sealed record PaymentNotification(string EventType, long TransactionId, string Reference);

public enum NotificationOutcome
{
    /// <summary>Verified and applied (or nothing needed changing).</summary>
    Processed,

    /// <summary>Already processed earlier; acknowledged without side effects (AC-8.4).</summary>
    Duplicate,

    /// <summary>Not an event we act on, or not one of our payments; acknowledged.</summary>
    Ignored,

    /// <summary>Could not be settled now (provider unavailable, concurrent change): ask the provider to send it again.</summary>
    RetryLater,
}

public interface IPaymentNotificationService
{
    Task<NotificationOutcome> HandleAsync(PaymentNotification notification, CancellationToken cancellationToken);
}
