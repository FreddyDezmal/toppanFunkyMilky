using Milkshake.Application.Ordering;
using Milkshake.Domain.Settings;

namespace Milkshake.UnitTests.Ordering;

public sealed class OrderingSettingsReaderTests
{
    private static Dictionary<string, string> ValidSettings() => new()
    {
        [SettingKeys.MinDrinksPerOrder] = "1",
        [SettingKeys.MaxDrinksPerOrder] = "10",
        [SettingKeys.MinPickupLeadTimeMinutes] = "15",
        [SettingKeys.PickupMaxAdvanceDays] = "7",
        [SettingKeys.PickupTimeZone] = "Africa/Johannesburg",
        [SettingKeys.CurrencyCode] = "ZAR",
        [SettingKeys.DisplayLocale] = "en-ZA",
    };

    [Fact]
    public void Read_ValidSettings_ReturnsTypedValues()
    {
        OrderingSettingsResult result = OrderingSettingsReader.Read(ValidSettings());

        OrderingSettings settings = Assert.IsType<OrderingSettings>(result.Settings);
        Assert.Equal((1, 10, 15, 7), (settings.MinDrinks, settings.MaxDrinks, settings.LeadTimeMinutes, settings.MaxAdvanceDays));
        Assert.Equal(("Africa/Johannesburg", "ZAR", "en-ZA"), (settings.TimeZoneId, settings.CurrencyCode, settings.DisplayLocale));
    }

    [Fact]
    public void Read_MissingSetting_IsUnavailable()
    {
        Dictionary<string, string> stored = ValidSettings();
        stored.Remove(SettingKeys.PickupTimeZone);

        OrderingSettingsResult result = OrderingSettingsReader.Read(stored);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains(SettingKeys.PickupTimeZone, StringComparison.Ordinal));
    }

    [Fact]
    public void Read_UnknownTimeZone_IsUnavailable()
    {
        Dictionary<string, string> stored = ValidSettings();
        stored[SettingKeys.PickupTimeZone] = "Nowhere/Nothing";

        Assert.False(OrderingSettingsReader.Read(stored).IsValid);
    }

    [Fact]
    public void Read_MinimumAboveMaximum_IsUnavailable()
    {
        Dictionary<string, string> stored = ValidSettings();
        stored[SettingKeys.MinDrinksPerOrder] = "11";

        Assert.False(OrderingSettingsReader.Read(stored).IsValid);
    }
}