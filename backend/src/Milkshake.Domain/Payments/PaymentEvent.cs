namespace Milkshake.Domain.Payments;

/// <summary>
/// A provider notification whose signature was verified (P1, P5). Only identifiers are kept – never the raw payload,
/// which can contain card and customer details (AC-8.6). The unique (provider, event type, provider transaction id)
/// index makes a repeated notification a no-op (AC-8.4).
/// </summary>
public sealed class PaymentEvent
{
    public const int EventTypeMaxLength = 50;

    private PaymentEvent()
    {
    }

    public PaymentEvent(string provider, string eventType, long providerTransactionId, string reference, DateTimeOffset receivedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        if (eventType.Length > EventTypeMaxLength || reference.Length > 100)
        {
            throw new ArgumentException("Event type or reference is too long.", nameof(eventType));
        }

        Provider = provider;
        EventType = eventType;
        ProviderTransactionId = providerTransactionId;
        Reference = reference;
        ReceivedAt = receivedAt;
    }

    public int Id { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string EventType { get; private set; } = string.Empty;

    public long ProviderTransactionId { get; private set; }

    /// <summary>The transaction reference in the event (ours when it is one of our payments).</summary>
    public string Reference { get; private set; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; private set; }
}
