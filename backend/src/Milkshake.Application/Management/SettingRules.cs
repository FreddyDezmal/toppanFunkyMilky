using System.Globalization;
using Milkshake.Domain.Settings;

namespace Milkshake.Application.Management;

/// <summary>Outcome of validating a setting value: the value to store, or a user-facing error.</summary>
public sealed record SettingValidation(string? NormalisedValue, string? Error)
{
    /// <summary>True when the value can be stored.</summary>
    public bool IsValid => Error is null;
}

/// <summary>
/// Business limits for management-editable settings (M10 decision D6, M5 provisional pickup settings).
/// URS values: 1 to 10 drinks, VAT 15%. The upper limits for maximum drinks (50), lead time (240 minutes) and
/// advance booking (30 days) are project recommendations.
/// </summary>
public static class SettingRules
{
    /// <summary>Highest allowed "maximum drinks per order" [project recommendation, D6].</summary>
    public const int MaxDrinksUpperLimit = 50;

    /// <summary>Highest allowed minimum pickup lead time in minutes [project recommendation, D6].</summary>
    public const int MaxLeadTimeMinutes = 240;

    /// <summary>Highest allowed advance booking in days [project recommendation, M5].</summary>
    public const int MaxAdvanceDaysLimit = 30;

    /// <summary>Shown when someone tries to change the currency.</summary>
    public const string CurrencyReadOnlyMessage =
        "The pricing currency cannot be changed here: every price would need to be re-entered in the new currency.";

    private static readonly string[] Order =
    [
        SettingKeys.MinDrinksPerOrder,
        SettingKeys.MaxDrinksPerOrder,
        SettingKeys.VatRate,
        SettingKeys.MaxDiscountAmount,
        SettingKeys.MinPickupLeadTimeMinutes,
        SettingKeys.PickupMaxAdvanceDays,
        SettingKeys.PickupTimeZone,
        SettingKeys.DisplayLocale,
        SettingKeys.CurrencyCode,
    ];

    /// <summary>Position on the management screen; unknown keys last.</summary>
    public static int SortIndex(string key)
    {
        int index = Array.IndexOf(Order, key);
        return index < 0 ? int.MaxValue : index;
    }

    /// <summary>False for settings managers may view but not change.</summary>
    public static bool IsEditable(string key) => key != SettingKeys.CurrencyCode && Array.IndexOf(Order, key) >= 0;

    /// <summary>The setting validated against this one (min and max drinks), or null.</summary>
    public static string? PartnerOf(string key) => key switch
    {
        SettingKeys.MinDrinksPerOrder => SettingKeys.MaxDrinksPerOrder,
        SettingKeys.MaxDrinksPerOrder => SettingKeys.MinDrinksPerOrder,
        _ => null,
    };

    /// <summary>Guidance shown next to the input.</summary>
    public static string Hint(string key) => key switch
    {
        SettingKeys.MinDrinksPerOrder => "Whole number, at least 1 and no more than the maximum drinks per order. URS value: 1.",
        SettingKeys.MaxDrinksPerOrder => $"Whole number from the minimum drinks per order up to {MaxDrinksUpperLimit}. URS value: 10.",
        SettingKeys.VatRate => "Fraction from 0 to 1, e.g. 0.15 for 15%. URS value: 15%.",
        SettingKeys.MaxDiscountAmount => "Amount in the pricing currency, 0 or more, at most two decimal places. Meaning pending confirmation (open question Q-02).",
        SettingKeys.MinPickupLeadTimeMinutes => $"Whole number of minutes from 0 to {MaxLeadTimeMinutes}. Pickup rules pending confirmation (open question Q-06).",
        SettingKeys.PickupMaxAdvanceDays => $"Whole number of days from 0 (same day only) to {MaxAdvanceDaysLimit}. Provisional, pending confirmation (open question Q-18).",
        SettingKeys.PickupTimeZone => "IANA time zone name, e.g. Africa/Johannesburg. Pickup times are chosen and shown in this zone. Provisional (open question Q-17).",
        SettingKeys.DisplayLocale => "Locale used to format prices, e.g. en-ZA.",
        SettingKeys.CurrencyCode => "Read-only. Changing the currency would require re-entering every price.",
        _ => string.Empty,
    };

    /// <summary>
    /// Validates a new value for <paramref name="key"/>. <paramref name="current"/> holds the stored values of all
    /// settings, used for cross-checks such as minimum drinks ≤ maximum drinks.
    /// </summary>
    public static SettingValidation Validate(string key, string? value, IReadOnlyDictionary<string, string> current)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (string.IsNullOrWhiteSpace(value))
        {
            return Fail("A value is required.");
        }

        string trimmed = value.Trim();

