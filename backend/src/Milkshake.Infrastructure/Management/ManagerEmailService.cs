using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Milkshake.Application.Management;
using Milkshake.Domain.Notifications;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Infrastructure.Management;

/// <summary>
/// Management → Emails (M9, AC-9.5, D6). Lists delivery state (never the email's content) and lets a manager queue a
/// sent or failed email again. A resend is an ordinary audited save; the dispatcher delivers it.
/// </summary>
internal sealed partial class ManagerEmailService(
    MilkshakeDbContext db,
    TimeProvider clock,
    ILogger<ManagerEmailService> logger) : IManagerEmailService
{
    public async Task<EmailDeliveryList> ListAsync(NotificationStatus? status, string? orderReference, int limit, CancellationToken cancellationToken)
    {
        // Filter and sort on the joined rows, then build the views (EF cannot translate members of a constructed record).
        IQueryable<Row> rows = Rows();
        if (status is { } wanted)
        {
            rows = rows.Where(r => r.Email.Status == wanted);
        }

        string reference = (orderReference ?? string.Empty).Trim().ToUpperInvariant();
        if (reference.Length > 0)
        {
            rows = rows.Where(r => r.OrderReference == reference);
        }

        List<EmailDeliveryView> items = await Project(rows
                .OrderByDescending(r => r.Email.CreatedAt).ThenByDescending(r => r.Email.Id)
                .Take(Math.Clamp(limit, 1, 200)))
            .ToListAsync(cancellationToken);

        Dictionary<NotificationStatus, int> counts = await db.EmailNotifications.AsNoTracking()
            .GroupBy(n => n.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);
        foreach (NotificationStatus each in Enum.GetValues<NotificationStatus>())
        {
            counts.TryAdd(each, 0);
        }

        return new EmailDeliveryList(items, counts);
    }

    public async Task<ResendEmailResult> ResendAsync(int id, CancellationToken cancellationToken)
    {
        EmailNotification? email = await db.EmailNotifications.SingleOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (email is null)
        {
            return new ResendEmailResult(ResendEmailOutcome.NotFound);
        }

        if (email.Status is not (NotificationStatus.Sent or NotificationStatus.Failed))
        {
            return new ResendEmailResult(ResendEmailOutcome.NotResendable, await ViewAsync(id, cancellationToken));
        }

        email.Resend(clock.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The dispatcher or another manager changed it first.
            db.ChangeTracker.Clear();
            return new ResendEmailResult(ResendEmailOutcome.Changed, await ViewAsync(id, cancellationToken));
        }

        LogResent(logger, email.Id, email.Kind, email.ResendCount);
        return new ResendEmailResult(ResendEmailOutcome.Resent, await ViewAsync(id, cancellationToken));
    }

    private Task<EmailDeliveryView?> ViewAsync(int id, CancellationToken cancellationToken) =>
        Project(Rows().Where(r => r.Email.Id == id)).SingleOrDefaultAsync(cancellationToken);

    private IQueryable<Row> Rows() =>
        from n in db.EmailNotifications.AsNoTracking()
        join o in db.Orders.AsNoTracking() on n.OrderId equals o.Id
        select new Row { Email = n, OrderReference = o.Reference };

    private static IQueryable<EmailDeliveryView> Project(IQueryable<Row> rows) =>
        rows.Select(r => new EmailDeliveryView(
            r.Email.Id,
            r.Email.Kind,
            r.Email.Status,
            r.OrderReference,
            r.Email.RecipientEmail,
            r.Email.AttemptCount,
            r.Email.LastError,
            r.Email.CreatedAt,
            r.Email.LastAttemptAt,
            r.Email.Status == NotificationStatus.Pending ? r.Email.NextAttemptAt : (DateTimeOffset?)null,
            r.Email.SentAt,
            r.Email.Provider,
            r.Email.ResendCount));

    [LoggerMessage(Level = LogLevel.Information, Message = "A manager queued email {NotificationId} ({Kind}) to be sent again (resend {ResendCount}).")]
    private static partial void LogResent(ILogger logger, int notificationId, NotificationKind kind, int resendCount);

    private sealed class Row
    {
        public required EmailNotification Email { get; init; }

        public required string OrderReference { get; init; }
    }
}
