using System.Globalization;
using Milkshake.Application.Management;
using Milkshake.Domain.Settings;

namespace Milkshake.Application.Ordering;

/// <summary>The settings ordering depends on, already validated and typed.</summary>
public sealed record OrderingSettings(
    int MinDrinks,
    int MaxDrinks,
    int LeadTimeMinutes,
    int MaxAdvanceDays,
    TimeZoneInfo TimeZone,
    string TimeZoneId,
    string CurrencyCode,
    string DisplayLocale,
    decimal VatRate);

/// <summary>Typed settings, or the reasons they cannot be used (for the server log only).</summary>
public sealed record OrderingSettingsResult(OrderingSettings? Settings, IReadOnlyList<string> Problems)
{
    /// <summary>True when ordering can use the settings.</summary>
    public bool IsValid => Settings is not null;
}

/// <summary>
/// Reads the stored ordering settings with the same rules managers are held to (<see cref="SettingRules"/>).
/// A missing or invalid value makes ordering unavailable rather than falling back to a guessed default.
/// </summary>
public static class OrderingSettingsReader
{
    private static readonly string[] RequiredKeys =
    [
        SettingKeys.MinDrinksPerOrder,
        SettingKeys.MaxDrinksPerOrder,
        SettingKeys.MinPickupLeadTimeMinutes,
        SettingKeys.PickupMaxAdvanceDays,
        SettingKeys.PickupTimeZone,
        SettingKeys.CurrencyCode,
        SettingKeys.DisplayLocale,
        SettingKeys.VatRate,
    ];

    /// <summary>Validates and converts the stored values (key → invariant-culture text).</summary>
    public static OrderingSettingsResult Read(IReadOnlyDictionary<string, string> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        var problems = new List<string>();
        foreach (string key in RequiredKeys)
        {
            if (!stored.ContainsKey(key))
            {
                problems.Add($"Setting '{key}' is missing.");
            }
        }

        if (problems.Count > 0)
        {
            return new OrderingSettingsResult(null, problems);
        }

        string Checked(string key)
        {
            SettingValidation result = SettingRules.Validate(key, stored[key], stored);
            if (!result.IsValid)
            {
                problems.Add($"Setting '{key}' has an invalid value: {result.Error}");
            }

            return result.NormalisedValue ?? string.Empty;
        }

        string min = Checked(SettingKeys.MinDrinksPerOrder);
        string max = Checked(SettingKeys.MaxDrinksPerOrder);
        string lead = Checked(SettingKeys.MinPickupLeadTimeMinutes);
        string days = Checked(SettingKeys.PickupMaxAdvanceDays);
        string zoneId = Checked(SettingKeys.PickupTimeZone);
        string locale = Checked(SettingKeys.DisplayLocale);
        string vatRate = Checked(SettingKeys.VatRate);

        string currency = stored[SettingKeys.CurrencyCode].Trim();
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper))
        {
            problems.Add($"Setting '{SettingKeys.CurrencyCode}' is not a three-letter ISO 4217 code.");
        }

        if (problems.Count > 0)
        {
            return new OrderingSettingsResult(null, problems);
        }

        var settings = new OrderingSettings(
            Whole(min),
            Whole(max),
            Whole(lead),
            Whole(days),
            TimeZoneInfo.FindSystemTimeZoneById(zoneId),
            zoneId,
            currency,
            locale,
            decimal.Parse(vatRate, NumberStyles.Number, CultureInfo.InvariantCulture));

        return new OrderingSettingsResult(settings, problems);
    }

    private static int Whole(string value) => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
}