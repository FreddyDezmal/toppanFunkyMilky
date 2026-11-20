using Milkshake.Application.Management;

namespace Milkshake.UnitTests.Management;

/// <summary>M12 management reports: strict query parsing, business-local periods (decision R1) and trend series.</summary>
public sealed class OrderReportTests
{
    private static readonly TimeZoneInfo Johannesburg = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");

    private static Dictionary<string, string[]> Query(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => new[] { p.Value }, StringComparer.Ordinal);

    private static (OrderReportQuery? Query, Dictionary<string, string> Errors) Orders(Dictionary<string, string[]> query)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        return (OrderReportQueryParser.ParseOrders(query, errors), errors);
    }

    private static (OrderTrendsQuery? Query, Dictionary<string, string> Errors) Trends(Dictionary<string, string[]> query)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        return (OrderReportQueryParser.ParseTrends(query, errors), errors);
    }

    // ---- orders report parameters ---------------------------------------------------------------------------------

    [Fact]
    public void ASingleDate_IsThatDayOnly_WithDefaults()
    {
        (OrderReportQuery? query, Dictionary<string, string> errors) = Orders(Query(("from", "2026-11-20")));

        Assert.Empty(errors);
        Assert.Equal(new OrderReportQuery(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 20), ReportPaymentFilter.All, null, 1, 25), query);
    }

    [Fact]
    public void ARangeWithEveryFilter_IsAccepted_CaseInsensitively()
    {
        (OrderReportQuery? query, _) = Orders(Query(
            ("FROM", "2026-01-01"), ("to", "2026-12-31"), ("Status", "Cancelled"), ("q", "  Oreo  "), ("page", "3"), ("pagesize", "50")));

        Assert.Equal(new OrderReportQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), ReportPaymentFilter.Cancelled, "Oreo", 3, 50), query);
    }

    [Theory]
    [InlineData("all", ReportPaymentFilter.All)]
    [InlineData("PAID", ReportPaymentFilter.Paid)]
    [InlineData("unpaid", ReportPaymentFilter.Unpaid)]
    [InlineData("cancelled", ReportPaymentFilter.Cancelled)]
    public void EveryStatus_IsAccepted(string status, ReportPaymentFilter expected)
    {
        (OrderReportQuery? query, _) = Orders(Query(("from", "2026-11-20"), ("status", status)));

        Assert.Equal(expected, query?.Status);
    }

    [Fact]
    public void ThreeHundredSixtySixDays_AreAllowed_ButNotThreeHundredSixtySeven()
    {
        (OrderReportQuery? leap, _) = Orders(Query(("from", "2028-01-01"), ("to", "2028-12-31")));
        (OrderReportQuery? tooLong, Dictionary<string, string> errors) = Orders(Query(("from", "2027-01-01"), ("to", "2028-01-02")));

        Assert.NotNull(leap);
        Assert.Null(tooLong);
        Assert.Equal("Choose a period of at most 366 days.", errors["to"]);
    }

    [Theory]
    [InlineData("from", "2026-1-5")]
    [InlineData("from", "20261120")]
    [InlineData("from", "2026-11-20T00:00")]
    [InlineData("from", "1999-12-31")]
    [InlineData("from", "2100-01-01")]
    [InlineData("from", "2026-02-30")]
    [InlineData("status", "1")]
    [InlineData("status", "refunded")]
    [InlineData("page", "0")]
    [InlineData("page", "10001")]
    [InlineData("pageSize", "51")]
    [InlineData("pageSize", "-5")]
    [InlineData("patronId", "x")]
    public void InvalidValues_AreRejected_OnTheirField(string key, string value)
    {
        Dictionary<string, string[]> query = Query(("from", "2026-11-20"));
        query[key] = [value];

        (OrderReportQuery? parsed, Dictionary<string, string> errors) = Orders(query);

        Assert.Null(parsed);
        Assert.True(errors.ContainsKey(key), string.Join("; ", errors));
    }

    [Fact]
    public void AnOverlongSearch_IsRejected()
    {
        (OrderReportQuery? query, Dictionary<string, string> errors) = Orders(Query(("from", "2026-11-20"), ("q", new string('a', 101))));

        Assert.Null(query);
        Assert.True(errors.ContainsKey("q"));
    }

    [Fact]
    public void TheOrdersReport_NeedsAStartDate()
    {
        (OrderReportQuery? none, Dictionary<string, string> noneErrors) = Orders(Query());
        (OrderReportQuery? toOnly, Dictionary<string, string> toOnlyErrors) = Orders(Query(("to", "2026-11-20")));

        Assert.Null(none);
        Assert.True(noneErrors.ContainsKey("from"));
        Assert.Null(toOnly);
        Assert.True(toOnlyErrors.ContainsKey("from"));
    }

    [Fact]
    public void AnEndBeforeTheStart_IsRejected()
    {
        (OrderReportQuery? query, Dictionary<string, string> errors) = Orders(Query(("from", "2026-11-20"), ("to", "2026-11-19")));

        Assert.Null(query);
        Assert.Equal("The end date must be on or after the start date.", errors["to"]);
    }

    [Fact]
    public void ARepeatedParameter_IsRejected()
    {
        var query = new Dictionary<string, string[]>(StringComparer.Ordinal) { ["from"] = ["2026-11-20", "2026-11-21"] };

        (OrderReportQuery? parsed, Dictionary<string, string> errors) = Orders(query);

        Assert.Null(parsed);
        Assert.Equal("Give this parameter once.", errors["from"]);
    }

    // ---- trends parameters ----------------------------------------------------------------------------------------

    [Fact]
    public void TrendsWithoutDates_CoverAllOrders()
    {
        (OrderTrendsQuery? query, Dictionary<string, string> errors) = Trends(Query());

        Assert.Empty(errors);
        Assert.Equal(new OrderTrendsQuery(null, null), query);
    }

    [Fact]
    public void TrendsForOneDate_AreThatDay()
    {
        (OrderTrendsQuery? query, _) = Trends(Query(("from", "2026-11-20")));

        Assert.Equal(new OrderTrendsQuery(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 20)), query);
    }

    [Theory]
    [InlineData("status", "paid")]
    [InlineData("page", "1")]
    [InlineData("to", "2026-11-20")]
    public void TrendsRefuseOrderOnlyParameters_AndAnEndWithoutAStart(string key, string value)
    {
        (OrderTrendsQuery? query, Dictionary<string, string> errors) = Trends(Query((key, value)));

        Assert.Null(query);
        Assert.NotEmpty(errors);
    }

    // ---- business-local periods -------------------------------------------------------------------------------------

    [Fact]
    public void ADayInJohannesburg_RunsFromLocalMidnightToLocalMidnight_InUtc()
    {
        (DateTimeOffset from, DateTimeOffset to) = ReportPeriod.ToUtc(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 20), Johannesburg);

        Assert.Equal(new DateTimeOffset(2026, 11, 19, 22, 0, 0, TimeSpan.Zero), from);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 22, 0, 0, TimeSpan.Zero), to);
        Assert.Equal(TimeSpan.Zero, from.Offset);
    }

    [Fact]
    public void ADayWhoseMidnightIsSkippedByAClockChange_StartsAtItsFirstValidMinute()
    {
        // A zone at UTC-3 that moves to UTC-2 at 00:00 on the first Sunday of September (2026-09-06): 00:00–00:59 never happen.
        TimeZoneInfo.AdjustmentRule rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2000, 1, 1), new DateTime(2099, 12, 31), TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 0, 0, 0), 9, 1, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 0, 0, 0), 4, 1, DayOfWeek.Sunday));
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test/SkipsMidnight", TimeSpan.FromHours(-3), "Skips midnight", "Standard", "Daylight", [rule]);

        DateTimeOffset start = ReportPeriod.StartOfDay(new DateOnly(2026, 9, 6), zone);

        Assert.Equal(new DateTimeOffset(2026, 9, 6, 3, 0, 0, TimeSpan.Zero), start);
    }

    // ---- trend series -----------------------------------------------------------------------------------------------

    [Fact]
    public void Trends_HaveEveryWeekdayMondayFirst_AndEveryMonthAndYearOfThePeriod_WithZeros()
    {
        DailyOrderCount[] days =
        [
            new(new DateOnly(2026, 11, 16), 3, 2), // Monday
            new(new DateOnly(2026, 11, 20), 4, 1), // Friday
            new(new DateOnly(2026, 11, 23), 1, 1), // Monday
            new(new DateOnly(2027, 1, 3), 2, 0),   // Sunday
        ];

        OrderTrends trends = OrderTrendBuilder.Build("Africa/Johannesburg", new DateOnly(2026, 10, 15), new DateOnly(2027, 1, 31), days);

        Assert.Equal((10, 4), (trends.TotalOrders, trends.TotalPaidOrders));
        Assert.Equal(
            new[] { ("Monday", 4, 3), ("Tuesday", 0, 0), ("Wednesday", 0, 0), ("Thursday", 0, 0), ("Friday", 4, 1), ("Saturday", 0, 0), ("Sunday", 2, 0) },
            trends.Weekdays.Select(p => (p.Label, p.Orders, p.PaidOrders)));
        Assert.Equal(
            new[] { ("2026-10", 0, 0), ("2026-11", 8, 4), ("2026-12", 0, 0), ("2027-01", 2, 0) },
            trends.Months.Select(p => (p.Label, p.Orders, p.PaidOrders)));
        Assert.Equal(new[] { ("2026", 8, 4), ("2027", 2, 0) }, trends.Years.Select(p => (p.Label, p.Orders, p.PaidOrders)));
    }

    [Fact]
    public void TrendsForAllOrders_SpanTheFirstToTheLastDayWithOrders()
    {
        DailyOrderCount[] days = [new(new DateOnly(2026, 12, 2), 1, 1), new(new DateOnly(2027, 2, 9), 1, 0)];

        OrderTrends trends = OrderTrendBuilder.Build("Africa/Johannesburg", null, null, days);

        Assert.Equal(new[] { "2026-12", "2027-01", "2027-02" }, trends.Months.Select(p => p.Label));
        Assert.Equal(new[] { "2026", "2027" }, trends.Years.Select(p => p.Label));
    }

    [Fact]
    public void TrendsWithoutAnyOrders_StillListTheWeek()
    {
        OrderTrends none = OrderTrendBuilder.Build("Africa/Johannesburg", null, null, []);
        OrderTrends emptyPeriod = OrderTrendBuilder.Build("Africa/Johannesburg", new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 20), []);

        Assert.Equal(7, none.Weekdays.Count);
        Assert.All(none.Weekdays, p => Assert.Equal((0, 0), (p.Orders, p.PaidOrders)));
        Assert.Empty(none.Months);
        Assert.Empty(none.Years);
        Assert.Equal(("2026-11", 0), (Assert.Single(emptyPeriod.Months).Label, emptyPeriod.Months[0].Orders));
    }

    [Fact]
    public void TotalPages_FollowTheRows()
    {
        Assert.Equal(0, new OrderReportPage("z", 0, 0, 1, 25, []).TotalPages);
        Assert.Equal(1, new OrderReportPage("z", 10, 25, 1, 25, []).TotalPages);
        Assert.Equal(2, new OrderReportPage("z", 10, 26, 1, 25, []).TotalPages);
    }
}
