namespace Milkshake.Infrastructure.Persistence.Seeding;

/// <summary>
/// Reference data inserted when missing. Bound from the "SeedData" configuration section,
/// so names, prices, currency and locale are configuration, not code.
/// </summary>
public sealed class SeedDataOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "SeedData";

    /// <summary>Seed reference data (roles are always ensured).</summary>
    public bool Enabled { get; set; }

    /// <summary>ISO 4217 currency for seeded prices, e.g. ZAR.</summary>
    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>Locale used to format amounts for display, e.g. en-ZA.</summary>
    public string DisplayLocale { get; set; } = string.Empty;

    /// <summary>Flavours in display order.</summary>
    public IList<PricedItemSeed> Flavours { get; } = new List<PricedItemSeed>();

    /// <summary>Toppings in display order.</summary>
    public IList<PricedItemSeed> Toppings { get; } = new List<PricedItemSeed>();

    /// <summary>Consistencies in display order.</summary>
    public IList<PricedItemSeed> Consistencies { get; } = new List<PricedItemSeed>();

    /// <summary>Pickup restaurants.</summary>
    public IList<RestaurantSeed> Restaurants { get; } = new List<RestaurantSeed>();

    /// <summary>Discount tiers (seed inactive until the business confirms the rules).</summary>
    public IList<DiscountTierSeed> DiscountTiers { get; } = new List<DiscountTierSeed>();

    /// <summary>Order and pricing settings.</summary>
    public SettingsSeed Settings { get; set; } = new();
}

/// <summary>A named item with an initial price.</summary>
public sealed class PricedItemSeed
{
    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Initial price in the configured currency.</summary>
    public decimal Price { get; set; }
}

/// <summary>A pickup restaurant.</summary>
public sealed class RestaurantSeed
{
    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Street address.</summary>
    public string? Address { get; set; }
}

/// <summary>A frequent-customer discount tier.</summary>
public sealed class DiscountTierSeed
{
    /// <summary>1, 2 or 3.</summary>
    public int TierNumber { get; set; }

    /// <summary>Paid orders needed before the tier applies.</summary>
    public int MinPreviousOrders { get; set; }

    /// <summary>Drinks an order needs to count.</summary>
    public int MinDrinksPerOrder { get; set; }

    /// <summary>Discount as a fraction, e.g. 0.05.</summary>
    public decimal DiscountRate { get; set; }

    /// <summary>Whether the tier is in effect.</summary>
    public bool IsActive { get; set; }
}

/// <summary>Initial values for management-configurable settings.</summary>
public sealed class SettingsSeed
{
    /// <summary>Minimum drinks per order.</summary>
    public int MinDrinksPerOrder { get; set; }

    /// <summary>Maximum drinks per order.</summary>
    public int MaxDrinksPerOrder { get; set; }

    /// <summary>VAT as a fraction, e.g. 0.15.</summary>
    public decimal VatRate { get; set; }

    /// <summary>Maximum frequent-customer discount per order.</summary>
    public decimal MaxDiscountAmount { get; set; }

    /// <summary>Minimum minutes between ordering and pickup.</summary>
    public int MinPickupLeadTimeMinutes { get; set; }

    
    /// <summary>IANA time zone for pickup times, e.g. Africa/Johannesburg [provisional, Q-17].</summary>
    public string PickupTimeZone { get; set; } = string.Empty;

    /// <summary>Days ahead a pickup may be booked; 0 = same day only [provisional, Q-18].</summary>
    public int PickupMaxAdvanceDays { get; set; }
}