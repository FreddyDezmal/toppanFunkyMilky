using System.Text;

namespace Milkshake.Application.Management;

/// <summary>A CSV download: its file name, its bytes (UTF-8 with a byte-order mark) and how many records it holds.</summary>
public sealed record CsvFile(string FileName, byte[] Content, int Records);

public enum ExportStatus
{
    Ready,

    /// <summary>More than <see cref="CsvWriter.MaxRecords"/> records match: narrow the filters.</summary>
    TooLarge,

    /// <summary>The business time zone setting is unusable (the reports are unavailable).</summary>
    Unavailable,

    /// <summary>The audit table filter names no audited table.</summary>
    UnknownEntity,
}

public sealed record ExportResult(ExportStatus Status, CsvFile? File)
{
    public static ExportResult Ready(CsvFile file) => new(ExportStatus.Ready, file);

    public static ExportResult Failed(ExportStatus status) => new(status, null);
}

/// <summary>
/// Report exports (AC-12.6): the same filters, data and masking as the screens, as CSV. Every export is recorded as a
/// security event (who and when) and in the server log (which report and how many records).
/// </summary>
public interface IReportExportService
{
    Task<ExportResult> OrdersAsync(OrderReportQuery query, CancellationToken cancellationToken);

    Task<ExportResult> TrendsAsync(OrderTrendsQuery query, CancellationToken cancellationToken);

    Task<ExportResult> AuditChangesAsync(AuditQuery query, CancellationToken cancellationToken);

    Task<ExportResult> SecurityEventsAsync(SecurityEventQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// RFC 4180 CSV that spreadsheets open safely: a cell starting with =, +, -, @, tab or carriage return gets a leading
/// apostrophe so it is shown as text and never run as a formula (CSV injection); cells with commas, quotes or line
/// breaks are quoted. UTF-8 with a byte-order mark so Excel reads accents correctly; CRLF line endings.
/// </summary>
public static class CsvWriter
{
    /// <summary>The most records one export may hold (decision R5).</summary>
    public const int MaxRecords = 10_000;

    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];
    private static readonly char[] NeedsQuotes = [',', '"', '\r', '\n'];

    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string safe = FormulaStarts.Contains(value[0]) ? "'" + value : value;
        return safe.IndexOfAny(NeedsQuotes) >= 0 || safe[0] == ' ' || safe[^1] == ' '
            ? "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : safe;
    }

    public static byte[] Build(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rows);

        var text = new StringBuilder();
        text.Append(string.Join(',', header.Select(Cell))).Append("\r\n");
        foreach (IReadOnlyList<string?> row in rows)
        {
            text.Append(string.Join(',', row.Select(Cell))).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }
}
