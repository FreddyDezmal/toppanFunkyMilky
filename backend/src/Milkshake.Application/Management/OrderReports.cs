using System.Globalization;
using Milkshake.Application.Ordering;

namespace Milkshake.Application.Management;

/// <summary>Which orders a report includes, by the payment label of decision R3 (URS §3.4.4: Paid, Unpaid, Cancelled).</summary>
public enum ReportPaymentFilter
{
    All,
    Paid,
    Unpaid,
    Cancelled,
}

/// <summary>
/// Orders placed between two business-local dates, inclusive (AC-12.2): one row per milkshake, newest order first,
/// numbered pages (URS §3.4.4). <see cref="Text"/> searches flavour, consistency, topping, restaurant and reference.
/// </summary>
public sealed record OrderReportQuery(DateOnly From, DateOnly To, ReportPaymentFilter Status, string? Text, int Page, int PageSize);

/// <summary>Order volumes for a period (AC-12.3). Both dates null means all orders ("None" on the URS screen).</summary>
public sealed record OrderTrendsQuery(DateOnly? From, DateOnly? To);

/// <summary>One milkshake of an order, from the stored snapshot; times already in the business time zone.</summary>
public sealed record OrderReportRow(
    string Reference,
    DateTimeOffset PlacedAtUtc,
    DateTime PlacedLocal,
    DateTime PickupLocal,
    string RestaurantName,
    int DrinkNumber,
    string Flavour,
    string Consistency,
    string Topping,
    PaymentState Payment);

/// <summary>A page of the orders report with the totals of the whole filtered result.</summary>
public sealed record OrderReportPage(string TimeZoneId, int TotalOrders, int TotalDrinks, int Page, int PageSize, IReadOnlyList<OrderReportRow> Rows)
{
    public int TotalPages => TotalDrinks == 0 ? 0 : (TotalDrinks + PageSize - 1) / PageSize;
}

/// <summary>Orders placed on one business-local day; <see cref="PaidOrders"/> counts those whose label is Paid.</summary>
public sealed record DailyOrderCount(DateOnly Date, int Orders, int PaidOrders);

/// <summary>One bar of a trend chart: a weekday ("Monday"), a month ("2026-11") or a year ("2026").</summary>
public sealed record TrendPoint(string Label, int Orders, int PaidOrders);

/// <summary>Order volumes by weekday, month and year (AC-12.3, URS §3.4.4 Weekly, Monthly and Growth).</summary>
public sealed record OrderTrends(
    string TimeZoneId,
    DateOnly? From,
    DateOnly? To,
    int TotalOrders,
    int TotalPaidOrders,
    IReadOnlyList<TrendPoint> Weekdays,
    IReadOnlyList<TrendPoint> Months,
    IReadOnlyList<TrendPoint> Years);

/// <summary>
/// Management reports (M12) from stored order snapshots and payment and refund records only: never audit rows or
/// current prices (decision R4). Patron identity is not reported (decision R6).
/// </summary>
public interface IOrderReportService
{
    /// <summary>Null when the business time zone setting is missing or unusable.</summary>
    Task<OrderReportPage?> OrdersAsync(OrderReportQuery query, CancellationToken cancellationToken);

    /// <summary>Null when the business time zone setting is missing or unusable.</summary>
    Task<OrderTrends?> TrendsAsync(OrderTrendsQuery query, CancellationToken cancellationToken);
}

/// <summary>Business-local dates as UTC instants (decision R1: days start at local midnight).</summary>
public static class ReportPeriod
{
    /// <summary>[start of <paramref name="from"/>, start of the day after <paramref name="to"/>) in UTC.</summary>
    public static (DateTimeOffset FromUtc, DateTimeOffset ToUtcExclusive) ToUtc(DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return (StartOfDay(from, zone), StartOfDay(to.AddDays(1), zone));
    }

    /// <summary>The first valid local moment of the day (midnight, or later when a clock change skips it).</summary>
    public static DateTimeOffset StartOfDay(DateOnly date, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        DateTime local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }
}

/// <summary>Builds the trend series from daily counts: every weekday, and every month and year in the period, with zeros.</summary>
public static class OrderTrendBuilder
{
    private static readonly DayOfWeek[] Week =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday,
        DayOfWeek.Sunday,
    ];

    public static OrderTrends Build(string timeZoneId, DateOnly? from, DateOnly? to, IReadOnlyCollection<DailyOrderCount> days)
    {
        ArgumentNullException.ThrowIfNull(days);

        List<TrendPoint> weekdays = Week
            .Select(day =>
            {
                List<DailyOrderCount> matching = days.Where(d => d.Date.DayOfWeek == day).ToList();
                return new TrendPoint(day.ToString(), matching.Sum(d => d.Orders), matching.Sum(d => d.PaidOrders));
            })
            .ToList();

        DateOnly? first = from ?? (days.Count == 0 ? (DateOnly?)null : days.Min(d => d.Date));
        DateOnly? last = to ?? (days.Count == 0 ? (DateOnly?)null : days.Max(d => d.Date));
        var months = new List<TrendPoint>();
        var years = new List<TrendPoint>();
        if (first is { } start && last is { } end)
        {
            for (var month = new DateOnly(start.Year, start.Month, 1); month <= end; month = month.AddMonths(1))
            {
                List<DailyOrderCount> matching = days.Where(d => d.Date.Year == month.Year && d.Date.Month == month.Month).ToList();
                months.Add(new TrendPoint(month.ToString("yyyy-MM", CultureInfo.InvariantCulture), matching.Sum(d => d.Orders), matching.Sum(d => d.PaidOrders)));
            }

            for (int year = start.Year; year <= end.Year; year++)
            {
                List<DailyOrderCount> matching = days.Where(d => d.Date.Year == year).ToList();
                years.Add(new TrendPoint(year.ToString(CultureInfo.InvariantCulture), matching.Sum(d => d.Orders), matching.Sum(d => d.PaidOrders)));
            }
        }

        return new OrderTrends(timeZoneId, from, to, days.Sum(d => d.Orders), days.Sum(d => d.PaidOrders), weekdays, months, years);
    }
}

