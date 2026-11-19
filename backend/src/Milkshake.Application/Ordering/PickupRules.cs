using System.Globalization;

namespace Milkshake.Application.Ordering;

/// <summary>
/// The pickup times a patron may choose, worked out at <see cref="AsOfUtc"/>. Local values are wall-clock times in the
/// business time zone (never the browser's); UTC values are what will be stored.
/// </summary>
public sealed record PickupWindow(
    DateTimeOffset AsOfUtc,
    DateTimeOffset EarliestUtc,
    DateTimeOffset LatestUtc,
    DateTime EarliestLocal,
    DateTime LatestLocal)
{
    /// <summary>True when no time can be chosen (e.g. same-day only and the day is over).</summary>
    public bool IsEmpty => EarliestUtc > LatestUtc;
}

/// <summary>A pickup time that is valid right now: the business-local wall-clock time and the instant it denotes.</summary>
public sealed record ValidatedPickup(DateTime Local, DateTimeOffset Utc, string TimeZoneId);

/// <summary>A valid pickup time, or the message explaining why it cannot be used.</summary>
public sealed record PickupCheck(ValidatedPickup? Pickup, string? Error);

/// <summary>
/// PROVISIONAL pickup rules pending open questions Q-05, Q-06, Q-17 and Q-18: earliest = now + lead time,
/// latest = end of the local day <c>MaxAdvanceDays</c> ahead. Opening hours, slots and capacity are not enforced.
/// M7 must check the time again when the order is placed.
/// </summary>
public static class PickupRules
{
    /// <summary>Format of local pickup times exchanged with the browser, e.g. 2026-11-19T15:30.</summary>
    public const string LocalFormat = "yyyy-MM-dd'T'HH:mm";

    /// <summary>The window of allowed pickup times at <paramref name="now"/>.</summary>
    public static PickupWindow WindowFor(OrderingSettings settings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);

        DateTimeOffset nowUtc = now.ToUniversalTime();
        DateTimeOffset earliestUtc = CeilingToMinute(nowUtc.AddMinutes(settings.LeadTimeMinutes));
        DateTime earliestLocal = TimeZoneInfo.ConvertTime(earliestUtc, settings.TimeZone).DateTime;

        DateTime localToday = TimeZoneInfo.ConvertTime(nowUtc, settings.TimeZone).Date;
        DateTime latestLocal = localToday.AddDays(settings.MaxAdvanceDays).AddHours(23).AddMinutes(59);
        var latestUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(latestLocal, settings.TimeZone), TimeSpan.Zero);

        return new PickupWindow(nowUtc, earliestUtc, latestUtc, earliestLocal, latestLocal);
    }

    /// <summary>
    /// Checks a business-local pickup time (<see cref="LocalFormat"/>) against <paramref name="window"/>.
    /// Times skipped or repeated by a clock change are rejected rather than guessed.
    /// </summary>
    public static PickupCheck Check(string? pickupLocal, OrderingSettings settings, PickupWindow window)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(window);

        if (window.IsEmpty)
        {
            return Fail(PickupMessages.NoTimesAvailable);
        }

        if (string.IsNullOrWhiteSpace(pickupLocal))
        {
            return Fail(PickupMessages.Required);
        }

        if (!DateTime.TryParseExact(pickupLocal, LocalFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
        {
            return Fail(PickupMessages.Format);
        }

        // Far outside the window: decide on the wall-clock value alone (avoids arithmetic at the edges of the calendar).
        if (local < window.EarliestLocal.AddDays(-2))
        {
            return Fail(PickupMessages.InThePast);
        }

        if (local > window.LatestLocal.AddDays(2))
        {
            return Fail(PickupMessages.TooFarAhead(settings.MaxAdvanceDays, window.LatestLocal));
        }

        TimeZoneInfo zone = settings.TimeZone;
        if (zone.IsInvalidTime(local))
        {
            return Fail(PickupMessages.SkippedByClockChange);
        }

        if (zone.IsAmbiguousTime(local))
        {
            return Fail(PickupMessages.RepeatedByClockChange);
        }

        var utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);

        if (utc <= window.AsOfUtc)
        {
            return Fail(PickupMessages.InThePast);
        }

        if (utc < window.EarliestUtc)
        {
            return Fail(PickupMessages.TooSoon(settings.LeadTimeMinutes, window.EarliestLocal));
        }

        if (utc > window.LatestUtc)
        {
            return Fail(PickupMessages.TooFarAhead(settings.MaxAdvanceDays, window.LatestLocal));
        }

        return new PickupCheck(new ValidatedPickup(local, utc, settings.TimeZoneId), null);
    }

    private static PickupCheck Fail(string message) => new(null, message);

    private static DateTimeOffset CeilingToMinute(DateTimeOffset value)
    {
        long remainder = value.Ticks % TimeSpan.TicksPerMinute;
        return remainder == 0 ? value : value.AddTicks(TimeSpan.TicksPerMinute - remainder);
    }
}

/// <summary>User-facing pickup messages. Times are shown in the business time zone.</summary>
public static class PickupMessages
{
    /// <summary>No time sent.</summary>
    public const string Required = "Choose a pickup date and time.";

    /// <summary>Not in yyyy-MM-ddTHH:mm form (or not a real date).</summary>
    public const string Format = "Enter the pickup date and time to the minute, for example 2026-11-20T14:30.";

    /// <summary>The window is empty.</summary>
    public const string NoTimesAvailable = "No pickup times are available right now. Please try again later.";

    /// <summary>The local time is skipped when the clocks go forward.</summary>
    public const string SkippedByClockChange =
        "That time does not exist on that day because the clocks change. Please choose another time.";

    /// <summary>The local time happens twice when the clocks go back.</summary>
    public const string RepeatedByClockChange =
        "That time occurs twice on that day because the clocks change. Please choose another time.";

    /// <summary>At or before now.</summary>
    public const string InThePast = "That pickup time has already passed. Please choose a later time.";

    /// <summary>In the future but inside the lead time.</summary>
    public static string TooSoon(int leadTimeMinutes, DateTime earliestLocal) =>
        $"Pickup must be at least {Minutes(leadTimeMinutes)} from now. The earliest available time is {Display(earliestLocal)}.";

    /// <summary>After the last allowed day.</summary>
    public static string TooFarAhead(int maxAdvanceDays, DateTime latestLocal) =>
        maxAdvanceDays == 0
            ? $"Pickup must be today, no later than {Display(latestLocal)}."
            : $"Pickup can be booked up to {Days(maxAdvanceDays)} ahead. The latest available time is {Display(latestLocal)}.";

    private static string Display(DateTime local) => local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string Minutes(int count) => count == 1 ? "1 minute" : $"{count} minutes";

    private static string Days(int count) => count == 1 ? "1 day" : $"{count} days";
}