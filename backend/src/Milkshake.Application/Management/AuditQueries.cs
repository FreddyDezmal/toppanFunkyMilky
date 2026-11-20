using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Milkshake.Domain.Auditing;

namespace Milkshake.Application.Management;

/// <summary>Filters for the change history (AC-11.4). Dates are UTC; <see cref="To"/> is exclusive.</summary>
public sealed record AuditQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? User,
    string? Actor,
    string? Entity,
    AuditAction? Action,
    string? Text,
    AuditCursor? After,
    int Limit);

/// <summary>Filters for sign-in and sign-out events (T11.3).</summary>
public sealed record SecurityEventQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? User,
    SecurityEventType? EventType,
    AuditCursor? After,
    int Limit);

/// <summary>One changed field. Values are as stored (JSON), with personal data masked for display.</summary>
public sealed record AuditFieldChange(string Field, JsonElement? Old, JsonElement? New);

/// <summary>One audit record as a manager sees it.</summary>
public sealed record AuditEntryView(
    long Id,
    DateTimeOffset OccurredAt,
    AuditAction Action,
    string Entity,
    string RecordId,
    string? Actor,
    Guid? UserId,
    string? User,
    IReadOnlyList<AuditFieldChange> Changes,
    string? TraceId);

/// <summary>One security event as a manager sees it (address and IP masked).</summary>
public sealed record SecurityEventView(long Id, DateTimeOffset OccurredAt, SecurityEventType EventType, Guid? UserId, string? User, string? IpAddress);

/// <summary>A page of results, newest first; <see cref="NextCursor"/> continues after the last item (null at the end).</summary>
public sealed record AuditPage<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>Management → audit history (M11, AC-11.4). Read-only; records are never changed here.</summary>
public interface IAuditQueryService
{
    /// <summary>Null when <see cref="AuditQuery.Entity"/> is not an audited table.</summary>
    Task<AuditPage<AuditEntryView>?> ChangesAsync(AuditQuery query, CancellationToken cancellationToken);

    Task<AuditPage<SecurityEventView>> SecurityEventsAsync(SecurityEventQuery query, CancellationToken cancellationToken);
}

/// <summary>Keyset position: newest first by time, then id. Opaque to clients (base64url).</summary>
public sealed record AuditCursor(DateTimeOffset OccurredAt, long Id)
{
    public string Encode() =>
        Convert.ToBase64String(Encoding.ASCII.GetBytes(
                OccurredAt.UtcTicks.ToString(CultureInfo.InvariantCulture) + ":" + Id.ToString(CultureInfo.InvariantCulture)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static AuditCursor? TryDecode(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64)
        {
            return null;
        }

        try
        {
            string base64 = value.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            string[] parts = Encoding.ASCII.GetString(Convert.FromBase64String(base64)).Split(':');
            return parts.Length == 2 &&
                   long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks) &&
                   long.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long id) &&
                   ticks >= DateTimeOffset.MinValue.UtcTicks && ticks <= DateTimeOffset.MaxValue.UtcTicks
                ? new AuditCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id)
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>
/// Strict validation of the query string (prompt §9): known filters only, one value each, bounded lengths, dates as
/// ISO 8601 (UTC), a range of at most 366 days, 1–100 results per page.
/// </summary>
public static partial class AuditQueryParser
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;
    public const int MaxTextLength = 100;
    public const int MaxUserLength = 256;
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(366);

    private static readonly string[] ChangeKeys = ["from", "to", "user", "actor", "entity", "action", "q", "cursor", "limit"];
    private static readonly string[] SecurityKeys = ["from", "to", "user", "eventType", "cursor", "limit"];

    public static AuditQuery? ParseChanges(IReadOnlyDictionary<string, string[]> query, IDictionary<string, string> errors)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(errors);
        Dictionary<string, string> values = Single(query, ChangeKeys, errors);
        (DateTimeOffset? from, DateTimeOffset? to) = Range(values, errors);

        string? entity = Value(values, "entity");
        if (entity is not null && !EntityRegex().IsMatch(entity))
        {
            errors["entity"] = "Use a table name such as orders or flavour_prices.";
        }

