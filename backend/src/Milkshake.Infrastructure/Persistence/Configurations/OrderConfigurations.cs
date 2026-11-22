using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Milkshake.Domain.Catalog;
using Milkshake.Domain.Common;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Restaurants;
using Milkshake.Infrastructure.Identity;

namespace Milkshake.Infrastructure.Persistence.Configurations;

/// <summary>
/// Orders (M7, decision O8). Every amount and name is a snapshot taken at placement; the check constraints repeat the
/// domain's arithmetic so no write path can store an order whose amounts do not add up. Foreign keys restrict
/// deletion, so order history can never be removed by deleting a patron, restaurant or catalogue entry.
/// </summary>
internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    private const int MoneyPrecision = 12;

    public void Configure(EntityTypeBuilder<Order> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string statuses = string.Join(", ", Enum.GetNames<OrderStatus>().Select(name => $"'{name}'"));

        builder.ToTable("orders", table =>
        {
            table.HasCheckConstraint("ck_orders_status", $"status IN ({statuses})");
            table.HasCheckConstraint("ck_orders_reference", $"reference ~ '{OrderReference.Pattern}'");
            table.HasCheckConstraint("ck_orders_amounts_non_negative",
                "subtotal >= 0 AND discount_amount >= 0 AND taxable >= 0 AND vat >= 0 AND total >= 0");
            table.HasCheckConstraint("ck_orders_discount_within_subtotal", "discount_amount <= subtotal");
            table.HasCheckConstraint("ck_orders_taxable", "taxable = subtotal - discount_amount");
            table.HasCheckConstraint("ck_orders_total", "total = taxable + vat");
            table.HasCheckConstraint("ck_orders_vat_rate", "vat_rate >= 0 AND vat_rate <= 1");
            table.HasCheckConstraint("ck_orders_discount_rate",
                "(discount_tier_number IS NULL AND discount_rate IS NULL) OR " +
                "(discount_tier_number >= 1 AND discount_rate >= 0 AND discount_rate <= 1)");
            table.HasCheckConstraint("ck_orders_request_fingerprint", "request_fingerprint ~ '^[0-9a-f]{64}$'");
        });

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Reference).HasMaxLength(OrderReference.Length).IsFixedLength().IsRequired();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.RestaurantName).HasMaxLength(Order.RestaurantNameMaxLength).IsRequired();
        builder.Property(o => o.RestaurantAddress).HasMaxLength(Order.RestaurantAddressMaxLength);
        builder.Property(o => o.PickupTimeZone).HasMaxLength(Order.TimeZoneMaxLength).IsRequired();
        builder.Property(o => o.CurrencyCode).HasMaxLength(Money.CurrencyCodeLength).IsFixedLength().IsRequired();
        builder.Property(o => o.Subtotal).HasPrecision(MoneyPrecision, Money.AmountScale);
        builder.Property(o => o.DiscountAmount).HasPrecision(MoneyPrecision, Money.AmountScale);
        builder.Property(o => o.DiscountRate).HasPrecision(5, 4);
        builder.Property(o => o.Taxable).HasPrecision(MoneyPrecision, Money.AmountScale);
        builder.Property(o => o.VatRate).HasPrecision(5, 4);
        builder.Property(o => o.Vat).HasPrecision(MoneyPrecision, Money.AmountScale);
        builder.Property(o => o.Total).HasPrecision(MoneyPrecision, Money.AmountScale);
        builder.Property(o => o.RequestFingerprint).HasMaxLength(Order.FingerprintLength).IsFixedLength().IsRequired();

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(o => o.PatronId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Restaurant>().WithMany().HasForeignKey(o => o.RestaurantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(o => o.Reference).IsUnique();

        // Idempotency (decision O5): the database, not a prior lookup, guarantees one order per patron and key.
        builder.HasIndex(o => new { o.PatronId, o.IdempotencyKey }).IsUnique();

        // A patron's orders, newest first (order history), and by status (discount eligibility, Q-03).
        builder.HasIndex(o => new { o.PatronId, o.CreatedAt });
        builder.HasIndex(o => new { o.PatronId, o.Status });

        // Management reports (M12): every order in a period, newest first, across all patrons.
        builder.HasIndex(o => new { o.CreatedAt, o.Id });
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    private const int MoneyPrecision = 12;

    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("order_items", table =>
        {
            table.HasCheckConstraint("ck_order_items_number", "number >= 1");
            table.HasCheckConstraint("ck_order_items_fees_non_negative",
                "flavour_fee >= 0 AND consistency_fee >= 0 AND topping_fee >= 0");
            table.HasCheckConstraint("ck_order_items_price", "price = flavour_fee + consistency_fee + topping_fee");
        });

        builder.HasKey(i => i.Id);
        builder.Property(i => i.FlavourName).HasMaxLength(OrderItem.NameMaxLength).IsRequired();
        builder.Property(i => i.ConsistencyName).HasMaxLength(OrderItem.NameMaxLength).IsRequired();
        builder.Property(i => i.ToppingName).HasMaxLength(OrderItem.NameMaxLength).IsRequired();
        builder.Property(i => i.FlavourFee).HasPrecision(MoneyPrecision, Money.AmountScale);
        builder.Property(i => i.ConsistencyFee).HasPrecision(MoneyPrecision, Money.AmountScale);
        builder.Property(i => i.ToppingFee).HasPrecision(MoneyPrecision, Money.AmountScale);
        builder.Property(i => i.Price).HasPrecision(MoneyPrecision, Money.AmountScale);

        builder.HasOne<Flavour>().WithMany().HasForeignKey(i => i.FlavourId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Consistency>().WithMany().HasForeignKey(i => i.ConsistencyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Topping>().WithMany().HasForeignKey(i => i.ToppingId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => new { i.OrderId, i.Number }).IsUnique();
    }
}
