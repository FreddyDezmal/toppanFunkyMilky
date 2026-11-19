namespace Milkshake.Domain.Settings;

/// <summary>Keys of the management-configurable settings.</summary>
public static class SettingKeys
{
    /// <summary>Minimum drinks per order (URS: currently 1).</summary>
    public const string MinDrinksPerOrder = "Orders.MinDrinksPerOrder";

    /// <summary>Maximum drinks per order (URS: currently 10).</summary>
    public const string MaxDrinksPerOrder = "Orders.MaxDrinksPerOrder";

    /// <summary>VAT rate as a fraction (URS: currently 15% = 0.15).</summary>
    public const string VatRate = "Tax.VatRate";

    /// <summary>Maximum frequent-customer discount amount per order (URS §3.4.5).</summary>
    public const string MaxDiscountAmount = "Discounts.MaxDiscountAmount";

    /// <summary>Minimum minutes between ordering and pickup [recommendation, Q-06].</summary>
    public const string MinPickupLeadTimeMinutes = "Pickup.MinLeadTimeMinutes";

    /// <summary>ISO 4217 currency for new prices and orders, e.g. ZAR.</summary>
    public const string CurrencyCode = "Pricing.CurrencyCode";

    /// <summary>Locale used to format amounts for display, e.g. en-ZA.</summary>
    public const string DisplayLocale = "Pricing.DisplayLocale";

    
    /// <summary>IANA time zone in which pickup times are chosen and shown [provisional, Q-17].</summary>
    public const string PickupTimeZone = "Pickup.TimeZone";

    /// <summary>How many days ahead a pickup may be booked; 0 = same day only [provisional, Q-18].</summary>
    public const string PickupMaxAdvanceDays = "Pickup.MaxAdvanceDays";
}