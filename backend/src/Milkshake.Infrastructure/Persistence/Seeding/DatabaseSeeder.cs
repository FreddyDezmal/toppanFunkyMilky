using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Milkshake.Domain.Catalog;
using Milkshake.Domain.Common;
using Milkshake.Domain.Restaurants;
using Milkshake.Domain.Settings;
using Milkshake.Domain.Users;
using Milkshake.Infrastructure.Identity;
using Milkshake.Application.Abstractions;
using Milkshake.Domain.Auditing;

namespace Milkshake.Infrastructure.Persistence.Seeding;

/// <summary>
/// Inserts missing reference data. Never updates existing rows, so values changed by managers are preserved.
/// Everything is saved in one SaveChanges, so the audit interceptor records it in the same transaction.
/// </summary>
internal sealed partial class DatabaseSeeder(
    IOptions<SeedDataOptions> options,
    TimeProvider timeProvider,
    ILogger<DatabaseSeeder> logger)
{
    private static readonly string[] RoleNames = [Roles.Manager, Roles.Patron];

    /// <summary>Synchronous entry point used by `dotnet ef database update`.</summary>
    public void Seed(MilkshakeDbContext context) =>
        SeedAsync(context, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Asynchronous entry point used by MigrateAsync.</summary>
    public async Task SeedAsync(MilkshakeDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        using IDisposable actor = AuditActorScope.Begin(AuditActors.Seeding);   // M11 audit attribution

        int roles = await SeedRolesAsync(context, cancellationToken);

        SeedDataOptions seed = options.Value;
        if (!seed.Enabled)
        {
            await context.SaveChangesAsync(cancellationToken);
            LogSeedingDisabled(logger, roles);
            return;
        }

        // Validates configuration: a bad currency or locale stops seeding with a clear error.
        string currency = new Money(0m, seed.CurrencyCode).CurrencyCode;
        string locale = CultureInfo.GetCultureInfo(seed.DisplayLocale).Name;
        DateTimeOffset now = timeProvider.GetUtcNow();

        int settings = await SeedSettingsAsync(context, seed.Settings, currency, locale, cancellationToken);
        int flavours = await SeedCatalogAsync(context.Flavours, seed.Flavours, (name, order) => new Flavour(name, order), (item, price) => item.SetPrice(price, now), currency, cancellationToken);
        int toppings = await SeedCatalogAsync(context.Toppings, seed.Toppings, (name, order) => new Topping(name, order), (item, price) => item.SetPrice(price, now), currency, cancellationToken);
        int consistencies = await SeedCatalogAsync(context.Consistencies, seed.Consistencies, (name, order) => new Consistency(name, order), (item, price) => item.SetPrice(price, now), currency, cancellationToken);
        int restaurants = await SeedRestaurantsAsync(context, seed.Restaurants, cancellationToken);
        int tiers = await SeedDiscountTiersAsync(context, seed.DiscountTiers, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        LogSeeded(logger, roles, settings, flavours, toppings, consistencies, restaurants, tiers);
    }

    private static async Task<int> SeedRolesAsync(MilkshakeDbContext context, CancellationToken cancellationToken)
    {
        List<string?> existing = await context.Roles.Select(r => r.NormalizedName).ToListAsync(cancellationToken);
        int added = 0;

        foreach (string roleName in RoleNames)
        {
            string normalized = roleName.ToUpperInvariant();
            if (!existing.Contains(normalized))
            {
                context.Roles.Add(new ApplicationRole(roleName) { NormalizedName = normalized });
                added++;
            }
        }

        return added;
    }

    private static async Task<int> SeedSettingsAsync(
        MilkshakeDbContext context,
        SettingsSeed values,
        string currency,
        string locale,
        CancellationToken cancellationToken)
    {
        var settings = new List<SystemSetting>
        {
            new(SettingKeys.MinDrinksPerOrder, Invariant(values.MinDrinksPerOrder), SettingValueType.WholeNumber, "Minimum number of drinks per order (URS: 1)."),
            new(SettingKeys.MaxDrinksPerOrder, Invariant(values.MaxDrinksPerOrder), SettingValueType.WholeNumber, "Maximum number of drinks per order (URS: 10)."),
            new(SettingKeys.VatRate, Invariant(values.VatRate), SettingValueType.DecimalNumber, "VAT as a fraction (URS: 15% = 0.15)."),
            new(SettingKeys.MaxDiscountAmount, Invariant(values.MaxDiscountAmount), SettingValueType.DecimalNumber, "Maximum frequent-customer discount per order, in the pricing currency."),
            new(SettingKeys.MinPickupLeadTimeMinutes, Invariant(values.MinPickupLeadTimeMinutes), SettingValueType.WholeNumber, "Minimum minutes between ordering and pickup."),
            new(SettingKeys.PickupMaxAdvanceDays, Invariant(values.PickupMaxAdvanceDays), SettingValueType.WholeNumber, "How many days ahead a pickup may be booked (0 = same day only). Provisional (Q-18)."),
            new(SettingKeys.RefundFeeRate, Invariant(values.RefundFeeRate), SettingValueType.DecimalNumber, "Fee kept when a patron cancels a paid order, as a fraction of the VAT-exclusive amount. Provisional (B1.4)."),
            new(SettingKeys.PickupTimeZone, values.PickupTimeZone, SettingValueType.Text, "IANA time zone in which pickup times are chosen and shown. Provisional (Q-17)."),
            new(SettingKeys.CurrencyCode, currency, SettingValueType.Text, "ISO 4217 currency used for new prices and orders."),
            new(SettingKeys.DisplayLocale, locale, SettingValueType.Text, "Locale used to format amounts for display."),
        };

        HashSet<string> existing = [.. await context.SystemSettings.Select(s => s.Key).ToListAsync(cancellationToken)];
        List<SystemSetting> missing = settings.Where(s => !existing.Contains(s.Key)).ToList();
        context.SystemSettings.AddRange(missing);
        return missing.Count;
    }

    private static async Task<int> SeedCatalogAsync<TItem>(
        DbSet<TItem> set,
        IEnumerable<PricedItemSeed> items,
        Func<string, int, TItem> create,
        Action<TItem, Money> setPrice,
        string currency,
        CancellationToken cancellationToken)
        where TItem : CatalogItem
    {
        HashSet<string> existing = new(await set.Select(i => i.Name).ToListAsync(cancellationToken), StringComparer.OrdinalIgnoreCase);
        int displayOrder = 0;
        int added = 0;

        foreach (PricedItemSeed seedItem in items)
        {
            displayOrder++;
            if (existing.Contains(seedItem.Name.Trim()))
            {
                continue;
            }

            TItem item = create(seedItem.Name, displayOrder);
            setPrice(item, new Money(seedItem.Price, currency));
            set.Add(item);
            added++;
        }

        return added;
    }

    private static async Task<int> SeedRestaurantsAsync(MilkshakeDbContext context, IEnumerable<RestaurantSeed> restaurants, CancellationToken cancellationToken)
    {
        HashSet<string> existing = new(await context.Restaurants.Select(r => r.Name).ToListAsync(cancellationToken), StringComparer.OrdinalIgnoreCase);
        List<Restaurant> missing = restaurants
            .Where(r => !existing.Contains(r.Name.Trim()))
            .Select(r => new Restaurant(r.Name, r.Address))
            .ToList();

        context.Restaurants.AddRange(missing);
        return missing.Count;
    }

    private static async Task<int> SeedDiscountTiersAsync(MilkshakeDbContext context, IEnumerable<DiscountTierSeed> tiers, CancellationToken cancellationToken)
    {
        HashSet<int> existing = [.. await context.DiscountTiers.Select(t => t.TierNumber).ToListAsync(cancellationToken)];
        List<DiscountTier> missing = tiers
            .Where(t => !existing.Contains(t.TierNumber))
            .Select(t => new DiscountTier(t.TierNumber, t.MinPreviousOrders, t.MinDrinksPerOrder, t.DiscountRate, t.IsActive))
            .ToList();

        context.DiscountTiers.AddRange(missing);
        return missing.Count;
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Invariant(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reference data seeding is disabled; ensured {Roles} role(s).")]
    private static partial void LogSeedingDisabled(ILogger logger, int roles);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Roles} role(s), {Settings} setting(s), {Flavours} flavour(s), {Toppings} topping(s), {Consistencies} consistency(ies), {Restaurants} restaurant(s), {Tiers} discount tier(s).")]
    private static partial void LogSeeded(ILogger logger, int roles, int settings, int flavours, int toppings, int consistencies, int restaurants, int tiers);
}