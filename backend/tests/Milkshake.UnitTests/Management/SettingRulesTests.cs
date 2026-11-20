using Milkshake.Application.Management;
using Milkshake.Domain.Settings;

namespace Milkshake.UnitTests.Management;

public sealed class SettingRulesTests
{
    private static readonly Dictionary<string, string> Current = new()
    {
        [SettingKeys.MinDrinksPerOrder] = "1",
        [SettingKeys.MaxDrinksPerOrder] = "10",
    };

    [Theory]
    [InlineData(SettingKeys.MinDrinksPerOrder, "0")]
    [InlineData(SettingKeys.MinDrinksPerOrder, "11")]     // above the current maximum (10)
    [InlineData(SettingKeys.MinDrinksPerOrder, "1.5")]
    [InlineData(SettingKeys.MaxDrinksPerOrder, "51")]
    [InlineData(SettingKeys.MaxDrinksPerOrder, "0")]      // below the current minimum (1)
    [InlineData(SettingKeys.VatRate, "1.01")]
    [InlineData(SettingKeys.VatRate, "-0.01")]
    [InlineData(SettingKeys.VatRate, "15%")]
    [InlineData(SettingKeys.VatRate, " ")]
    [InlineData(SettingKeys.MaxDiscountAmount, "-1")]
    [InlineData(SettingKeys.MaxDiscountAmount, "10.555")]
    [InlineData(SettingKeys.MinPickupLeadTimeMinutes, "-1")]
    [InlineData(SettingKeys.MinPickupLeadTimeMinutes, "241")]
    [InlineData(SettingKeys.DisplayLocale, "not a locale")]
    [InlineData(SettingKeys.CurrencyCode, "USD")]
    [InlineData(SettingKeys.PickupTimeZone, "Mars/Olympus_Mons")]
    [InlineData(SettingKeys.PickupTimeZone, "South Africa Standard Time")]   // Windows name, not IANA
    [InlineData(SettingKeys.PickupMaxAdvanceDays, "31")]
    [InlineData(SettingKeys.RefundFeeRate, "0.51")]
    [InlineData(SettingKeys.RefundFeeRate, "-0.01")]
    [InlineData(SettingKeys.RefundFeeRate, "0.12345")]
    [InlineData(SettingKeys.RefundFeeRate, "ten")]
    [InlineData(SettingKeys.PickupMaxAdvanceDays, "-1")]
    [InlineData("Unknown.Key", "1")]
    public void Validate_RejectsInvalidValues(string key, string value)
    {
        SettingValidation result = SettingRules.Validate(key, value, Current);

        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Theory]
    [InlineData(SettingKeys.MinDrinksPerOrder, "1", "1")]
    [InlineData(SettingKeys.MinDrinksPerOrder, "10", "10")]
    [InlineData(SettingKeys.MaxDrinksPerOrder, "50", "50")]
    [InlineData(SettingKeys.MaxDrinksPerOrder, " 12 ", "12")]
    [InlineData(SettingKeys.VatRate, "0", "0")]
    [InlineData(SettingKeys.VatRate, "0.15", "0.15")]
    [InlineData(SettingKeys.VatRate, "1", "1")]
    [InlineData(SettingKeys.MaxDiscountAmount, "0", "0")]
    [InlineData(SettingKeys.MaxDiscountAmount, "50.00", "50.00")]
    [InlineData(SettingKeys.MinPickupLeadTimeMinutes, "0", "0")]
    [InlineData(SettingKeys.MinPickupLeadTimeMinutes, "240", "240")]
    [InlineData(SettingKeys.PickupTimeZone, "Africa/Johannesburg", "Africa/Johannesburg")]
    [InlineData(SettingKeys.PickupMaxAdvanceDays, "0", "0")]
    [InlineData(SettingKeys.PickupMaxAdvanceDays, "30", "30")]
    [InlineData(SettingKeys.RefundFeeRate, "0.10", "0.10")]
    [InlineData(SettingKeys.RefundFeeRate, " 0 ", "0")]
    [InlineData(SettingKeys.RefundFeeRate, "0.5", "0.5")]
    [InlineData(SettingKeys.DisplayLocale, "en-ZA", "en-ZA")]
    [InlineData(SettingKeys.DisplayLocale, "en-za", "en-ZA")]
    public void Validate_AcceptsAndNormalisesValidValues(string key, string value, string expected)
    {
        SettingValidation result = SettingRules.Validate(key, value, Current);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(expected, result.NormalisedValue);
    }

    [Fact]
    public void CurrencyIsReadOnly_AndLocaleIsEditable()
    {
        Assert.False(SettingRules.IsEditable(SettingKeys.CurrencyCode));
        Assert.True(SettingRules.IsEditable(SettingKeys.DisplayLocale));
    }

    [Fact]
    public void DiscountActivation_IsBlockedUntilRulesAreConfirmed() =>
        Assert.False(DiscountPolicy.RulesConfirmed);
}