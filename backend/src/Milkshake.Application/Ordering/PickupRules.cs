namespace Milkshake.Application.Ordering;

/// <summary>
/// The pickup times a patron may choose now. Local values are wall-clock times in the business time zone
/// (never the browser's); UTC values are what will be stored.
/// </summary>
public sealed record PickupWindow(DateTimeOffset EarliestUtc, DateTimeOffset LatestUtc, DateTime EarliestLocal, DateTime LatestLocal)
{
    /// <summary>True when no time can be chosen (e.g. same-day only and the day is over).</summary>
    public bool IsEmpty => EarliestUtc > LatestUtc;
}

/// <summary>
/// PROVISIONAL pickup rules pending open questions Q-05, Q-06, Q-17 and Q-18: earliest = now + lead time,
/// latest = end of the local day <c>MaxAdvanceDays</c> ahead. Opening hours, slots and capacity are not enforced.
/// </summary>
public static class PickupRules
{
    /// <summary>Format of local pickup times exchanged with the browser, e.g. 2026-11-19T15:30.</summary>
    public const string LocalFormat = "yyyy-MM-dd'T'HH:mm";

    /// <summary>The window of allowed pickup times at <paramref name="now"/>.</summary>
    public static PickupWindow WindowFor(OrderingSettings settings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);

        DateTimeOffset earliestUtc = CeilingToMinute(now.ToUniversalTime().AddMinutes(settings.LeadTimeMinutes));
        DateTime earliestLocal = TimeZoneInfo.ConvertTime(earliestUtc, settings.TimeZone).DateTime;

        DateTime localToday = TimeZoneInfo.ConvertTime(now, settings.TimeZone).Date;
        DateTime latestLocal = localToday.AddDays(settings.MaxAdvanceDays).AddHours(23).AddMinutes(59);
        var latestUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(latestLocal, settings.TimeZone), TimeSpan.Zero);

        return new PickupWindow(earliestUtc, latestUtc, earliestLocal, latestLocal);
    }

    private static DateTimeOffset CeilingToMinute(DateTimeOffset value)
    {
        long remainder = value.Ticks % TimeSpan.TicksPerMinute;
        return remainder == 0 ? value : value.AddTicks(TimeSpan.TicksPerMinute - remainder);
    }
}