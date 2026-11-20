using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Milkshake.Application.Abstractions;
using Milkshake.Application.Management;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Auditing;
using Milkshake.Infrastructure.Identity;

namespace Milkshake.Infrastructure.Management;

/// <summary>
/// CSV exports built from the report and audit services themselves, so an export has exactly the rows, payment labels
/// and masking of the screen it came from. Capped at <see cref="CsvWriter.MaxRecords"/> records.
/// </summary>
internal sealed partial class ReportExportService(
    IOrderReportService reports,
    IAuditQueryService audit,
    SecurityEventRecorder securityEvents,
    ICurrentUser currentUser,
    ILogger<ReportExportService> logger) : IReportExportService
{
    /// <summary>Audit records fetched per round trip while exporting.</summary>
    private const int AuditBatch = 1_000;

    public async Task<ExportResult> OrdersAsync(OrderReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        OrderReportPage? page = await reports.OrdersAsync(query with { Page = 1, PageSize = CsvWriter.MaxRecords + 1 }, cancellationToken);
        if (page is null)
        {
            return ExportResult.Failed(ExportStatus.Unavailable);
        }

        if (page.TotalDrinks > CsvWriter.MaxRecords)
        {
            return ExportResult.Failed(ExportStatus.TooLarge);
        }

        string[] header =
        [
            "Date", "Time", "Order", "Milkshake", "Flavour", "Topping", "Consistency", "Restaurant", "Pickup", "Payment status", "Refund",
        ];
        IEnumerable<string?[]> rows = page.Rows.Select(r => new[]
        {
            r.PlacedLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            r.PlacedLocal.ToString("HH:mm", CultureInfo.InvariantCulture),
            r.Reference,
            r.DrinkNumber.ToString(CultureInfo.InvariantCulture),
            r.Flavour,
            r.Topping,
            r.Consistency,
            r.RestaurantName,
            r.PickupLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            r.Payment.Label.ToString(),
            RefundText(r.Payment.Refund),
        });

        string name = $"milkshake-orders_{Period(query.From, query.To)}.csv";
        return await ReadyAsync("orders", new CsvFile(name, CsvWriter.Build(header, rows.ToList()), page.Rows.Count), cancellationToken);
    }

    public async Task<ExportResult> TrendsAsync(OrderTrendsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        OrderTrends? trends = await reports.TrendsAsync(query, cancellationToken);
        if (trends is null)
        {
            return ExportResult.Failed(ExportStatus.Unavailable);
        }

        var rows = new List<string?[]>();
        rows.AddRange(trends.Weekdays.Select(p => Point("Day of the week", p)));
        rows.AddRange(trends.Months.Select(p => Point("Month", p)));
        rows.AddRange(trends.Years.Select(p => Point("Year", p)));

        string name = query.From is { } from && query.To is { } to
            ? $"milkshake-trends_{Period(from, to)}.csv"
            : "milkshake-trends_all.csv";
        return await ReadyAsync("trends", new CsvFile(name, CsvWriter.Build(["Series", "Period", "Orders", "Paid orders"], rows), rows.Count), cancellationToken);
    }

    public async Task<ExportResult> AuditChangesAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var entries = new List<AuditEntryView>();
        AuditQuery next = query with { After = null, Limit = AuditBatch };
        while (true)
        {
            AuditPage<AuditEntryView>? page = await audit.ChangesAsync(next, cancellationToken);
            if (page is null)
            {
                return ExportResult.Failed(ExportStatus.UnknownEntity);
            }

            entries.AddRange(page.Items);
            if (entries.Count > CsvWriter.MaxRecords)
            {
                return ExportResult.Failed(ExportStatus.TooLarge);
            }

            if (AuditCursor.TryDecode(page.NextCursor) is not { } cursor)
            {
                break;
            }

            next = next with { After = cursor };
        }

        // One line per changed field, so each old and new value has its own cell; personal data is already masked.
        var rows = new List<string?[]>();
        foreach (AuditEntryView entry in entries)
        {
            string?[] common =
            [
                Utc(entry.OccurredAt), entry.Action.ToString(), entry.Entity, entry.RecordId, entry.Actor ?? "Not recorded", entry.User,
            ];
            if (entry.Changes.Count == 0)
            {
                rows.Add([.. common, null, null, null]);
            }

            rows.AddRange(entry.Changes.Select(string?[] (c) => [.. common, c.Field, Value(c.Old), Value(c.New)]));
        }

        string[] header = ["Occurred at (UTC)", "Action", "Table", "Record", "Made by", "User", "Field", "From", "To"];
        string name = $"milkshake-audit-changes_{Period(query.From, query.To)}.csv";
        return await ReadyAsync("audit changes", new CsvFile(name, CsvWriter.Build(header, rows), entries.Count), cancellationToken);
    }

    public async Task<ExportResult> SecurityEventsAsync(SecurityEventQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var events = new List<SecurityEventView>();
        SecurityEventQuery next = query with { After = null, Limit = AuditBatch };
        while (true)
        {
            AuditPage<SecurityEventView> page = await audit.SecurityEventsAsync(next, cancellationToken);
            events.AddRange(page.Items);
            if (events.Count > CsvWriter.MaxRecords)
            {
                return ExportResult.Failed(ExportStatus.TooLarge);
            }

            if (AuditCursor.TryDecode(page.NextCursor) is not { } cursor)
            {
                break;
            }

            next = next with { After = cursor };
        }

        string[] header = ["Occurred at (UTC)", "Event", "User", "Address"];
        IEnumerable<string?[]> rows = events.Select(e => new[] { Utc(e.OccurredAt), e.EventType.ToString(), e.User, e.IpAddress });
        string name = $"milkshake-sign-in-events_{Period(query.From, query.To)}.csv";
        return await ReadyAsync("sign-in events", new CsvFile(name, CsvWriter.Build(header, rows.ToList()), events.Count), cancellationToken);
    }

    /// <summary>Records who exported (security event) and what (server log, linked by the request's trace id).</summary>
    private async Task<ExportResult> ReadyAsync(string report, CsvFile file, CancellationToken cancellationToken)
    {
        await securityEvents.RecordAsync(SecurityEventType.ReportExported, currentUser.UserId, currentUser.Email, cancellationToken);
        LogExported(logger, report, file.Records);
        return ExportResult.Ready(file);
    }

    private static string?[] Point(string series, TrendPoint point) =>
        [series, point.Label, point.Orders.ToString(CultureInfo.InvariantCulture), point.PaidOrders.ToString(CultureInfo.InvariantCulture)];

    private static string RefundText(RefundLabel refund) => refund switch
    {
        RefundLabel.NotPaid => "Not paid",
        RefundLabel.RefundInProgress => "Refund in progress",
        RefundLabel.Refunded => "Refunded",
        RefundLabel.RefundedLessFee => "Refunded less fee",
        RefundLabel.RefundFailed => "Refund failed",
        _ => string.Empty,
    };

    private static string Period(DateOnly from, DateOnly to) =>
        from == to
            ? from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : $"{from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}_{to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    private static string Period(DateTimeOffset? from, DateTimeOffset? to) =>
        from is null && to is null
            ? "all"
            : $"{from?.UtcDateTime.ToString("yyyyMMdd'T'HHmm'Z'", CultureInfo.InvariantCulture) ?? "start"}_{to?.UtcDateTime.ToString("yyyyMMdd'T'HHmm'Z'", CultureInfo.InvariantCulture) ?? "now"}";

    private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>A stored value as text: strings without quotes, anything else as JSON; nothing for null.</summary>
    private static string? Value(JsonElement? value) => value switch
    {
        null => null,
        { ValueKind: JsonValueKind.Null } => null,
        { ValueKind: JsonValueKind.String } element => element.GetString(),
        { } element => element.GetRawText(),
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "A manager exported {Report} ({Records} records).")]
    private static partial void LogExported(ILogger logger, string report, int records);
}
