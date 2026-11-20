using Milkshake.Application.Management;

namespace Milkshake.Api.Contracts.Management;

/// <summary>One email's delivery state on Management → Emails. Never includes the email's content.</summary>
public sealed record EmailDeliveryResponse(
    int Id,
    string Kind,
    string Status,
    string OrderReference,
    string RecipientEmail,
    int AttemptCount,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? SentAt,
    string? Provider,
    int ResendCount,
    bool CanResend)
{
    public static EmailDeliveryResponse From(EmailDeliveryView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new EmailDeliveryResponse(view.Id, view.Kind.ToString(), view.Status.ToString(), view.OrderReference, view.RecipientEmail,
            view.AttemptCount, view.LastError?.ToString(), view.CreatedAt, view.LastAttemptAt, view.NextAttemptAt, view.SentAt, view.Provider,
            view.ResendCount, view.CanResend);
    }
}

/// <summary>GET /api/manager/emails: the newest matching emails and the number in each status.</summary>
public sealed record EmailDeliveryListResponse(IReadOnlyList<EmailDeliveryResponse> Items, IReadOnlyDictionary<string, int> Counts)
{
    public static EmailDeliveryListResponse From(EmailDeliveryList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return new EmailDeliveryListResponse(
            list.Items.Select(EmailDeliveryResponse.From).ToList(),
            list.Counts.ToDictionary(c => c.Key.ToString(), c => c.Value));
    }
}

public static class ManagerEmailMessages
{
    public const string StatusInvalid = "Choose one of: Pending, Sending, Sent, Failed, Suppressed.";
    public const string OrderReferenceInvalid = "Enter an order reference such as MS-7KQ2-X9TD.";
    public const string NotFoundTitle = "No email has this id.";
    public const string NotResendableTitle = "Only a sent or failed email can be sent again.";
    public const string NotResendableDetail = "Waiting emails are sent automatically, and suppressed ones are not on the allow-list.";
    public const string ChangedTitle = "This email changed while you were looking at it. Refresh and try again.";
}
