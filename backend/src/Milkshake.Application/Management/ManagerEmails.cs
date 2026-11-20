using Milkshake.Domain.Notifications;

namespace Milkshake.Application.Management;

/// <summary>One email as a manager sees it on Management → Emails (AC-9.5, D6): delivery state only, never its content.</summary>
public sealed record EmailDeliveryView(
    int Id,
    NotificationKind Kind,
    NotificationStatus Status,
    string OrderReference,
    string RecipientEmail,
    int AttemptCount,
    DeliveryError? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? SentAt,
    string? Provider,
    int ResendCount)
{
    /// <summary>Only a sent or failed email can be sent again; a suppressed one would be suppressed again (D4).</summary>
    public bool CanResend => Status is NotificationStatus.Sent or NotificationStatus.Failed;
}

/// <summary>The newest emails matching the filter, and how many emails are in each status overall.</summary>
public sealed record EmailDeliveryList(IReadOnlyList<EmailDeliveryView> Items, IReadOnlyDictionary<NotificationStatus, int> Counts);

public enum ResendEmailOutcome
{
    Resent,
    NotFound,

    /// <summary>Waiting, being sent, or suppressed: nothing to resend.</summary>
    NotResendable,

    /// <summary>The email changed while the manager was looking at it (row version).</summary>
    Changed,
}

public sealed record ResendEmailResult(ResendEmailOutcome Outcome, EmailDeliveryView? Email = null);

/// <summary>Management → Emails (AC-9.5): delivery status of transactional emails, and resending one.</summary>
public interface IManagerEmailService
{
    /// <summary>Up to <paramref name="limit"/> emails, newest first, optionally only one status or one order.</summary>
    Task<EmailDeliveryList> ListAsync(NotificationStatus? status, string? orderReference, int limit, CancellationToken cancellationToken);

    /// <summary>Queues the same email again (same recipient and content, new idempotency key). The dispatcher sends it.</summary>
    Task<ResendEmailResult> ResendAsync(int id, CancellationToken cancellationToken);
}
