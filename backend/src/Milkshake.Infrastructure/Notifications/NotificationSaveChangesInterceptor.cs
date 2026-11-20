using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Milkshake.Application.Notifications;
using Milkshake.Domain.Common;
using Milkshake.Domain.Notifications;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;
using Milkshake.Domain.Settings;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Infrastructure.Notifications;

/// <summary>
/// The email outbox's producer (D7). Before every SaveChanges it looks for the four events that send an email – order
/// confirmed, cancellation requested, order cancelled, refund processed or failed – and adds their outbox rows to the
/// same SaveChanges, so the business change and its email commit or roll back together. Nothing is sent here: the
/// dispatcher delivers later, so an email-provider outage can never affect a payment, cancellation or refund.
/// Registered before the audit interceptor, so the new rows get timestamps and are audited too.
/// It never fails the business save: an event whose email cannot be built (no account, invalid address, already
/// queued) is skipped and logged, without the address.
/// </summary>
internal sealed partial class NotificationSaveChangesInterceptor(TimeProvider timeProvider, ILogger<NotificationSaveChangesInterceptor> logger)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is MilkshakeDbContext context)
        {
            // With sync = true every database call below is synchronous, so this completes without blocking on I/O.
            QueueAsync(context, sync: true, CancellationToken.None).GetAwaiter().GetResult();
        }

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is MilkshakeDbContext context)
        {
            await QueueAsync(context, sync: false, cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private async Task QueueAsync(MilkshakeDbContext context, bool sync, CancellationToken cancellationToken)
    {
        List<EntityEntry> entries = context.ChangeTracker.Entries().ToList();
        List<Event> events = Detect(entries);
        if (events.Count == 0)
        {
            return;
        }

        // Keys already in this unit of work: a retried SaveChanges (transient failure) must not queue them twice.
        var queued = new HashSet<string>(
            entries.Select(e => e.Entity).OfType<EmailNotification>().Select(n => n.NotificationKey), StringComparer.Ordinal);
        DateTimeOffset now = timeProvider.GetUtcNow();
        string? locale = null;
        var created = new List<EmailNotification>();

        foreach (Event @event in events)
        {
            if (queued.Contains(EmailNotification.KeyFor(@event.Kind, @event.EventId)))
            {
                continue;
            }

            Order? order = await OrderAsync(context, entries, @event.OrderId, @event.Kind == NotificationKind.OrderConfirmed, sync, cancellationToken);
            if (order is null)
            {
                LogSkipped(logger, @event.Kind, @event.OrderId, "the order was not found");
                continue;
            }

            NotificationRecipient? recipient = await RecipientAsync(context, order.PatronId, sync, cancellationToken);
            if (recipient is null)
            {
                LogSkipped(logger, @event.Kind, order.Id, "the patron's account was not found");
                continue;
            }

            locale ??= await LocaleAsync(context, sync, cancellationToken);

            EmailNotification? email;
            try
            {
                email = await BuildAsync(context, entries, @event, order, recipient, locale, now, sync, cancellationToken);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or BusinessRuleException)
            {
                LogNotBuilt(logger, exception, @event.Kind, order.Id);
                continue;
            }

            if (email is null)
            {
                LogSkipped(logger, @event.Kind, order.Id, "the patron has no valid email address");
                continue;
            }

            created.Add(email);
            queued.Add(email.NotificationKey);
        }

        if (created.Count == 0)
        {
            return;
        }

        // Already queued by an earlier save: the unique key would reject the whole business save, so skip it here.
        List<string> keys = created.Select(n => n.NotificationKey).ToList();
        IQueryable<string> existingQuery = context.EmailNotifications.AsNoTracking()
            .Where(n => keys.Contains(n.NotificationKey))
            .Select(n => n.NotificationKey);
        var existing = new HashSet<string>(sync ? existingQuery.ToList() : await existingQuery.ToListAsync(cancellationToken), StringComparer.Ordinal);

        foreach (EmailNotification email in created)
        {
            if (existing.Contains(email.NotificationKey))
            {
                LogAlreadyQueued(logger, email.Kind, email.OrderId);
                continue;
            }

            if (sync)
            {
                context.EmailNotifications.Add(email);
            }
            else
            {
                await context.EmailNotifications.AddAsync(email, cancellationToken);
            }
        }
    }

    /// <summary>The state changes in this SaveChanges that send an email (D2).</summary>
    private static List<Event> Detect(List<EntityEntry> entries)
    {
        var events = new List<Event>();
        foreach (EntityEntry entry in entries)
        {
            switch (entry.Entity)
            {
                case Order order when entry.State == EntityState.Modified && StatusChanged(entry, nameof(Order.Status)):
                    if (order.Status == OrderStatus.Confirmed)
                    {
                        events.Add(new Event(NotificationKind.OrderConfirmed, order.Id, order.Id));
                    }
                    else if (order.Status == OrderStatus.Cancelled)
                    {
                        events.Add(new Event(NotificationKind.OrderCancelled, order.Id, order.Id));
                    }

                    break;
                case CancellationRequest request when entry.State == EntityState.Added:
                    events.Add(new Event(NotificationKind.CancellationRequested, request.Id, request.OrderId, Request: request));
                    break;
                case Refund refund when entry.State == EntityState.Modified && StatusChanged(entry, nameof(Refund.Status)):
                    if (refund.Status == RefundStatus.Processed)
                    {
                        events.Add(new Event(NotificationKind.RefundProcessed, refund.Id, refund.OrderId, Refund: refund));
                    }
                    else if (refund.Status == RefundStatus.Failed)
                    {
                        events.Add(new Event(NotificationKind.RefundFailed, refund.Id, refund.OrderId, Refund: refund));
                    }

                    break;
            }
        }

        return events;
    }

    private static bool StatusChanged(EntityEntry entry, string property)
    {
        PropertyEntry status = entry.Property(property);
        return status.IsModified && !Equals(status.OriginalValue, status.CurrentValue);
    }

    private static async Task<EmailNotification?> BuildAsync(
        MilkshakeDbContext context,
        List<EntityEntry> entries,
        Event @event,
        Order order,
        NotificationRecipient recipient,
        string locale,
        DateTimeOffset now,
        bool sync,
        CancellationToken cancellationToken)
    {
        switch (@event.Kind)
        {
            case NotificationKind.OrderConfirmed:
                Payment? payment = await SucceededPaymentAsync(context, entries, order.Id, sync, cancellationToken)
                    ?? throw new InvalidOperationException("The confirmed order has no successful payment.");
                return EmailNotificationFactory.OrderConfirmed(order, payment, recipient, locale, now);

            case NotificationKind.CancellationRequested:
                Payment? paid = await SucceededPaymentAsync(context, entries, order.Id, sync, cancellationToken);
                return EmailNotificationFactory.CancellationRequested(order, @event.Request!, paid, recipient, locale, now);

            case NotificationKind.OrderCancelled:
                // The cancellation's refund and an accepted request are saved in the same SaveChanges (M8b).
                Refund? refund = entries
                    .Where(e => e.State == EntityState.Added)
                    .Select(e => e.Entity)
                    .OfType<Refund>()
                    .FirstOrDefault(r => r.OrderId == order.Id && r.Kind != RefundKind.UnacceptedPayment);
                bool accepted = entries
                    .Select(e => e.Entity)
                    .OfType<CancellationRequest>()
                    .Any(r => r.OrderId == order.Id && r.Status == CancellationRequestStatus.Accepted);
                return EmailNotificationFactory.OrderCancelled(order, refund, accepted, recipient, locale, now);

            default:
                return EmailNotificationFactory.RefundOutcome(order, @event.Refund!, recipient, locale, now);
        }
    }

    /// <summary>The tracked order (it carries this save's changes), or a read-only copy when it is not tracked or its items were not loaded.</summary>
    private static async Task<Order?> OrderAsync(
        MilkshakeDbContext context, List<EntityEntry> entries, int orderId, bool needItems, bool sync, CancellationToken cancellationToken)
    {
        Order? tracked = entries.Select(e => e.Entity).OfType<Order>().FirstOrDefault(o => o.Id == orderId);
        if (tracked is not null && (!needItems || tracked.Items.Count > 0))
        {
            return tracked;
        }

        IQueryable<Order> query = context.Orders.AsNoTracking().Include(o => o.Items).Where(o => o.Id == orderId);
        Order? stored = sync ? query.FirstOrDefault() : await query.FirstOrDefaultAsync(cancellationToken);
        return stored ?? tracked;
    }

    private static async Task<Payment?> SucceededPaymentAsync(
        MilkshakeDbContext context, List<EntityEntry> entries, int orderId, bool sync, CancellationToken cancellationToken)
    {
        // Tracked first: the payment that confirms the order changes status in this very save.
        Payment? tracked = entries.Select(e => e.Entity).OfType<Payment>()
            .FirstOrDefault(p => p.OrderId == orderId && p.Status == PaymentStatus.Succeeded);
        if (tracked is not null)
        {
            return tracked;
        }

        IQueryable<Payment> query = context.Payments.AsNoTracking()
            .Where(p => p.OrderId == orderId && p.Status == PaymentStatus.Succeeded)
            .OrderBy(p => p.Id);
        return sync ? query.FirstOrDefault() : await query.FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>The patron's registered address at event time; never an address supplied by the browser.</summary>
    private static async Task<NotificationRecipient?> RecipientAsync(MilkshakeDbContext context, Guid patronId, bool sync, CancellationToken cancellationToken)
    {
        var query = context.Users.AsNoTracking().Where(u => u.Id == patronId).Select(u => new { u.Email, u.FirstName });
        var user = sync ? query.FirstOrDefault() : await query.FirstOrDefaultAsync(cancellationToken);
        return user is null ? null : new NotificationRecipient(user.Email ?? string.Empty, user.FirstName);
    }

    /// <summary>The display locale at event time (Pricing.DisplayLocale), so the email shows amounts as the app did.</summary>
    private static async Task<string> LocaleAsync(MilkshakeDbContext context, bool sync, CancellationToken cancellationToken)
    {
        IQueryable<string> query = context.SystemSettings.AsNoTracking().Where(s => s.Key == SettingKeys.DisplayLocale).Select(s => s.Value);
        return (sync ? query.FirstOrDefault() : await query.FirstOrDefaultAsync(cancellationToken)) ?? string.Empty;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No {Kind} email for order {OrderId}: {Reason}.")]
    private static partial void LogSkipped(ILogger logger, NotificationKind kind, int orderId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No {Kind} email for order {OrderId}: the email could not be prepared.")]
    private static partial void LogNotBuilt(ILogger logger, Exception exception, NotificationKind kind, int orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The {Kind} email for order {OrderId} was already queued.")]
    private static partial void LogAlreadyQueued(ILogger logger, NotificationKind kind, int orderId);

    private sealed record Event(NotificationKind Kind, int EventId, int OrderId, CancellationRequest? Request = null, Refund? Refund = null);
}
