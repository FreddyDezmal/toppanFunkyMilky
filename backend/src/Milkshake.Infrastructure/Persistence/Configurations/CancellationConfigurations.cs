using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Milkshake.Domain.Common;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;
using Milkshake.Infrastructure.Identity;

namespace Milkshake.Infrastructure.Persistence.Configurations;

/// <summary>
/// Refunds (M8b, decisions R3–R5). One refund per payment is enforced by a unique index, so a repeated cancellation or
/// notification can never return money twice. The checks repeat the domain's arithmetic: refund = paid − fee, and only
/// a patron's own cancellation carries a fee (Q1, Q2). Foreign keys restrict deletion: refunds are financial evidence.
/// </summary>
internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string statuses = string.Join(", ", Enum.GetNames<RefundStatus>().Select(name => $"'{name}'"));
        string kinds = string.Join(", ", Enum.GetNames<RefundKind>().Select(name => $"'{name}'"));

        builder.ToTable("refunds", table =>
        {
            table.HasCheckConstraint("ck_refunds_status", $"status IN ({statuses})");
            table.HasCheckConstraint("ck_refunds_kind", $"kind IN ({kinds})");
            table.HasCheckConstraint("ck_refunds_reference", $"reference ~ '{Refund.ReferencePattern}'");
            table.HasCheckConstraint("ck_refunds_currency", "currency_code ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint("ck_refunds_amounts",
                "paid_amount_minor > 0 AND fee_minor >= 0 AND amount_minor > 0 AND amount_minor = paid_amount_minor - fee_minor");
            table.HasCheckConstraint("ck_refunds_fee_rate",
                $"fee_rate >= 0 AND fee_rate <= {Refund.MaxFeeRate.ToString(System.Globalization.CultureInfo.InvariantCulture)} AND " +
                "(kind = 'PatronCancellation' OR (fee_rate = 0 AND fee_minor = 0))");
            table.HasCheckConstraint("ck_refunds_provider_id", "status <> 'Pending' OR provider_refund_id IS NOT NULL");
            table.HasCheckConstraint("ck_refunds_completed",
                "(status IN ('Processed', 'Failed')) = (completed_at IS NOT NULL)");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Reference).HasMaxLength(Payment.ReferenceLength).IsFixedLength().IsRequired();
        builder.Property(r => r.Provider).HasMaxLength(Payment.ProviderMaxLength).IsRequired();
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.FeeRate).HasPrecision(5, 4);
        builder.Property(r => r.CurrencyCode).HasMaxLength(Money.CurrencyCodeLength).IsFixedLength().IsRequired();
        builder.Property(r => r.ProviderMessage).HasMaxLength(Payment.ProviderMessageMaxLength);

        builder.HasOne<Order>().WithMany().HasForeignKey(r => r.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Payment>().WithMany().HasForeignKey(r => r.PaymentId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.Reference).IsUnique();

        // R3: at most one refund per payment, however many requests or notifications arrive.
        builder.HasIndex(r => r.PaymentId, "ux_refunds_one_per_payment").IsUnique().HasDatabaseName("ux_refunds_one_per_payment");

        // A provider refund belongs to one row; the order page lists an order's refunds; the job finds open refunds by age.
        builder.HasIndex(r => new { r.Provider, r.ProviderRefundId }).IsUnique().HasFilter("provider_refund_id IS NOT NULL");
        builder.HasIndex(r => r.OrderId);
        builder.HasIndex(r => new { r.Status, r.CreatedAt });
    }
}

/// <summary>
/// Manager cancellation requests (M8b, decisions R2, R7). The partial unique index allows at most one pending request
/// per order; answered and lapsed requests stay as history.
/// </summary>
internal sealed class CancellationRequestConfiguration : IEntityTypeConfiguration<CancellationRequest>
{
    public void Configure(EntityTypeBuilder<CancellationRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string statuses = string.Join(", ", Enum.GetNames<CancellationRequestStatus>().Select(name => $"'{name}'"));

        builder.ToTable("cancellation_requests", table =>
        {
            table.HasCheckConstraint("ck_cancellation_requests_status", $"status IN ({statuses})");
            table.HasCheckConstraint("ck_cancellation_requests_reason",
                $"char_length(reason) BETWEEN 1 AND {CancellationRequest.ReasonMaxLength} AND reason = btrim(reason) AND reason !~ '[[:cntrl:]]'");
            table.HasCheckConstraint("ck_cancellation_requests_closed", "(status = 'Pending') = (closed_at IS NULL)");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Reason).HasMaxLength(CancellationRequest.ReasonMaxLength).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Order>().WithMany().HasForeignKey(r => r.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.RequestedBy).OnDelete(DeleteBehavior.Restrict);

        // R2: at most one pending request per order.
        builder.HasIndex(r => r.OrderId, "ux_cancellation_requests_one_pending_per_order").IsUnique().HasFilter("status = 'Pending'")
            .HasDatabaseName("ux_cancellation_requests_one_pending_per_order");
        builder.HasIndex(r => r.OrderId);
        builder.HasIndex(r => r.RequestedBy);
        builder.HasIndex(r => r.Status);
    }
}
