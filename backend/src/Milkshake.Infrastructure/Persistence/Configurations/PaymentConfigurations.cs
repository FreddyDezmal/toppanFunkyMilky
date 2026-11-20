using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Milkshake.Domain.Common;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;

namespace Milkshake.Infrastructure.Persistence.Configurations;

/// <summary>
/// Payments (M8, decisions P1–P3). The partial unique indexes are what guarantee, in the database, that an order has at
/// most one successful payment and at most one open attempt, however many requests or notifications arrive at once.
/// Foreign keys restrict deletion: payment history is financial evidence and is never cascaded away.
/// </summary>
internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string statuses = string.Join(", ", Enum.GetNames<PaymentStatus>().Select(name => $"'{name}'"));
        string reasons = string.Join(", ", Enum.GetNames<RefundReason>().Select(name => $"'{name}'"));

        builder.ToTable("payments", table =>
        {
            table.HasCheckConstraint("ck_payments_status", $"status IN ({statuses})");
            table.HasCheckConstraint("ck_payments_reference", $"reference ~ '{Payment.ReferencePattern}'");
            table.HasCheckConstraint("ck_payments_amount_positive", "amount_minor > 0");
            table.HasCheckConstraint("ck_payments_currency", "currency_code ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint("ck_payments_refund_reason",
                $"(status = 'RequiresRefund' AND refund_reason IS NOT NULL AND refund_reason IN ({reasons})) OR " +
                "(status <> 'RequiresRefund' AND refund_reason IS NULL)");
            table.HasCheckConstraint("ck_payments_checkout_url", "checkout_url IS NULL OR checkout_url LIKE 'https://%'");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Reference).HasMaxLength(Payment.ReferenceLength).IsFixedLength().IsRequired();
        builder.Property(p => p.Provider).HasMaxLength(Payment.ProviderMaxLength).IsRequired();
        builder.Property(p => p.CurrencyCode).HasMaxLength(Money.CurrencyCodeLength).IsFixedLength().IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.RefundReason).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.CheckoutUrl).HasMaxLength(Payment.CheckoutUrlMaxLength);
        builder.Property(p => p.ProviderMessage).HasMaxLength(Payment.ProviderMessageMaxLength);

        builder.HasOne<Order>().WithMany().HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.Reference).IsUnique();

        // Initiation idempotency: one attempt per order and Idempotency-Key.
        builder.HasIndex(p => new { p.OrderId, p.IdempotencyKey }).IsUnique();

        // P3: at most one successful payment per order, and at most one open attempt per order.
        // Named indexes: EF Core merges unnamed indexes on the same column into one. HasDatabaseName is also needed:
        // the snake_case naming convention otherwise renames them, and the code tells them apart by name.
        builder.HasIndex(p => p.OrderId, "ux_payments_one_success_per_order").IsUnique().HasFilter("status = 'Succeeded'")
            .HasDatabaseName("ux_payments_one_success_per_order");
        builder.HasIndex(p => p.OrderId, "ux_payments_one_open_per_order").IsUnique().HasFilter("status IN ('Initiating', 'Pending')")
            .HasDatabaseName("ux_payments_one_open_per_order");

        // A provider transaction belongs to one attempt; and the reconciliation job finds open attempts by age.
        builder.HasIndex(p => new { p.Provider, p.ProviderTransactionId }).IsUnique().HasFilter("provider_transaction_id IS NOT NULL");
        builder.HasIndex(p => new { p.Status, p.CreatedAt });
    }
}

internal sealed class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("payment_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Provider).HasMaxLength(Payment.ProviderMaxLength).IsRequired();
        builder.Property(e => e.EventType).HasMaxLength(PaymentEvent.EventTypeMaxLength).IsRequired();
        builder.Property(e => e.Reference).HasMaxLength(100).IsRequired();

        // AC-8.4: the same notification is stored once; a repeat violates this index and is acknowledged as a duplicate.
        builder.HasIndex(e => new { e.Provider, e.EventType, e.ProviderTransactionId }).IsUnique();
        builder.HasIndex(e => e.Reference);
    }
}
