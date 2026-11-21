using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Milkshake.Domain.Common;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Milkshake.Domain.Auditing;
using Milkshake.Domain.Catalog;
using Milkshake.Domain.Notifications;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;
using Milkshake.Domain.Restaurants;
using Milkshake.Domain.Settings;
using Milkshake.Infrastructure.Identity;

namespace Milkshake.Infrastructure.Persistence;

/// <summary>
/// EF Core unit of work for the Milkshake database (Supabase PostgreSQL), including ASP.NET Core Identity.
/// Entity mappings live in the Configurations folder and are discovered automatically.
/// </summary>
public sealed class MilkshakeDbContext(DbContextOptions<MilkshakeDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IDataProtectionKeyContext
{
    /// <summary>PostgreSQL schema holding all application tables, separate from Supabase-managed schemas.</summary>
    public const string Schema = "milkshake";

    /// <summary>Pickup locations.</summary>
    public DbSet<Restaurant> Restaurants => Set<Restaurant>();

    /// <summary>Flavours.</summary>
    public DbSet<Flavour> Flavours => Set<Flavour>();

    /// <summary>Flavour price history.</summary>
    public DbSet<FlavourPrice> FlavourPrices => Set<FlavourPrice>();

    /// <summary>Toppings.</summary>
    public DbSet<Topping> Toppings => Set<Topping>();

    /// <summary>Topping price history.</summary>
    public DbSet<ToppingPrice> ToppingPrices => Set<ToppingPrice>();

    /// <summary>Consistencies.</summary>
    public DbSet<Consistency> Consistencies => Set<Consistency>();

    /// <summary>Consistency price history.</summary>
    public DbSet<ConsistencyPrice> ConsistencyPrices => Set<ConsistencyPrice>();

    /// <summary>Management-configurable settings.</summary>
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    /// <summary>Frequent-customer discount tiers.</summary>
    public DbSet<DiscountTier> DiscountTiers => Set<DiscountTier>();

    /// <summary>Audit trail.</summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    
    /// <summary>Sign-in, sign-out and provisioning events.</summary>
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();

    public DbSet<Refund> Refunds => Set<Refund>();

    public DbSet<CancellationRequest> CancellationRequests => Set<CancellationRequest>();

    public DbSet<EmailNotification> EmailNotifications => Set<EmailNotification>();

    /// <summary>
    /// ASP.NET Core Data Protection key ring (M14 decision D-3): protects session and anti-forgery cookies, so sign-ins
    /// survive restarts. Encrypted with a certificate in production; never audited (the audit trail must not hold key
    /// material). Used only when <c>DataProtection:StoreKeysInDatabase</c> is true.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Identity's own mappings first; our configurations then rename its tables.
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(Schema);

        // Keys are reserved from a sequence before insert, so audit records can reference them in the same transaction.
        // The provider's default sequence name is used deliberately: a custom name is not written into the
        // migration snapshot per column, which makes EF report false "pending model changes".
        builder.UseHiLo();

        builder.ApplyConfigurationsFromAssembly(typeof(MilkshakeDbContext).Assembly);

        // Optimistic concurrency: every IHasRowVersion entity's Version is PostgreSQL's built-in xmin system column.
        // Every row already has it (no column is created) and it changes on every update, so EF Core adds
        // "WHERE xmin = <version read>" to updates and reports a conflict when someone else saved first.
        foreach (Type entityType in builder.Model.GetEntityTypes()
                     .Select(type => type.ClrType)
                     .Where(type => typeof(IHasRowVersion).IsAssignableFrom(type))
                     .ToList())
        {
            builder.Entity(entityType)
                .Property(nameof(IHasRowVersion.Version))
                .IsRowVersion()
                .HasColumnName("xmin")
                .HasColumnType("xid");
        }
    }
}