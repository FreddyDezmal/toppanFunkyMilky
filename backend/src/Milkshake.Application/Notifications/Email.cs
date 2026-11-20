using Milkshake.Domain.Notifications;

namespace Milkshake.Application.Notifications;

/// <summary>
/// One rendered email ready to send. The recipient comes from the stored notification (captured from the patron's
/// account at event time), never from a request. <see cref="IdempotencyKey"/> lets a provider recognise a repeat.
/// </summary>
public sealed record EmailMessage(
    string To,
    string ToName,
    string Subject,
    string HtmlBody,
    string TextBody,
    string IdempotencyKey,
    string NotificationKey);

/// <summary>
/// What happened to one send attempt. A failure carries a safe category only (never the provider's response text),
/// whether retrying can help, and how long the provider asked us to wait.
/// </summary>
public sealed record EmailSendResult(bool Sent, string? ProviderMessageId, DeliveryError? Error, bool IsPermanent, TimeSpan? RetryAfter)
{
    public static EmailSendResult Success(string? providerMessageId) => new(true, providerMessageId, null, false, null);

    public static EmailSendResult Temporary(DeliveryError error, TimeSpan? retryAfter = null) => new(false, null, error, false, retryAfter);

    public static EmailSendResult Permanent(DeliveryError error) => new(false, null, error, true, null);
}

/// <summary>
/// An email provider (decision D3): Mailpit for local capture, or Resend. Implementations live in Infrastructure and
/// never throw for delivery problems – they return a result the dispatcher can act on.
/// </summary>
public interface IEmailSender
{
    /// <summary>Stored on each sent email, e.g. "Mailpit".</summary>
    string Name { get; }

    /// <summary>False when no provider is configured: the dispatcher then leaves emails queued.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// True when mail reaches real inboxes (Resend). Only then does the recipient allow-list apply (decision D4); a
    /// local capture tool (Mailpit) never delivers anywhere.
    /// </summary>
    bool DeliversToRealInboxes { get; }

    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
