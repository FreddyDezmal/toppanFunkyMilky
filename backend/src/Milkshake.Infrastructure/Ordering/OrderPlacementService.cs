using Microsoft.EntityFrameworkCore;
using Milkshake.Application.Abstractions;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Pricing;
using Milkshake.Infrastructure.Persistence;
using Npgsql;

namespace Milkshake.Infrastructure.Ordering;

/// <summary>
/// Places orders (M7). Order of work:
/// 1. If this patron already used the key, return that order (same content) or refuse (different content).
/// 2. Revalidate and reprice the draft with <see cref="IOrderPricingService.PriceDraftAsync"/> (one pricing instant).
/// 3. If the new total or currency differs from what the patron confirmed, return the new price and store nothing.
/// 4. Store the order, its items and their audit rows with ONE SaveChanges call, i.e. one database transaction.
/// Step 1 is only a shortcut: the unique index on (patron_id, idempotency_key) is what guarantees one order per key,
/// including for simultaneous requests and for a retry after an unclear commit (decision O7).
/// </summary>
internal sealed class OrderPlacementService(
    MilkshakeDbContext db,
    IOrderPricingService pricing,
    ICurrentUser currentUser) : IOrderPlacementService
{
    private const string IdempotencyIndex = "ix_orders_patron_id_idempotency_key";
    private const string ReferenceIndex = "ix_orders_reference";

    /// <summary>A new random reference is tried if one collides (about 1 in 10^12 per order).</summary>
    private const int ReferenceAttempts = 3;

    public async Task<PlaceOrderResult> PlaceAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Guid patronId = currentUser.UserId
            ?? throw new InvalidOperationException("Placing an order requires a signed-in patron.");
        string fingerprint = OrderRequestFingerprint.Compute(command.Draft);

        if (await EarlierSubmissionAsync(patronId, command.IdempotencyKey, fingerprint, cancellationToken) is { } earlier)
        {
            return earlier;
        }

        DraftPricingResult priced = await pricing.PriceDraftAsync(command.Draft, cancellationToken);
        if (priced.OrderingUnavailable)
        {
            return PlaceOrderResult.Unavailable();
        }

        if (priced.Draft is not ValidatedDraft draft || priced.Price is not PriceBreakdown price)
        {
            return PlaceOrderResult.Invalid(priced.Errors);
        }

        // Comparison only: the expected amount never feeds the calculation (decision O6).
        if (price.Total.Amount != command.ExpectedTotal ||
            !string.Equals(price.CurrencyCode, command.ExpectedCurrencyCode, StringComparison.Ordinal))
        {
            return PlaceOrderResult.PriceChanged(price);
        }

        PriceBreakdown snapshot = price with { PricedAtUtc = price.PricedAtUtc.ToUniversalTime() };
        var restaurant = new OrderRestaurant(draft.Restaurant.Id, draft.Restaurant.Name, draft.Restaurant.Address);

        for (int attempt = 1; ; attempt++)
        {
            Order order = Order.Place(
                patronId,
                OrderReference.New(),
                restaurant,
                draft.Pickup.Utc.ToUniversalTime(),
                draft.Pickup.TimeZoneId,
                snapshot,
                command.IdempotencyKey,
                fingerprint);

            db.Orders.Add(order);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return PlaceOrderResult.Created(order);
            }
            catch (DbUpdateException exception) when (ViolatedIndex(exception) == IdempotencyIndex)
            {
                // A simultaneous request, or a retry of a commit that did succeed, stored this key first.
                db.ChangeTracker.Clear();
                return await EarlierSubmissionAsync(patronId, command.IdempotencyKey, fingerprint, cancellationToken)
                    ?? throw new InvalidOperationException("The idempotency key was reported as used, but no order has it.", exception);
            }
            catch (DbUpdateException exception) when (ViolatedIndex(exception) == ReferenceIndex && attempt < ReferenceAttempts)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<PlaceOrderResult?> EarlierSubmissionAsync(
        Guid patronId, Guid idempotencyKey, string fingerprint, CancellationToken cancellationToken)
    {
        Order? existing = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.PatronId == patronId && o.IdempotencyKey == idempotencyKey, cancellationToken);

        if (existing is null)
        {
            return null;
        }

        return string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal)
            ? PlaceOrderResult.Replayed(existing)
            : PlaceOrderResult.KeyReused();
    }

    private static string? ViolatedIndex(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
            ? postgres.ConstraintName
            : null;
}
