using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Milkshake.Domain.Auditing;
using Milkshake.Domain.Restaurants;
using Milkshake.Domain.Settings;

namespace Milkshake.Infrastructure.Persistence.Configurations;

internal sealed class RestaurantConfiguration : IEntityTypeConfiguration<Restaurant>
{
    public void Configure(EntityTypeBuilder<Restaurant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("restaurants");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(Restaurant.NameMaxLength).IsRequired();
        builder.Property(r => r.Address).HasMaxLength(Restaurant.AddressMaxLength);
        builder.HasIndex(r => r.Name).IsUnique();
    }
}

internal sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("system_settings");
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(SystemSetting.KeyMaxLength);
        builder.Property(s => s.Value).HasMaxLength(SystemSetting.ValueMaxLength).IsRequired();
        builder.Property(s => s.ValueType).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Description).HasMaxLength(SystemSetting.DescriptionMaxLength);
    }
}

internal sealed class DiscountTierConfiguration : IEntityTypeConfiguration<DiscountTier>
{
    public void Configure(EntityTypeBuilder<DiscountTier> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("discount_tiers", table =>
        {
            table.HasCheckConstraint("ck_discount_tiers_tier_number", $"tier_number BETWEEN 1 AND {DiscountTier.MaxTierNumber}");
            table.HasCheckConstraint("ck_discount_tiers_discount_rate", "discount_rate >= 0 AND discount_rate <= 1");
            table.HasCheckConstraint("ck_discount_tiers_thresholds", "min_previous_orders >= 0 AND min_drinks_per_order >= 1");
        });

        builder.HasKey(t => t.Id);
        builder.Property(t => t.DiscountRate).HasPrecision(5, 4);
        builder.HasIndex(t => t.TierNumber).IsUnique();
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("audit_logs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(10);
        builder.Property(a => a.TableName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.RecordId).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Changes).HasColumnType("jsonb").IsRequired();
        builder.Property(a => a.UserEmail).HasMaxLength(256);
        builder.Property(a => a.TraceId).HasMaxLength(64);

        // No foreign key to users on purpose: audit history must survive user deletion.
        builder.HasIndex(a => a.OccurredAt);
        builder.HasIndex(a => new { a.TableName, a.RecordId });
        builder.HasIndex(a => a.UserId);
    }
}