        string? actor = Value(values, "actor");
        if (actor is not null && !AuditActors.IsValid(actor))
        {
            errors["actor"] = "Use user, anonymous or a system name such as system:payment-reconciliation.";
        }

        AuditAction? action = EnumValue<AuditAction>(values, "action", "Use Insert, Update or Delete.", errors);
        string? text = Text(values, "q", MaxTextLength, errors);
        string? user = Text(values, "user", MaxUserLength, errors);
        AuditCursor? after = Cursor(values, errors);
        int limit = Limit(values, errors);

        return errors.Count > 0 ? null : new AuditQuery(from, to, user, actor, entity, action, text, after, limit);
    }

    public static SecurityEventQuery? ParseSecurity(IReadOnlyDictionary<string, string[]> query, IDictionary<string, string> errors)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(errors);
        Dictionary<string, string> values = Single(query, SecurityKeys, errors);
        (DateTimeOffset? from, DateTimeOffset? to) = Range(values, errors);
        string? user = Text(values, "user", MaxUserLength, errors);
        SecurityEventType? type = EnumValue<SecurityEventType>(values, "eventType", "Use LoginSucceeded, LoginFailed, LoginLockedOut or Logout.", errors);
        AuditCursor? after = Cursor(values, errors);
        int limit = Limit(values, errors);

        return errors.Count > 0 ? null : new SecurityEventQuery(from, to, user, type, after, limit);
    }

    private static Dictionary<string, string> Single(IReadOnlyDictionary<string, string[]> query, string[] allowed, IDictionary<string, string> errors)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string[] given) in query)
        {
            string? known = allowed.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (known is null)
            {
                errors[key] = $"Unknown filter. Use: {string.Join(", ", allowed)}.";
            }
            else if (given.Length != 1)
            {
                errors[known] = "Give this filter once.";
            }
            else if (!string.IsNullOrWhiteSpace(given[0]))
            {
                values[known] = given[0].Trim();
            }
        }

        return values;
    }

    private static string? Value(Dictionary<string, string> values, string key) => values.GetValueOrDefault(key);

    private static (DateTimeOffset? From, DateTimeOffset? To) Range(Dictionary<string, string> values, IDictionary<string, string> errors)
    {
        DateTimeOffset? from = Date(values, "from", errors);
        DateTimeOffset? to = Date(values, "to", errors);
        if (from is { } start && to is { } end)
        {
            if (end <= start)
            {
                errors["to"] = "The end must be after the start.";
            }
            else if (end - start > MaxRange)
            {
                errors["to"] = "Choose a period of at most 366 days.";
            }
        }

        return (from, to);
    }

    private static DateTimeOffset? Date(Dictionary<string, string> values, string key, IDictionary<string, string> errors)
    {
        if (Value(values, key) is not { } raw)
        {
            return null;
        }

        if (raw.Length <= 40 && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed) &&
            parsed.Year is >= 2000 and <= 9998)
        {
            return parsed.ToUniversalTime();
        }

        errors[key] = "Use an ISO 8601 date and time, such as 2026-11-20T00:00:00Z.";
        return null;
    }

    private static TEnum? EnumValue<TEnum>(Dictionary<string, string> values, string key, string message, IDictionary<string, string> errors)
        where TEnum : struct, Enum
    {
        if (Value(values, key) is not { } raw)
        {
            return null;
        }

        if (!raw.All(char.IsAsciiDigit) && Enum.TryParse(raw, ignoreCase: true, out TEnum parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        errors[key] = message;
        return null;
    }

    private static string? Text(Dictionary<string, string> values, string key, int maxLength, IDictionary<string, string> errors)
    {
        if (Value(values, key) is not { } raw)
        {
            return null;
        }

        if (raw.Length > maxLength || raw.Any(char.IsControl))
        {
            errors[key] = $"Use at most {maxLength} characters, without line breaks.";
            return null;
        }

        return raw;
    }

    private static AuditCursor? Cursor(Dictionary<string, string> values, IDictionary<string, string> errors)
    {
        if (Value(values, "cursor") is not { } raw)
        {
            return null;
        }

        AuditCursor? cursor = AuditCursor.TryDecode(raw);
        if (cursor is null)
        {
            errors["cursor"] = "This page link is not valid. Start again from the first page.";
        }

        return cursor;
    }

    private static int Limit(Dictionary<string, string> values, IDictionary<string, string> errors)
    {
        if (Value(values, "limit") is not { } raw)
        {
            return DefaultLimit;
        }

        if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int limit) && limit is >= 1 and <= MaxLimit)
        {
            return limit;
        }

        errors["limit"] = $"Use a number from 1 to {MaxLimit}.";
        return DefaultLimit;
    }

    [GeneratedRegex("^[a-z][a-z_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityRegex();
}

/// <summary>
/// What a manager may see of personal data in the audit history (decision 4, provisional pending Q-14): the values
/// stay stored as they are; account details are masked on output, and free-text search never looks inside them.
/// </summary>
public static class AuditMasking
{
    public const string Masked = "***";

    /// <summary>Tables whose field values are personal data; free-text search skips their changes.</summary>
    public static readonly IReadOnlySet<string> PersonalTables =
        new HashSet<string>(["users", "user_tokens", "user_claims", "user_logins"], StringComparer.Ordinal);

    private static readonly Dictionary<string, HashSet<string>> PersonalFields = new(StringComparer.Ordinal)
    {
        ["users"] = new(["email", "normalized_email", "user_name", "normalized_user_name", "phone_number", "first_name", "last_name"], StringComparer.Ordinal),
        ["user_claims"] = new(["claim_value"], StringComparer.Ordinal),
        ["user_logins"] = new(["provider_key", "provider_display_name"], StringComparer.Ordinal),
        ["user_tokens"] = new(["value"], StringComparer.Ordinal),
        ["email_notifications"] = new(["recipient_email", "recipient_name", "payload"], StringComparer.Ordinal),
    };

    private static readonly JsonElement MaskedValue = JsonDocument.Parse("\"***\"").RootElement.Clone();

    /// <summary><c>t***@example.test</c>; anything that is not a plain address becomes <c>***</c>.</summary>
    public static string MaskEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        int at = email.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 || at != email.LastIndexOf('@') || at == email.Length - 1
            ? Masked
            : email[0] + Masked + email[at..];
    }

    /// <summary>The network only: <c>196.21.x.x</c>, or the first two groups of an IPv6 address.</summary>
    public static string MaskIp(string ip)
    {
        ArgumentNullException.ThrowIfNull(ip);
        if (System.Net.IPAddress.TryParse(ip, out System.Net.IPAddress? address))
        {
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                string[] octets = address.ToString().Split('.');
                return $"{octets[0]}.{octets[1]}.x.x";
            }

            string[] groups = address.ToString().Split(':');
            return $"{groups[0]}:{(groups.Length > 1 ? groups[1] : "0")}:…";
        }

        return Masked;
    }

    /// <summary>Parses a stored change list and masks personal fields. Unreadable content is shown as one masked entry.</summary>
    public static IReadOnlyList<AuditFieldChange> Changes(string table, string json)
    {
        ArgumentNullException.ThrowIfNull(table);
        HashSet<string>? personal = PersonalFields.GetValueOrDefault(table);
        var result = new List<AuditFieldChange>();
        try
        {
            using JsonDocument document = JsonDocument.Parse(json ?? "[]");
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [new AuditFieldChange("(unreadable)", MaskedValue, MaskedValue)];
            }

            foreach (JsonElement change in document.RootElement.EnumerateArray())
            {
                if (change.ValueKind != JsonValueKind.Object || !change.TryGetProperty("field", out JsonElement fieldElement) ||
                    fieldElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string field = fieldElement.GetString()!;
                bool mask = personal?.Contains(field) == true;
                result.Add(new AuditFieldChange(field, Value(change, "old", mask), Value(change, "new", mask)));
            }
        }
        catch (JsonException)
        {
            return [new AuditFieldChange("(unreadable)", MaskedValue, MaskedValue)];
        }

        return result;
    }

    private static JsonElement? Value(JsonElement change, string name, bool mask)
    {
        if (!change.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return mask ? MaskedValue : value.Clone();
    }
}