/// <summary>
/// Strict validation of the report query strings: known parameters only, each once; dates as yyyy-MM-dd (business
/// local); a period of at most 366 days; numbered pages of 1–50 rows.
/// </summary>
public static class OrderReportQueryParser
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 50;
    public const int MaxPage = 10_000;
    public const int MaxDays = 366;
    public const int MaxTextLength = 100;

    private static readonly string[] OrderKeys = ["from", "to", "status", "q", "page", "pageSize"];
    private static readonly string[] TrendKeys = ["from", "to"];
    private static readonly DateOnly Earliest = new(2000, 1, 1);
    private static readonly DateOnly Latest = new(2099, 12, 31);

    public static OrderReportQuery? ParseOrders(IReadOnlyDictionary<string, string[]> query, IDictionary<string, string> errors)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(errors);

        Dictionary<string, string> values = Single(query, OrderKeys, errors);
        (DateOnly? from, DateOnly? to) = Period(values, errors);
        if (from is null && !errors.ContainsKey("from"))
        {
            errors["from"] = "Choose a date (from), and optionally an end date (to).";
        }

        ReportPaymentFilter status = ReportPaymentFilter.All;
        if (values.TryGetValue("status", out string? rawStatus))
        {
            ReportPaymentFilter? parsed = rawStatus.ToUpperInvariant() switch
            {
                "ALL" => ReportPaymentFilter.All,
                "PAID" => ReportPaymentFilter.Paid,
                "UNPAID" => ReportPaymentFilter.Unpaid,
                "CANCELLED" => ReportPaymentFilter.Cancelled,
                _ => null,
            };
            if (parsed is null)
            {
                errors["status"] = "Use all, paid, unpaid or cancelled.";
            }
            else
            {
                status = parsed.Value;
            }
        }

        string? text = values.GetValueOrDefault("q");
        if (text is not null && text.Length > MaxTextLength)
        {
            errors["q"] = $"Use at most {MaxTextLength} characters.";
        }

        int page = Number(values, "page", 1, MaxPage, 1, errors);
        int pageSize = Number(values, "pageSize", 1, MaxPageSize, DefaultPageSize, errors);

        return errors.Count > 0 || from is null || to is null
            ? null
            : new OrderReportQuery(from.Value, to.Value, status, text, page, pageSize);
    }

    public static OrderTrendsQuery? ParseTrends(IReadOnlyDictionary<string, string[]> query, IDictionary<string, string> errors)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(errors);

        Dictionary<string, string> values = Single(query, TrendKeys, errors);
        (DateOnly? from, DateOnly? to) = Period(values, errors);

        return errors.Count > 0 ? null : new OrderTrendsQuery(from, to);
    }

    private static Dictionary<string, string> Single(IReadOnlyDictionary<string, string[]> query, string[] allowed, IDictionary<string, string> errors)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string[] given) in query)
        {
            string? known = allowed.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (known is null)
            {
                errors[key] = $"Unknown parameter. Use: {string.Join(", ", allowed)}.";
            }
            else if (given.Length != 1)
            {
                errors[known] = "Give this parameter once.";
            }
            else if (!string.IsNullOrWhiteSpace(given[0]))
            {
                values[known] = given[0].Trim();
            }
        }

        return values;
    }

    /// <summary>A single date (from only, to defaults to it) or an inclusive range; to without from is refused.</summary>
    private static (DateOnly? From, DateOnly? To) Period(Dictionary<string, string> values, IDictionary<string, string> errors)
    {
        DateOnly? from = Date(values, "from", errors);
        DateOnly? to = Date(values, "to", errors);
        if (errors.ContainsKey("from") || errors.ContainsKey("to"))
        {
            return (null, null);
        }

        if (from is null)
        {
            if (to is not null)
            {
                errors["from"] = "Choose a start date (from) for the end date.";
            }

            return (null, null);
        }

        to ??= from;
        if (to < from)
        {
            errors["to"] = "The end date must be on or after the start date.";
        }
        else if (to.Value.DayNumber - from.Value.DayNumber + 1 > MaxDays)
        {
            errors["to"] = $"Choose a period of at most {MaxDays} days.";
        }

        return (from, to);
    }

    private static DateOnly? Date(Dictionary<string, string> values, string key, IDictionary<string, string> errors)
    {
        if (!values.TryGetValue(key, out string? raw))
        {
            return null;
        }

        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date) &&
            date >= Earliest && date <= Latest)
        {
            return date;
        }

        errors[key] = "Use a date such as 2026-11-20 (year 2000 to 2099).";
        return null;
    }

    private static int Number(Dictionary<string, string> values, string key, int min, int max, int fallback, IDictionary<string, string> errors)
    {
        if (!values.TryGetValue(key, out string? raw))
        {
            return fallback;
        }

        if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value >= min && value <= max)
        {
            return value;
        }

        errors[key] = $"Use a number from {min} to {max}.";
        return fallback;
    }
}
