using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Milkshake.Domain.Notifications;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;

namespace Milkshake.Infrastructure.Persistence.Configurations;

/// <summary>
/// The transactional email outbox (M9, decision D7). The unique notification key is the database guarantee of one
/// email per business event; the checks keep status, lease and timestamps consistent even for direct SQL. Foreign keys
/// restrict deletion: the delivery log is evidence for AC-9.5 and is never cascaded away.
/// </summary>
internal sealed class EmailNotificationConfiguration : IEntityTypeConfiguration<EmailNotification>
{
    public void Configure(EntityTypeBuilder<EmailNotification> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string kinds = string.Join(", ", Enum.GetNames<NotificationKind>().Select(name => $"'{name}'"));
        string statuses = string.Join(", ", Enum.GetNames<NotificationStatus>().Select(name => $"'{name}'"));
        string errors = string.Join(", ", Enum.GetNames<DeliveryError>().Select(name => $"'{name}'"));

        builder.ToTable("email_notifications", table =>
        {
            table.HasCheckConstraint("ck_email_notifications_kind", $"kind IN ({kinds})");
            table.HasCheckConstraint("ck_email_notifications_status", $"status IN ({statuses})");
            table.HasCheckConstraint("ck_email_notifications_last_error", $"last_error IS NULL OR last_error IN ({errors})");
            table.HasCheckConstraint("ck_email_notifications_counts", "attempt_count >= 0 AND resend_count >= 0 AND template_version >= 1");
            table.HasCheckConstraint("ck_email_notifications_sent", "(status = 'Sent') = (sent_at IS NOT NULL)");
            table.HasCheckConstraint("ck_email_notifications_lease", "(status = 'Sending') = (locked_until IS NOT NULL)");
            table.HasCheckConstraint("ck_email_notifications_refund",
                "(kind IN ('RefundProcessed', 'RefundFailed')) = (refund_id IS NOT NULL)");
            table.HasCheckConstraint("ck_email_notifications_request",
                "(kind = 'CancellationRequested') = (cancellation_request_id IS NOT NULL)");
            table.HasCheckConstraint("ck_email_notifications_recipient",
                "recipient_email LIKE '%_@_%' AND recipient_email !~ '[[:cntrl:][:space:],<>]' AND recipient_name !~ '[[:cntrl:]]'");
        });

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(n => n.NotificationKey).HasMaxLength(EmailNotification.KeyMaxLength).IsRequired();
        builder.Property(n => n.RecipientEmail).HasMaxLength(EmailNotification.EmailMaxLength).IsRequired();
        builder.Property(n => n.RecipientName).HasMaxLength(EmailNotification.NameMaxLength).IsRequired();
        builder.Property(n => n.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(n => n.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(n => n.LastError).HasConversion<string>().HasMaxLength(30);
        builder.Property(n => n.Provider).HasMaxLength(EmailNotification.ProviderMaxLength);
        builder.Property(n => n.ProviderMessageId).HasMaxLength(EmailNotification.ProviderMessageIdMaxLength);
        builder.Ignore(n => n.IdempotencyKey);

        builder.HasOne<Order>().WithMany().HasForeignKey(n => n.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Refund>().WithMany().HasForeignKey(n => n.RefundId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CancellationRequest>().WithMany().HasForeignKey(n => n.CancellationRequestId).OnDelete(DeleteBehavior.Restrict);

        // D7: one email per business event, however often the event is processed.
        builder.HasIndex(n => n.NotificationKey).IsUnique();

        // The dispatcher's query: due emails, oldest first.
        builder.HasIndex(n => new { n.Status, n.NextAttemptAt });
    }
}
