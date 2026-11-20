using Microsoft.EntityFrameworkCore;
using Milkshake.Application.Management;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Settings;
using Milkshake.Infrastructure.Ordering;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Infrastructure.Management;

/// <summary>
/// Read-only management reports (M12) over the stored order snapshots, payments and refunds. Periods are business-local
/// days (decision R1) turned into UTC bounds, so the (created_at, id) index serves every query. Parameterised only.
/// </summary>
internal sealed class OrderReportService(MilkshakeDbContext db) : IOrderReportService
{
    public async Task<OrderReportPage?> OrdersAsync(OrderReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await BusinessZoneAsync(cancellationToken) is not { } business)
        {
            return null;
        }

        (DateTimeOffset fromUtc, DateTimeOffset toUtc) = ReportPeriod.ToUtc(query.From, query.To, business.Zone);
        IQueryable<Order> orders = db.Orders.AsNoTracking().Where(o => o.CreatedAt >= fromUtc && o.CreatedAt < toUtc);
        orders = query.Status switch
        {
            ReportPaymentFilter.Paid => orders.Where(o => o.Status == OrderStatus.Confirmed),
            ReportPaymentFilter.Unpaid => orders.Where(o => o.Status == OrderStatus.PendingPayment || o.Status == OrderStatus.PaymentFailed),
            ReportPaymentFilter.Cancelled => orders.Where(o => o.Status == OrderStatus.Cancelled),
            _ => orders,
        };

        var rows =
            from o in orders
            join i in db.OrderItems.AsNoTracking() on o.Id equals i.OrderId
            select new { Order = o, Item = i };

        if (!string.IsNullOrEmpty(query.Text))
        {
            string pattern = "%" + EscapeLike(query.Text) + "%";
            rows = rows.Where(r =>
                EF.Functions.ILike(r.Item.FlavourName, pattern, "\\") ||
                EF.Functions.ILike(r.Item.ConsistencyName, pattern, "\\") ||
                EF.Functions.ILike(r.Item.ToppingName, pattern, "\\") ||
                EF.Functions.ILike(r.Order.RestaurantName, pattern, "\\") ||
                EF.Functions.ILike(r.Order.Reference, pattern, "\\"));
        }

        int totalDrinks = await rows.CountAsync(cancellationToken);
        int totalOrders = await rows.Select(r => r.Order.Id).Distinct().CountAsync(cancellationToken);

        var page = await rows
            .OrderByDescending(r => r.Order.CreatedAt)
            .ThenByDescending(r => r.Order.Id)
            .ThenBy(r => r.Item.Number)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(r => new
            {
                r.Order.Id,
                r.Order.Reference,
                r.Order.Status,
                r.Order.CreatedAt,
                r.Order.PickupAtUtc,
                r.Order.PickupTimeZone,
                r.Order.RestaurantName,
                r.Item.Number,
                r.Item.FlavourName,
                r.Item.ConsistencyName,
                r.Item.ToppingName,
            })
            .ToListAsync(cancellationToken);

        ILookup<int, RefundFacts> refunds = await RefundFactsReader.ForOrdersAsync(db, page.Select(r => r.Id).Distinct().ToList(), cancellationToken);
        var pickupZones = new Dictionary<string, TimeZoneInfo>(StringComparer.Ordinal);

        List<OrderReportRow> result = page
            .Select(r => new OrderReportRow(
                r.Reference,
                r.CreatedAt,
                TimeZoneInfo.ConvertTime(r.CreatedAt, business.Zone).DateTime,
                TimeZoneInfo.ConvertTime(r.PickupAtUtc, PickupZone(pickupZones, r.PickupTimeZone, business.Zone)).DateTime,
                r.RestaurantName,
                r.Number,
                r.FlavourName,
                r.ConsistencyName,
                r.ToppingName,
                PaymentLabels.For(r.Status, refunds[r.Id].ToList())))
            .ToList();

        return new OrderReportPage(business.Id, totalOrders, totalDrinks, query.Page, query.PageSize, result);
    }

    public async Task<OrderTrends?> TrendsAsync(OrderTrendsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await BusinessZoneAsync(cancellationToken) is not { } business)
        {
            return null;
        }

        // Grouped by business-local day in the database; weekdays, months and years are built from the days.
        // "Paid" is the Paid label of decision R3 (order status Confirmed).
        FormattableString sql;
        if (query.From is { } from && query.To is { } to)
        {
            (DateTimeOffset fromUtc, DateTimeOffset toUtc) = ReportPeriod.ToUtc(from, to, business.Zone);
            sql = $"""
                SELECT (o.created_at AT TIME ZONE {business.Id})::date AS day,
                       count(*)::int AS orders,
                       (count(*) FILTER (WHERE o.status = 'Confirmed'))::int AS paid_orders
                FROM milkshake.orders AS o
                WHERE o.created_at >= {fromUtc} AND o.created_at < {toUtc}
                GROUP BY 1
                """;
        }
        else
        {
            sql = $"""
                SELECT (o.created_at AT TIME ZONE {business.Id})::date AS day,
                       count(*)::int AS orders,
                       (count(*) FILTER (WHERE o.status = 'Confirmed'))::int AS paid_orders
                FROM milkshake.orders AS o
                GROUP BY 1
                """;
        }

        List<DayRow> days = await db.Database.SqlQuery<DayRow>(sql).ToListAsync(cancellationToken);

        return OrderTrendBuilder.Build(business.Id, query.From, query.To,
            days.Select(d => new DailyOrderCount(d.Day, d.Orders, d.PaidOrders)).ToList());
    }

    /// <summary>The business time zone setting (decision R1); null when it is missing or not a known zone.</summary>
    private async Task<(TimeZoneInfo Zone, string Id)?> BusinessZoneAsync(CancellationToken cancellationToken)
    {
        string? id = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == SettingKeys.PickupTimeZone)
            .Select(s => s.Value)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        try
        {
            return (TimeZoneInfo.FindSystemTimeZoneById(id), id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    /// <summary>The order's own stored pickup zone (its snapshot); the business zone if that zone is unknown here.</summary>
    private static TimeZoneInfo PickupZone(Dictionary<string, TimeZoneInfo> cache, string id, TimeZoneInfo fallback)
    {
        if (!cache.TryGetValue(id, out TimeZoneInfo? zone))
        {
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                zone = fallback;
            }

            cache[id] = zone;
        }

        return zone;
    }

    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private sealed record DayRow(DateOnly Day, int Orders, int PaidOrders);
}