        switch (key)
        {
            case SettingKeys.MinDrinksPerOrder:
            {
                if (!TryWhole(trimmed, out int min))
                {
                    return Fail("Enter a whole number.");
                }

                if (min < 1)
                {
                    return Fail("Minimum drinks per order must be at least 1.");
                }

                if (TryCurrentWhole(current, SettingKeys.MaxDrinksPerOrder, out int max) && min > max)
                {
                    return Fail($"Minimum drinks per order cannot be more than the maximum ({max}).");
                }

                return Ok(min.ToString(CultureInfo.InvariantCulture));
            }

            case SettingKeys.MaxDrinksPerOrder:
            {
                if (!TryWhole(trimmed, out int max))
                {
                    return Fail("Enter a whole number.");
                }

                if (max > MaxDrinksUpperLimit)
                {
                    return Fail($"Maximum drinks per order cannot be more than {MaxDrinksUpperLimit}.");
                }

                int min = TryCurrentWhole(current, SettingKeys.MinDrinksPerOrder, out int storedMin) ? storedMin : 1;
                if (max < min)
                {
                    return Fail($"Maximum drinks per order cannot be less than the minimum ({min}).");
                }

                return Ok(max.ToString(CultureInfo.InvariantCulture));
            }

            case SettingKeys.VatRate:
            {
                if (!TryDecimal(trimmed, out decimal rate))
                {
                    return Fail("Enter a number such as 0.15.");
                }

                if (rate < 0m || rate > 1m)
                {
                    return Fail("VAT rate must be between 0 and 1 (0% to 100%), e.g. 0.15 for 15%.");
                }

                return Ok(rate.ToString(CultureInfo.InvariantCulture));
            }

            case SettingKeys.MaxDiscountAmount:
            {
                if (!TryDecimal(trimmed, out decimal amount))
                {
                    return Fail("Enter an amount such as 50.00.");
                }

                var errors = new Dictionary<string, string[]>();
                ManagementValidation.Amount(errors, ManagementFields.Value, amount);
                return errors.Count > 0
                    ? Fail(errors[ManagementFields.Value][0].Replace("Price", "Amount", StringComparison.Ordinal))
                    : Ok(amount.ToString(CultureInfo.InvariantCulture));
            }

            case SettingKeys.MinPickupLeadTimeMinutes:
            {
                if (!TryWhole(trimmed, out int minutes))
                {
                    return Fail("Enter a whole number of minutes.");
                }

                if (minutes < 0 || minutes > MaxLeadTimeMinutes)
                {
                    return Fail($"Lead time must be between 0 and {MaxLeadTimeMinutes} minutes.");
                }

                return Ok(minutes.ToString(CultureInfo.InvariantCulture));
            }

            case SettingKeys.PickupMaxAdvanceDays:
            {
                if (!TryWhole(trimmed, out int days))
                {
                    return Fail("Enter a whole number of days.");
                }

                if (days < 0 || days > MaxAdvanceDaysLimit)
                {
                    return Fail($"Advance booking must be between 0 and {MaxAdvanceDaysLimit} days.");
                }

                return Ok(days.ToString(CultureInfo.InvariantCulture));
            }

            case SettingKeys.PickupTimeZone:
            {
                // IANA names only (e.g. Africa/Johannesburg): they work on Windows and Linux servers alike.
                bool isIana = TimeZoneInfo.TryConvertIanaIdToWindowsId(trimmed, out _);
                return isIana && TimeZoneInfo.TryFindSystemTimeZoneById(trimmed, out _)
                    ? Ok(trimmed)
                    : Fail("Enter an IANA time zone name such as Africa/Johannesburg.");
            }

            case SettingKeys.DisplayLocale:
            {
                try
                {
                    CultureInfo culture = CultureInfo.GetCultureInfo(trimmed, predefinedOnly: true);
                    return string.IsNullOrEmpty(culture.Name)
                        ? Fail("Enter a locale such as en-ZA.")
                        : Ok(culture.Name);
                }
                catch (CultureNotFoundException)
                {
                    return Fail("Enter a valid locale such as en-ZA.");
                }
            }

            case SettingKeys.CurrencyCode:
                return Fail(CurrencyReadOnlyMessage);

            default:
                return Fail("This setting cannot be edited.");
        }
    }

    private static bool TryWhole(string value, out int result) =>
        int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);

    private static bool TryDecimal(string value, out decimal result) =>
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result);

    private static bool TryCurrentWhole(IReadOnlyDictionary<string, string> current, string key, out int result)
    {
        result = 0;
        return current.TryGetValue(key, out string? stored) && TryWhole(stored, out result);
    }

    private static SettingValidation Ok(string value) => new(value, null);

    private static SettingValidation Fail(string error) => new(null, error);
}