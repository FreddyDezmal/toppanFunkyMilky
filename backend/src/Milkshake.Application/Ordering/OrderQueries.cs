using System.Globalization;
using System.Text;
using Milkshake.Domain.Orders;

namespace Milkshake.Application.Ordering;

/// <summary>
/// Reads the signed-in patron's own orders (AC-7.6, AC-12.1). Every query is filtered by the current patron, so another
/// patron's order is indistinguishable from one that does not exist.
/// </summary>
public interface IOrderQueryService
{
    /// <summary>The patron's order with this reference and its items, or null if there is none for this patron.</summary>
    Task<Order?> FindOwnAsync(string reference, CancellationToken cancellationToken);

    /// <summary>
    /// One page of the patron's order history, newest first (placed time, then id), with each order's items and its
    /// payment state. <paramref name="after"/> is the previous page's <see cref="OrderHistoryPage.Next"/>.
    /// </summary>
    Task<OrderHistoryPage> ListOwnAsync(OrderCursor? after, int limit, CancellationToken cancellationToken);
}

/// <summary>An order from the patron's history with its payment state, from stored payment and refund records.</summary>
public sealed record OrderHistoryEntry(Order Order, PaymentState Payment);

/// <summary>A page of order history. <see cref="Next"/> is null on the last page.</summary>
public sealed record OrderHistoryPage(IReadOnlyList<OrderHistoryEntry> Items, OrderCursor? Next);

/// <summary>Keyset position in the order history: newest first by placed time, then id. Opaque to clients (base64url).</summary>
public sealed record OrderCursor(DateTimeOffset CreatedAt, int Id)
{
    public string Encode() =>
        Convert.ToBase64String(Encoding.ASCII.GetBytes(
                CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture) + ":" + Id.ToString(CultureInfo.InvariantCulture)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static OrderCursor? TryDecode(string? value)
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
                   int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int id) &&
                   ticks >= DateTimeOffset.MinValue.UtcTicks && ticks <= DateTimeOffset.MaxValue.UtcTicks && id > 0
                ? new OrderCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id)
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>A validated order-history request.</summary>
public sealed record OrderHistoryQuery(OrderCursor? After, int Limit);

/// <summary>Strict validation of GET /api/ordering/orders: only <c>cursor</c> and <c>limit</c>, each at most once.</summary>
public static class OrderHistoryQueryParser
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    private static readonly string[] Keys = ["cursor", "limit"];

    public static OrderHistoryQuery? Parse(IReadOnlyDictionary<string, string[]> query, IDictionary<string, string> errors)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(errors);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string[] given) in query)
        {
            string? known = Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (known is null)
            {
                errors[key] = "Unknown parameter. Use: cursor, limit.";
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

        OrderCursor? after = null;
        if (values.TryGetValue("cursor", out string? rawCursor))
        {
            after = OrderCursor.TryDecode(rawCursor);
            if (after is null)
            {
                errors["cursor"] = "This page link is not valid. Start again from the first page.";
            }
        }

        int limit = DefaultLimit;
        if (values.TryGetValue("limit", out string? rawLimit) &&
            !(int.TryParse(rawLimit, NumberStyles.None, CultureInfo.InvariantCulture, out limit) && limit is >= 1 and <= MaxLimit))
        {
            errors["limit"] = $"Use a number from 1 to {MaxLimit}.";
        }

        return errors.Count > 0 ? null : new OrderHistoryQuery(after, limit);
    }
}
