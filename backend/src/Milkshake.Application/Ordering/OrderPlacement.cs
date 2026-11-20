using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Pricing;

namespace Milkshake.Application.Ordering;

/// <summary>
/// A request to place an order (M7). <see cref="ExpectedTotal"/> and <see cref="ExpectedCurrencyCode"/> are the amount
/// the patron last saw: they are only compared with the server's new price (decision O6) and never used to calculate
/// it. <see cref="IdempotencyKey"/> identifies this submission so a retry cannot create a second order (decision O5).
/// </summary>
public sealed record PlaceOrderCommand(OrderDraft Draft, decimal ExpectedTotal, string ExpectedCurrencyCode, Guid IdempotencyKey);

public enum PlaceOrderOutcome
{
    /// <summary>A new order was stored.</summary>
    Created,

    /// <summary>This patron already placed an order with this key and the same content: that order is returned.</summary>
    Replayed,

    /// <summary>The draft is no longer valid (see the field errors). Nothing was stored.</summary>
    Invalid,

    /// <summary>The server's price differs from the one the patron confirmed. Nothing was stored.</summary>
    PriceChanged,

    /// <summary>The key was already used by this patron for a different order. Nothing was stored.</summary>
    KeyReused,

    /// <summary>Ordering configuration is invalid. Nothing was stored.</summary>
    Unavailable,
}

public sealed record PlaceOrderResult(
    PlaceOrderOutcome Outcome,
    Order? Order,
    PriceBreakdown? CurrentPrice,
    IReadOnlyDictionary<string, string[]> Errors)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    public static PlaceOrderResult Created(Order order) => new(PlaceOrderOutcome.Created, order, null, NoErrors);

    public static PlaceOrderResult Replayed(Order order) => new(PlaceOrderOutcome.Replayed, order, null, NoErrors);

    public static PlaceOrderResult Invalid(IReadOnlyDictionary<string, string[]> errors) => new(PlaceOrderOutcome.Invalid, null, null, errors);

    public static PlaceOrderResult PriceChanged(PriceBreakdown current) => new(PlaceOrderOutcome.PriceChanged, null, current, NoErrors);

    public static PlaceOrderResult KeyReused() => new(PlaceOrderOutcome.KeyReused, null, null, NoErrors);

    public static PlaceOrderResult Unavailable() => new(PlaceOrderOutcome.Unavailable, null, null, NoErrors);
}

/// <summary>Places orders for the signed-in patron. The single authoritative way an order is created.</summary>
public interface IOrderPlacementService
{
    /// <summary>
    /// Revalidates and reprices the draft on the server, then stores the order and its items in one transaction.
    /// Never trusts an amount from the browser; never stores anything when the result is not Created.
    /// </summary>
    Task<PlaceOrderResult> PlaceAsync(PlaceOrderCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// SHA-256 of a draft's content (restaurant, pickup time and each milkshake's choices, in order). Two requests with the
/// same key are the same submission only if their fingerprints match. The expected total is deliberately left out, so a
/// patron who confirms a changed price can resubmit with the same key.
/// </summary>
public static class OrderRequestFingerprint
{
    public static string Compute(OrderDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var canonical = new StringBuilder();
        canonical.Append(CultureInfo.InvariantCulture, $"r={Value(draft.RestaurantId)};p={draft.PickupLocal ?? "-"};d=");
        foreach (DraftDrinkSelection? drink in draft.Drinks)
        {
            canonical.Append(drink is null
                ? "[-]"
                : string.Create(CultureInfo.InvariantCulture, $"[{Value(drink.FlavourId)},{Value(drink.ConsistencyId)},{Value(drink.ToppingId)}]"));
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static string Value(int? id) => id?.ToString(CultureInfo.InvariantCulture) ?? "-";
}
