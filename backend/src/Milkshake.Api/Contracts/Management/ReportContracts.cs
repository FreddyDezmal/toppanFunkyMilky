using System.Globalization;
using Milkshake.Application.Management;
using Milkshake.Application.Ordering;

namespace Milkshake.Api.Contracts.Management;

/// <summary>
/// One milkshake in the orders report (URS §3.4.4): <see cref="Date"/> (yyyy-MM-dd) and <see cref="Time"/> (HH:mm) are
/// when the order was placed, in the business time zone; <see cref="PickupLocal"/> is the pickup time in the order's own
/// time zone. <see cref="PaymentStatus"/> is Paid, Unpaid or Cancelled; <see cref="RefundStatus"/> is set only for
/// cancelled orders. No patron details (decision R6).
/// </summary>
public sealed record OrderReportRowResponse(
    string Reference,
    string Date,
    string Time,
    DateTimeOffset PlacedAtUtc,
    string PickupLocal,
    string Restaurant,
    int DrinkNumber,
    string Flavour,
    string Topping,
    string Consistency,
    string PaymentStatus,
    string? RefundStatus)
{
    public static OrderReportRowResponse From(OrderReportRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new OrderReportRowResponse(
            row.Reference,
            row.PlacedLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            row.PlacedLocal.ToString("HH:mm", CultureInfo.InvariantCulture),
            row.PlacedAtUtc,
            row.PickupLocal.ToString(PickupRules.LocalFormat, CultureInfo.InvariantCulture),
            row.RestaurantName,
            row.DrinkNumber,
            row.Flavour,
            row.Topping,
            row.Consistency,
            row.Payment.Label.ToString(),
            row.Payment.Refund == RefundLabel.None ? null : row.Payment.Refund.ToString());
    }
}

/// <summary>
/// GET /api/manager/reports/orders: one page of rows plus totals of the whole filtered result. <see cref="TotalOrders"/>
/// counts orders; <see cref="TotalDrinks"/> counts rows (milkshakes) and drives <see cref="TotalPages"/>.
/// </summary>
public sealed record OrderReportResponse(
    string From,
    string To,
    string TimeZone,
    int TotalOrders,
    int TotalDrinks,
    int Page,
    int PageSize,
    int TotalPages,
    IReadOnlyList<OrderReportRowResponse> Rows)
{
    public static OrderReportResponse Create(OrderReportQuery query, OrderReportPage page)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);

        return new OrderReportResponse(
            query.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            query.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            page.TimeZoneId,
            page.TotalOrders,
            page.TotalDrinks,
            page.Page,
            page.PageSize,
            page.TotalPages,
            page.Rows.Select(OrderReportRowResponse.From).ToList());
    }
}

/// <summary>One bar: a weekday (Monday…Sunday), a month (yyyy-MM) or a year (yyyy).</summary>
public sealed record TrendPointResponse(string Label, int Orders, int PaidOrders);

/// <summary>
/// GET /api/manager/reports/trends: order volumes by weekday, month and year (AC-12.3). <c>from</c> and <c>to</c> are
/// null for all orders. <c>paidOrders</c> counts orders whose payment status is Paid.
/// </summary>
public sealed record OrderTrendsResponse(
    string? From,
    string? To,
    string TimeZone,
    int TotalOrders,
    int TotalPaidOrders,
    IReadOnlyList<TrendPointResponse> Weekdays,
    IReadOnlyList<TrendPointResponse> Months,
    IReadOnlyList<TrendPointResponse> Years)
{
    public static OrderTrendsResponse Create(OrderTrends trends)
    {
        ArgumentNullException.ThrowIfNull(trends);

        return new OrderTrendsResponse(
            trends.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            trends.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            trends.TimeZoneId,
            trends.TotalOrders,
            trends.TotalPaidOrders,
            Points(trends.Weekdays),
            Points(trends.Months),
            Points(trends.Years));
    }

    private static List<TrendPointResponse> Points(IEnumerable<TrendPoint> points) =>
        points.Select(p => new TrendPointResponse(p.Label, p.Orders, p.PaidOrders)).ToList();
}

/// <summary>Messages for the reports.</summary>
public static class ReportMessages
{
    public const string UnavailableTitle = "Reports are unavailable.";

    public const string UnavailableDetail = "The business time zone setting is missing or invalid. Check Management → Settings.";

    public const string TooLargeTitle = "Too many records to export.";

    public const string TooLargeDetail = "An export holds at most 10,000 records. Choose a shorter period or add filters, then export again.";
}
