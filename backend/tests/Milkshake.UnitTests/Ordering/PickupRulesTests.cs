using Milkshake.Application.Ordering;

namespace Milkshake.UnitTests.Ordering;

public sealed class PickupRulesTests
{
    private static readonly TimeZoneInfo Johannesburg = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");

    private static OrderingSettings Settings(int leadMinutes = 15, int maxAdvanceDays = 7) =>
        new(1, 10, leadMinutes, maxAdvanceDays, Johannesburg, "Africa/Johannesburg", "ZAR", "en-ZA");

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
        var now = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

        PickupWindow window = PickupRules.WindowFor(Settings(maxAdvanceDays: 7), now);

        Assert.Equal(new DateTime(2026, 11, 26, 23, 59, 0), window.LatestLocal);
        Assert.Equal(new DateTimeOffset(2026, 11, 26, 21, 59, 0, TimeSpan.Zero), window.LatestUtc);   // UTC+2
        Assert.False(window.IsEmpty);
    }

    [Fact]
    public void SameDayOnly_LateInTheEvening_LeavesNoPickupTimes()
    {
        var now = new DateTimeOffset(2026, 11, 19, 21, 50, 0, TimeSpan.Zero);   // 23:50 in Johannesburg

        PickupWindow window = PickupRules.WindowFor(Settings(leadMinutes: 15, maxAdvanceDays: 0), now);

        Assert.True(window.IsEmpty);
    }
}