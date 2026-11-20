using System.Net.Mail;
using Milkshake.Domain.Common;

namespace Milkshake.Domain.Notifications;

/// <summary>
/// One logical transactional email (M9, decisions D2, D7): created in the same transaction as the business change it
/// reports, then delivered by a background dispatcher with retries. <see cref="NotificationKey"/> is unique, so each
/// event produces at most one email; the recipient and the content data are captured when the event happens.
/// Delivery is at-least-once: a provider idempotency key prevents most duplicates, but exactly-once receipt is not
/// promised.
/// </summary>
public sealed class EmailNotification : IHasTimestamps, IHasRowVersion
{
    public const int KeyMaxLength = 100;
    public const int EmailMaxLength = 256;
    public const int NameMaxLength = 100;
    public const int ProviderMaxLength = 20;
    public const int ProviderMessageIdMaxLength = 100;

    /// <summary>After this many attempts a temporary failure becomes final (about four hours of retries).</summary>
    public const int MaxAttempts = 8;

    /// <summary>The longest wait between attempts.</summary>
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(60);

    private EmailNotification()
    {
    }

    public int Id { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>e.g. "order-confirmed:1234". Unique: the database guarantee of one email per event.</summary>
    public string NotificationKey { get; private set; } = string.Empty;

    public int OrderId { get; private set; }

    public int? RefundId { get; private set; }

    public int? CancellationRequestId { get; private set; }

    /// <summary>The patron's address when the event happened (from their account, never from a request).</summary>
    public string RecipientEmail { get; private set; } = string.Empty;

    public string RecipientName { get; private set; } = string.Empty;

    /// <summary>Which template layout the payload was written for.</summary>
    public int TemplateVersion { get; private set; }

    /// <summary>The content data as JSON, copied from the stored order, payment, refund or request (AC-9.3).</summary>
    public string Payload { get; private set; } = string.Empty;

    public NotificationStatus Status { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    /// <summary>While Sending: until when this dispatcher holds the email. An expired lease means it can be claimed again.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    public DateTimeOffset? LastAttemptAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public string? Provider { get; private set; }

    public string? ProviderMessageId { get; private set; }

    /// <summary>A safe category only – never the provider's response text.</summary>
    public DeliveryError? LastError { get; private set; }

    /// <summary>How often a manager asked for it to be sent again (AC-9.5).</summary>
    public int ResendCount { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; private set; }

    /// <summary>
    /// The key the provider uses to recognise a repeat of the same send. A manager's resend gets a new one, so the
    /// provider really sends again.
    /// </summary>
    public string IdempotencyKey => ResendCount == 0 ? NotificationKey : $"{NotificationKey}#resend-{ResendCount}";

    public static string KeyFor(NotificationKind kind, int id) => kind switch
    {
        NotificationKind.OrderConfirmed => $"order-confirmed:{id}",
        NotificationKind.CancellationRequested => $"cancellation-requested:{id}",
        NotificationKind.OrderCancelled => $"order-cancelled:{id}",
        NotificationKind.RefundProcessed => $"refund-processed:{id}",
        NotificationKind.RefundFailed => $"refund-failed:{id}",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>A plain, single-line address: no display name and no line breaks (prevents header injection).</summary>
    public static bool IsValidRecipient(string? email) =>
        email is { Length: > 3 and <= EmailMaxLength } &&
        !email.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is ',' or '<' or '>') &&
        MailAddress.TryCreate(email, out MailAddress? parsed) &&
        string.Equals(parsed.Address, email, StringComparison.Ordinal);

    public static EmailNotification Create(
        NotificationKind kind,
        string notificationKey,
        int orderId,
        int? refundId,
        int? cancellationRequestId,
        string recipientEmail,
        string recipientName,
        int templateVersion,
        string payload,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        if (notificationKey.Length > KeyMaxLength)
        {
            throw new ArgumentException("The notification key is too long.", nameof(notificationKey));
        }

        if (!IsValidRecipient(recipientEmail))
        {
            throw new BusinessRuleException("The recipient is not a valid single email address.");
        }

        string name = (recipientName ?? string.Empty).Trim();
        if (name.Length > NameMaxLength || name.Any(char.IsControl))
        {
            throw new BusinessRuleException("The recipient name is not valid.");
        }

        if (templateVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(templateVersion));
        }

        return new EmailNotification
        {
            Kind = kind,
            NotificationKey = notificationKey,
            OrderId = orderId,
            RefundId = refundId,
            CancellationRequestId = cancellationRequestId,
            RecipientEmail = recipientEmail,
            RecipientName = name,
            TemplateVersion = templateVersion,
            Payload = payload,
            Status = NotificationStatus.Pending,
            NextAttemptAt = now,
        };
    }

    /// <summary>Whether a dispatcher may take it now: due and pending, or held by a dispatcher whose lease expired.</summary>
    public bool CanBeClaimedAt(DateTimeOffset now) =>
        (Status == NotificationStatus.Pending && NextAttemptAt <= now) ||
        (Status == NotificationStatus.Sending && LockedUntil is { } until && until <= now);

    /// <summary>
    /// Takes the email for one attempt. The dispatcher does the same in one SQL statement (FOR UPDATE SKIP LOCKED);
    /// this method states the rule and is used where a single row is handled.
    /// </summary>
    public void Claim(DateTimeOffset now, TimeSpan lease)
    {
        if (!CanBeClaimedAt(now))
        {
            throw new BusinessRuleException($"A {Status} email cannot be claimed now.");
        }

        Status = NotificationStatus.Sending;
        AttemptCount++;
        LastAttemptAt = now;
        LockedUntil = now + lease;
    }

    public void MarkSent(string provider, string? providerMessageId, DateTimeOffset now)
    {
        RequireSending();
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        Status = NotificationStatus.Sent;
        Provider = Truncate(provider, ProviderMaxLength);
        ProviderMessageId = providerMessageId is null ? null : Truncate(providerMessageId, ProviderMessageIdMaxLength);
        SentAt = now;
        LockedUntil = null;
        LastError = null;
    }

    /// <summary>The recipient is not on the allow-list: never sent (decision D4).</summary>
    public void MarkSuppressed()
    {
        RequireSending();
        Status = NotificationStatus.Suppressed;
        LockedUntil = null;
        LastError = null;
    }

    /// <summary>
    /// An attempt failed. A permanent failure (invalid recipient, rejection) or the last allowed attempt ends it as
    /// Failed; otherwise it waits with exponential backoff (1, 2, 4 … minutes, at most <see cref="MaxBackoff"/>).
    /// </summary>
    public void MarkAttemptFailed(DeliveryError error, bool permanent, DateTimeOffset now, TimeSpan? retryAfter = null)
    {
        RequireSending();
        LastError = error;
        LockedUntil = null;

        if (permanent || AttemptCount >= MaxAttempts)
        {
            Status = NotificationStatus.Failed;
            return;
        }

        TimeSpan wait = Backoff(AttemptCount);
        if (retryAfter is { } asked && asked > wait)
        {
            wait = asked < MaxBackoff ? asked : MaxBackoff;
        }

        Status = NotificationStatus.Pending;
        NextAttemptAt = now + wait;
    }

    /// <summary>
    /// A manager asks for it to be sent again (AC-9.5): same recipient and content, a fresh set of attempts and a new
    /// provider idempotency key. Not for suppressed emails: the allow-list would suppress them again.
    /// </summary>
    public void Resend(DateTimeOffset now)
    {
        if (Status is not (NotificationStatus.Sent or NotificationStatus.Failed))
        {
            throw new BusinessRuleException($"A {Status} email cannot be resent.");
        }

        ResendCount++;
        Status = NotificationStatus.Pending;
        AttemptCount = 0;
        NextAttemptAt = now;
        LockedUntil = null;
        SentAt = null;
        ProviderMessageId = null;
        LastError = null;
    }

    /// <summary>1, 2, 4, 8 … minutes after the given number of attempts, at most <see cref="MaxBackoff"/>.</summary>
    public static TimeSpan Backoff(int attempts)
    {
        int exponent = Math.Clamp(attempts - 1, 0, 10);
        TimeSpan wait = TimeSpan.FromMinutes(1 << exponent);
        return wait < MaxBackoff ? wait : MaxBackoff;
    }

    private void RequireSending()
    {
        if (Status != NotificationStatus.Sending)
        {
            throw new BusinessRuleException($"Only an email being sent can record a result (it is {Status}).");
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>Stored as text; the names are part of a database check constraint.</summary>
public enum NotificationKind
{
    OrderConfirmed,
    CancellationRequested,
    OrderCancelled,
    RefundProcessed,
    RefundFailed,
}

/// <summary>Stored as text; the names are part of a database check constraint.</summary>
public enum NotificationStatus
{
    Pending,
    Sending,
    Sent,
    Failed,
    Suppressed,
}

/// <summary>Why an attempt failed, as a safe category. Stored as text.</summary>
public enum DeliveryError
{
    Timeout,
    ProviderUnavailable,
    RateLimited,
    Rejected,
    InvalidRecipient,
}
