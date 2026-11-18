using System.Globalization;
using Milkshake.Domain.Common;

namespace Milkshake.Domain.Settings;

/// <summary>How a setting's text value must be interpreted and validated.</summary>
public enum SettingValueType
{
    /// <summary>Free text, e.g. a currency code.</summary>
    Text,

    /// <summary>A whole number, e.g. maximum drinks.</summary>
    WholeNumber,

    /// <summary>A decimal number, e.g. a VAT rate of 0.15.</summary>
    DecimalNumber,

    /// <summary>true or false.</summary>
    YesNo,
}

/// <summary>
/// A management-configurable value (URS §3.4.6), e.g. maximum drinks per order or the VAT rate.
/// Values are stored as invariant-culture text and validated against <see cref="ValueType"/>.
/// </summary>
public sealed class SystemSetting : IHasTimestamps, IHasRowVersion
{
    /// <summary>Maximum key length.</summary>
    public const int KeyMaxLength = 100;

    /// <summary>Maximum value length.</summary>
    public const int ValueMaxLength = 500;

    /// <summary>Maximum description length.</summary>
    public const int DescriptionMaxLength = 500;

    private SystemSetting()
    {
    }

    /// <summary>Creates a setting with a validated value.</summary>
    public SystemSetting(string key, string value, SettingValueType valueType, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > KeyMaxLength)
        {
            throw new ArgumentException($"Key cannot exceed {KeyMaxLength} characters.", nameof(key));
        }

        Key = key;
        ValueType = valueType;
        Description = description;
        ChangeValue(value);
    }

    /// <summary>Unique key, e.g. "Orders.MaxDrinksPerOrder" (see <see cref="SettingKeys"/>).</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>Invariant-culture text value, e.g. "10" or "0.15".</summary>
    public string Value { get; private set; } = string.Empty;

    /// <summary>Type used to validate <see cref="Value"/>.</summary>
    public SettingValueType ValueType { get; private set; }

    /// <summary>Explanation shown to managers.</summary>
    public string? Description { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAt { get; set; }
    
    /// <inheritdoc />
    public uint Version { get; private set; }

    /// <summary>Changes the value after validating it against the setting's type.</summary>
    public void ChangeValue(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string trimmed = value.Trim();

        if (trimmed.Length > ValueMaxLength)
        {
            throw new ArgumentException($"Value cannot exceed {ValueMaxLength} characters.", nameof(value));
        }

        bool valid = ValueType switch
        {
            SettingValueType.WholeNumber => int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            SettingValueType.DecimalNumber => decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
            SettingValueType.YesNo => bool.TryParse(trimmed, out _),
            _ => true,
        };

        if (!valid)
        {
            throw new ArgumentException($"'{trimmed}' is not a valid {ValueType} value for setting '{Key}'.", nameof(value));
        }

        Value = trimmed;
    }
}