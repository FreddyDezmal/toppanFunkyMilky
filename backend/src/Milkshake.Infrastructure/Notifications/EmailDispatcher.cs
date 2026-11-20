using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Milkshake.Application.Notifications;
using Milkshake.Domain.Notifications;
using Milkshake.Infrastructure.Persistence;
using Milkshake.Application.Abstractions;
using Milkshake.Domain.Auditing;

namespace Milkshake.Infrastructure.Notifications;

/// <summary>
/// One dispatch run (M9, decision D7), the same reliability approach as payment reconciliation:
/// 1. claim a batch of due emails in one statement (FOR UPDATE SKIP LOCKED), each with a lease, so two dispatchers
///    never send the same email and an email held by a dispatcher that stopped is picked up again after its lease;
/// 2. per email: suppress it if the sender reaches real inboxes and the recipient is not allow-listed (D4), render it,
///    send it with its idempotency key, and record the result straight away.
/// Temporary failures wait with exponential backoff (1, 2, 4 … minutes, up to 8 attempts); permanent ones and
/// unrenderable emails end as Failed. Nothing here touches orders, payments or refunds.
/// </summary>
internal sealed partial class EmailDispatcher(
    MilkshakeDbContext db,
    IEmailSender sender,
    IOptions<EmailOptions> options,
    TimeProvider clock,
    IHostEnvironment environment,
    ILogger<EmailDispatcher> logger) : IEmailDispatcher
{
    public async Task<int> DispatchAsync(CancellationToken cancellationToken)
    {
        using IDisposable actor = AuditActorScope.Begin(AuditActors.EmailDispatcher);   // M11 audit attribution
        EmailOptions settings = options.Value;
        if (!sender.IsConfigured)
        {
            return 0;   // Provider "None" or incomplete settings: emails stay queued until a sender is configured.
        }

        if (settings.PublicBaseUrl is not { IsAbsoluteUri: true } site)
        {
            LogNoPublicBaseUrl(logger);
            return 0;
        }

        // Links patrons follow must be trusted HTTPS; plain http is only for a developer's own machine.
        if (site.Scheme != Uri.UriSchemeHttps && !environment.IsDevelopment())
        {
            LogInsecurePublicBaseUrl(logger);
            return 0;
        }

        DateTimeOffset now = clock.GetUtcNow();
        List<int> claimed = await ClaimAsync(now, settings.Dispatch, cancellationToken);
        if (claimed.Count == 0)
        {
            return 0;
        }

        List<EmailNotification> emails = await db.EmailNotifications
            .Where(n => claimed.Contains(n.Id))
            .OrderBy(n => n.NextAttemptAt).ThenBy(n => n.Id)
            .ToListAsync(cancellationToken);

        int handled = 0;
        foreach (EmailNotification email in emails)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await DeliverAsync(email, site, settings, cancellationToken))
            {
                handled++;
            }
        }

        return handled;
    }

    /// <summary>
    /// Due emails (pending and due, or held by a dispatcher whose lease expired) become Sending with a new lease and one
    /// more attempt, in one statement. Locked rows are skipped, so concurrent dispatchers take different emails.
    /// Raw SQL, so the claim itself is not in the audit trail; every outcome after it is.
    /// </summary>
    private async Task<List<int>> ClaimAsync(DateTimeOffset now, EmailOptions.DispatchSettings dispatch, CancellationToken cancellationToken)
    {
        DateTimeOffset until = now + TimeSpan.FromMinutes(Math.Max(1, dispatch.LeaseMinutes));
        int batch = Math.Max(1, dispatch.BatchSize);
        return await db.Database.SqlQuery<int>($"""
            UPDATE milkshake.email_notifications AS n
            SET status = 'Sending', attempt_count = n.attempt_count + 1, last_attempt_at = {now}, locked_until = {until}, updated_at = {now}
            WHERE n.id IN (
                SELECT c.id
                FROM milkshake.email_notifications AS c
                WHERE (c.status = 'Pending' AND c.next_attempt_at <= {now})
                   OR (c.status = 'Sending' AND c.locked_until <= {now})
                ORDER BY c.next_attempt_at, c.id
                LIMIT {batch}
                FOR UPDATE SKIP LOCKED)
            RETURNING n.id AS "Value"
            """).ToListAsync(cancellationToken);
    }

    private async Task<bool> DeliverAsync(EmailNotification email, Uri site, EmailOptions settings, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        if (email.Status != NotificationStatus.Sending || email.LockedUntil is not { } until || until <= now)
        {
            return false;   // The lease ran out while earlier emails were sent: another run takes it.
        }

        if (email.AttemptCount > EmailNotification.MaxAttempts)
        {
            // Taken back from a dispatcher that stopped on its last attempt: whether that one went out is unknown.
            email.MarkAttemptFailed(DeliveryError.Timeout, permanent: true, now);
            return await SaveAsync(email, cancellationToken);
        }

        if (sender.DeliversToRealInboxes && !settings.IsAllowed(email.RecipientEmail))
        {
            email.MarkSuppressed();
            LogSuppressed(logger, email.Id, email.Kind);
            return await SaveAsync(email, cancellationToken);
        }

        RenderedEmail rendered;
        try
        {
            rendered = EmailTemplates.Render(email, site);
        }
        catch (EmailRenderException exception)
        {
            email.MarkAttemptFailed(DeliveryError.Rejected, permanent: true, now);
            LogNotRendered(logger, exception, email.Id, email.Kind, email.TemplateVersion);
            return await SaveAsync(email, cancellationToken);
        }

        var message = new EmailMessage(
            email.RecipientEmail,
            email.RecipientName,
            rendered.Subject,
            rendered.Html,
            rendered.Text,
            email.IdempotencyKey,
            email.NotificationKey);

        EmailSendResult result;
        try
        {
            result = await sender.SendAsync(message, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Senders report known failures as results; anything unexpected is treated as temporary and retried.
            LogSenderError(logger, exception, email.Id, sender.Name);
            result = EmailSendResult.Temporary(DeliveryError.ProviderUnavailable);
        }

        now = clock.GetUtcNow();
        if (result.Sent)
        {
            email.MarkSent(sender.Name, result.ProviderMessageId, now);
        }
        else
        {
            email.MarkAttemptFailed(result.Error ?? DeliveryError.ProviderUnavailable, result.IsPermanent, now, result.RetryAfter);
            LogAttemptFailed(logger, email.Id, email.Kind, email.AttemptCount, email.LastError, email.Status);
        }

        return await SaveAsync(email, cancellationToken);
    }

    /// <summary>Each outcome is saved on its own, so one problem never loses the others' results.</summary>
    private async Task<bool> SaveAsync(EmailNotification email, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Changed elsewhere since the claim: leave it to the next run, and drop its unsaved audit rows with it.
            foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
            {
                entry.State = EntityState.Detached;
            }

            db.Entry(email).State = EntityState.Detached;
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Emails are queued but not sent: Email:PublicBaseUrl is not configured.")]
    private static partial void LogNoPublicBaseUrl(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Emails are queued but not sent: Email:PublicBaseUrl must use https outside development.")]
    private static partial void LogInsecurePublicBaseUrl(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Email {NotificationId} ({Kind}) was suppressed: its recipient is not on the allow-list.")]
    private static partial void LogSuppressed(ILogger logger, int notificationId, NotificationKind kind);

    [LoggerMessage(Level = LogLevel.Error, Message = "Email {NotificationId} ({Kind}, template version {TemplateVersion}) could not be rendered and was marked failed.")]
    private static partial void LogNotRendered(ILogger logger, Exception exception, int notificationId, NotificationKind kind, int templateVersion);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The email sender {Sender} failed unexpectedly for email {NotificationId}; it will be retried.")]
    private static partial void LogSenderError(ILogger logger, Exception exception, int notificationId, string sender);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {NotificationId} ({Kind}) attempt {Attempt} failed: {Error}; now {Status}.")]
    private static partial void LogAttemptFailed(ILogger logger, int notificationId, NotificationKind kind, int attempt, DeliveryError? error, NotificationStatus status);
}

/// <summary>
/// Runs the dispatcher inside the API every few seconds (no extra infrastructure), like the reconciliation job. A run
/// that fails is logged and the next one tries again; queued emails are never lost.
/// </summary>
internal sealed partial class EmailDispatchWorker(
    IServiceScopeFactory scopes,
    IOptions<EmailOptions> options,
    ILogger<EmailDispatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EmailOptions.DispatchSettings dispatch = options.Value.Dispatch;
        if (!dispatch.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, dispatch.IntervalSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IEmailDispatcher>().DispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException or TimeoutException or Npgsql.NpgsqlException)
            {
                LogRunFailed(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "An email dispatch run failed; the next run will try again.")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);
}
