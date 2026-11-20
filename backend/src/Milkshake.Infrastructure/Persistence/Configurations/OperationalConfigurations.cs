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
        // Names are unique ignoring letter case: a unique index on lower(name), created with SQL in the
        // CaseInsensitiveNamesAndRowVersions migration (EF Core cannot express expression indexes).
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

        // Append-only (AC-11.3): UPDATE, DELETE and TRUNCATE are refused by triggers created in the
        // AppendOnlyAuditAndActors migration (EF Core cannot express triggers).
        builder.ToTable("audit_logs", table =>
            table.HasCheckConstraint("ck_audit_logs_actor", $"actor IS NULL OR actor ~ '{AuditActors.DatabasePattern}'"));
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(10);
        builder.Property(a => a.TableName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.RecordId).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Changes).HasColumnType("jsonb").IsRequired();
        builder.Property(a => a.UserEmail).HasMaxLength(256);
        builder.Property(a => a.TraceId).HasMaxLength(64);
        builder.Property(a => a.Actor).HasMaxLength(AuditActors.MaxLength);

        // No foreign key to users on purpose: audit history must survive user deletion.
        builder.HasIndex(a => a.OccurredAt);
        builder.HasIndex(a => new { a.TableName, a.RecordId });
        builder.HasIndex(a => a.UserId);

        // Newest-first keyset paging for the audit query (AC-11.4).
        builder.HasIndex(a => new { a.OccurredAt, a.Id }).IsDescending(true, true);
    }
}


internal sealed class SecurityEventConfiguration : IEntityTypeConfiguration<SecurityEvent>
{
    public void Configure(EntityTypeBuilder<SecurityEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Append-only, like audit_logs (triggers in the AppendOnlyAuditAndActors migration).
        builder.ToTable("security_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EventType).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Email).HasMaxLength(SecurityEvent.EmailMaxLength);
        builder.Property(e => e.IpAddress).HasMaxLength(SecurityEvent.IpAddressMaxLength);
        builder.Property(e => e.TraceId).HasMaxLength(SecurityEvent.TraceIdMaxLength);

        // No foreign key to users: the trail must survive account deletion.
        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => new { e.OccurredAt, e.Id }).IsDescending(true, true);
    }
}