using System.Globalization;
using Milkshake.Application.Ordering;

namespace Milkshake.UnitTests.Ordering;

public sealed class PickupRulesTests
{
    private static readonly TimeZoneInfo Johannesburg = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    // 12:00 in Johannesburg; with a 15-minute lead and 7 days: earliest 12:15 on the 19th, latest 23:59 on the 26th.
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    private static OrderingSettings Settings(int leadMinutes = 15, int maxAdvanceDays = 7) =>
        new(1, 10, leadMinutes, maxAdvanceDays, Johannesburg, "Africa/Johannesburg", "ZAR", "en-ZA");

    private static OrderingSettings LondonSettings() =>
        new(1, 10, 15, 7, London, "Europe/London", "GBP", "en-GB");

    private static PickupCheck Check(string? pickupLocal) =>
        PickupRules.Check(pickupLocal, Settings(), PickupRules.WindowFor(Settings(), Now));

    [Fact]
    public void Earliest_IsNowPlusLeadTime_RoundedUpToTheMinute_InUtcAndLocal()
    {
        var now = new DateTimeOffset(2026, 11, 19, 10, 0, 30, TimeSpan.Zero);   // 12:00:30 in Johannesburg

        PickupWindow window = PickupRules.WindowFor(Settings(leadMinutes: 15), now);

        Assert.Equal(new DateTimeOffset(2026, 11, 19, 10, 16, 0, TimeSpan.Zero), window.EarliestUtc);
        Assert.Equal(new DateTime(2026, 11, 19, 12, 16, 0), window.EarliestLocal);
    }

    [Fact]
    public void Latest_IsTheEndOfTheLocalDay_MaxAdvanceDaysAhead()
    {
        PickupWindow window = PickupRules.WindowFor(Settings(maxAdvanceDays: 7), Now);

        Assert.Equal(new DateTime(2026, 11, 26, 23, 59, 0), window.LatestLocal);
        Assert.Equal(new DateTimeOffset(2026, 11, 26, 21, 59, 0, TimeSpan.Zero), window.LatestUtc);   // UTC+2
        Assert.Equal(Now, window.AsOfUtc);
        Assert.False(window.IsEmpty);
    }

    [Fact]
    public void SameDayOnly_LateInTheEvening_LeavesNoPickupTimes()
    {
        var now = new DateTimeOffset(2026, 11, 19, 21, 50, 0, TimeSpan.Zero);   // 23:50 in Johannesburg

        PickupWindow window = PickupRules.WindowFor(Settings(leadMinutes: 15, maxAdvanceDays: 0), now);

        Assert.True(window.IsEmpty);
    }

    [Theory]
    [InlineData("2026-11-19T12:15", "2026-11-19T10:15:00Z")]   // exactly the earliest
    [InlineData("2026-11-22T09:30", "2026-11-22T07:30:00Z")]
    [InlineData("2026-11-26T23:59", "2026-11-26T21:59:00Z")]   // exactly the latest
    public void Check_TimeInsideTheWindow_ReturnsTheLocalTimeAndItsUtcInstant(string pickupLocal, string expectedUtc)
    {
        PickupCheck check = Check(pickupLocal);

        ValidatedPickup pickup = Assert.IsType<ValidatedPickup>(check.Pickup);
        Assert.Null(check.Error);
        Assert.Equal(DateTime.ParseExact(pickupLocal, PickupRules.LocalFormat, CultureInfo.InvariantCulture), pickup.Local);
        Assert.Equal(DateTimeOffset.Parse(expectedUtc, CultureInfo.InvariantCulture), pickup.Utc);
        Assert.Equal("Africa/Johannesburg", pickup.TimeZoneId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Check_NoTime_IsRequired(string? pickupLocal)
    {
        Assert.Equal(PickupMessages.Required, Check(pickupLocal).Error);
    }

    [Theory]
    [InlineData("2026-11-20 14:30")]          // space instead of T
    [InlineData("2026-11-20T14:30:15")]       // seconds
    [InlineData("2026-11-20T14:30Z")]         // UTC marker
    [InlineData("2026-11-20T14:30+02:00")]    // offset
    [InlineData("20/11/2026 14:30")]          // culture-specific
    [InlineData("2026-11-31T10:00")]          // no such date
    public void Check_AnythingButBusinessLocalMinutes_IsRejectedAsFormat(string pickupLocal)
    {
        Assert.Equal(PickupMessages.Format, Check(pickupLocal).Error);
    }

    [Theory]
    [InlineData("2026-11-19T11:00")]
    [InlineData("2026-11-19T12:00")]   // exactly now
    [InlineData("0001-01-01T00:00")]
    public void Check_TimeAtOrBeforeNow_IsInThePast(string pickupLocal)
    {
        Assert.Equal(PickupMessages.InThePast, Check(pickupLocal).Error);
    }

    [Fact]
    public void Check_TimeInsideTheLeadTime_IsTooSoon()
    {
        Assert.Equal(PickupMessages.TooSoon(15, new DateTime(2026, 11, 19, 12, 15, 0)), Check("2026-11-19T12:10").Error);
    }

    [Theory]
    [InlineData("2026-11-27T00:00")]
    [InlineData("9999-12-31T23:59")]
    public void Check_TimeAfterTheLastDay_IsTooFarAhead(string pickupLocal)
    {
        Assert.Equal(PickupMessages.TooFarAhead(7, new DateTime(2026, 11, 26, 23, 59, 0)), Check(pickupLocal).Error);
    }

    [Fact]
    public void Check_WhenTheWindowIsEmpty_SaysNoTimesAreAvailable()
    {
        var lateEvening = new DateTimeOffset(2026, 11, 19, 21, 50, 0, TimeSpan.Zero);
        OrderingSettings sameDay = Settings(maxAdvanceDays: 0);

        PickupCheck check = PickupRules.Check("2026-11-19T23:55", sameDay, PickupRules.WindowFor(sameDay, lateEvening));

        Assert.Equal(PickupMessages.NoTimesAvailable, check.Error);
    }

    [Fact]
    public void Check_TimeSkippedWhenTheClocksGoForward_IsRejected()
    {
        // Europe/London: 01:00 GMT jumps to 02:00 BST on 28 March 2027, so 01:30 does not exist.
        var now = new DateTimeOffset(2027, 3, 27, 10, 0, 0, TimeSpan.Zero);

        PickupCheck check = PickupRules.Check("2027-03-28T01:30", LondonSettings(), PickupRules.WindowFor(LondonSettings(), now));

        Assert.Equal(PickupMessages.SkippedByClockChange, check.Error);
    }

    [Fact]
    public void Check_TimeRepeatedWhenTheClocksGoBack_IsRejected()
    {
        // Europe/London: 02:00 BST falls back to 01:00 GMT on 25 October 2026, so 01:30 happens twice.
        var now = new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);

        PickupCheck check = PickupRules.Check("2026-10-25T01:30", LondonSettings(), PickupRules.WindowFor(LondonSettings(), now));

        Assert.Equal(PickupMessages.RepeatedByClockChange, check.Error);
    }
}