using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Milkshake.Application.Abstractions;
using Milkshake.Application.Payments;
using Milkshake.Domain.Common;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;
using Milkshake.Infrastructure.Persistence;
using Npgsql;

namespace Milkshake.Infrastructure.Payments;

/// <summary>
/// Starts payments (decision P4). Order of work:
/// 1. The patron's own order, awaiting payment; the same Idempotency-Key replays its attempt.
/// 2. An open attempt is checked with the provider first: paid → the order is confirmed; still open → the same checkout
///    is resumed (no second transaction); otherwise it is ended.
/// 3. A new attempt is stored as Initiating and COMMITTED before the provider is called, so a crash or timeout during
///    the call leaves a reference that can be verified later. No database transaction is held during the call.
/// 4. The checkout address is validated (https, provider host) and the attempt becomes Pending.
/// The partial unique index "one open attempt per order" settles simultaneous requests.
/// </summary>
internal sealed partial class PaymentService(
    MilkshakeDbContext db,
    IPaymentGateway gateway,
    ICurrentUser currentUser,
    TimeProvider clock,
    IOptions<PaystackOptions> options,
    ILogger<PaymentService> logger) : IPaymentService
{
    /// <summary>An Initiating attempt younger than this is assumed to belong to a request still in progress.</summary>
    internal static readonly TimeSpan InitiatingGrace = TimeSpan.FromMinutes(1);

    private const string OneOpenIndex = "ux_payments_one_open_per_order";
    private const string KeyIndex = "ix_payments_order_id_idempotency_key";

    public async Task<StartPaymentResult> StartAsync(string orderReference, Guid idempotencyKey, CancellationToken cancellationToken)
    {
        Guid patronId = currentUser.UserId ?? throw new InvalidOperationException("Paying requires a signed-in patron.");
        if (!OrderReference.IsValid(orderReference))
        {
            return Result(StartPaymentOutcome.NotFound);
        }

        Order? order = await db.Orders.SingleOrDefaultAsync(o => o.PatronId == patronId && o.Reference == orderReference, cancellationToken);
        if (order is null)
        {
            return Result(StartPaymentOutcome.NotFound);
        }

        if (StateOf(order) is { } finished)
        {
            return finished;
        }

        Payment? sameKey = await db.Payments.SingleOrDefaultAsync(p => p.OrderId == order.Id && p.IdempotencyKey == idempotencyKey, cancellationToken);
        if (sameKey is not null)
        {
            return Replay(sameKey);
        }

        DateTimeOffset now = clock.GetUtcNow();
        Payment? open = await db.Payments.SingleOrDefaultAsync(
            p => p.OrderId == order.Id && (p.Status == PaymentStatus.Initiating || p.Status == PaymentStatus.Pending), cancellationToken);
        if (open is not null)
        {
            StartPaymentResult? resolved = await ResolveOpenAttemptAsync(order, open, now, cancellationToken);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        // Decision A2: no new checkout once the pickup time has passed (an open attempt that turned out paid still counts, P9).
        if (order.PickupAtUtc <= now)
        {
            return Result(StartPaymentOutcome.PickupPassed);
        }

        if (currentUser.Email is not { Length: > 0 } email || options.Value.ReturnBaseUrl is not { IsAbsoluteUri: true } returnBase)
        {
            LogNotConfigured(logger);
            return Result(StartPaymentOutcome.GatewayUnavailable);
        }

        Payment payment = Payment.Start(order, Payment.NewReference(), gateway.Name, idempotencyKey);
        db.Payments.Add(payment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (ViolatedIndex(exception) == OneOpenIndex)
        {
            db.ChangeTracker.Clear();
            return Result(StartPaymentOutcome.InProgress);
        }
        catch (DbUpdateException exception) when (ViolatedIndex(exception) == KeyIndex)
        {
            db.ChangeTracker.Clear();
            return Replay(await db.Payments.AsNoTracking().SingleAsync(p => p.OrderId == order.Id && p.IdempotencyKey == idempotencyKey, cancellationToken));
        }

        Uri checkout;
        try
        {
            var request = new CheckoutRequest(payment.Reference, payment.AmountMinor, payment.CurrencyCode, email,
                new Uri(returnBase, $"orders/{Uri.EscapeDataString(order.Reference)}?payment=return"));
            checkout = await gateway.CreateCheckoutAsync(request, cancellationToken);
        }
        catch (PaymentGatewayException)
        {
            // The provider may or may not have created the transaction. The attempt stays Initiating with our reference
            // and is settled by verifying it (next attempt or the reconciliation job). Nothing was charged here.
            return Result(StartPaymentOutcome.GatewayUnavailable);
        }

        try
        {
            payment.CheckoutCreated(checkout, gateway.CheckoutHost);
        }
        catch (BusinessRuleException)
        {
            LogUnexpectedCheckout(logger, payment.Reference);
            payment.MarkAbandoned("Unexpected checkout address.", now);
            await db.SaveChangesAsync(CancellationToken.None);
            return Result(StartPaymentOutcome.GatewayUnavailable);
        }

        // Not cancellable: the checkout exists at the provider, so its address is worth keeping even if the patron left.
        await db.SaveChangesAsync(CancellationToken.None);
        return Checkout(payment);
    }

    /// <summary>Returns a final answer, or null when a new attempt should be created.</summary>
    private async Task<StartPaymentResult?> ResolveOpenAttemptAsync(Order order, Payment open, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (open.Status == PaymentStatus.Initiating && now - open.CreatedAt < InitiatingGrace)
        {
            return Result(StartPaymentOutcome.InProgress);
        }

        GatewayVerification verified;
        try
        {
            verified = await gateway.VerifyAsync(open.Reference, cancellationToken);
        }
        catch (PaymentGatewayException)
        {
            return Result(StartPaymentOutcome.GatewayUnavailable);
        }

        if (!verified.Found)
        {
            open.MarkAbandoned("No transaction at the provider.", now);
        }
        else if (verified.Outcome == VerifiedOutcome.Open)
        {
            if (open.Status == PaymentStatus.Pending && open.CheckoutUrl is not null)
            {
                // Still payable at the provider: resume the same checkout rather than start a second transaction.
                return Checkout(open);
            }

            open.Supersede();
        }
        else
        {
            bool otherSuccess = await db.Payments.AnyAsync(
                p => p.OrderId == order.Id && p.Id != open.Id && p.Status == PaymentStatus.Succeeded, cancellationToken);
            open.ApplyVerifiedOutcome(order, verified.Outcome, verified.AmountMinor, verified.CurrencyCode, verified.TransactionId,
                verified.Message, otherSuccess, now);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A notification changed the order or the attempt at the same moment: let the patron try again.
            db.ChangeTracker.Clear();
            return Result(StartPaymentOutcome.InProgress);
        }

        return StateOf(order);
    }

    private static StartPaymentResult? StateOf(Order order) =>
        order.Status == OrderStatus.Confirmed ? Result(StartPaymentOutcome.AlreadyPaid)
        : !OrderStatusTransitions.AwaitsPayment(order.Status) ? Result(StartPaymentOutcome.NotPayable)
        : null;

    private static StartPaymentResult Replay(Payment payment) => payment.Status switch
    {
        PaymentStatus.Pending when payment.CheckoutUrl is not null => Checkout(payment),
        PaymentStatus.Succeeded => Result(StartPaymentOutcome.AlreadyPaid),
        PaymentStatus.Initiating => Result(StartPaymentOutcome.InProgress),
        _ => Result(StartPaymentOutcome.KeyUsed),
    };

    private static StartPaymentResult Checkout(Payment payment) =>
        new(StartPaymentOutcome.Checkout, payment.Reference, new Uri(payment.CheckoutUrl!));

    private static StartPaymentResult Result(StartPaymentOutcome outcome) => new(outcome);

    private static string? ViolatedIndex(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres ? postgres.ConstraintName : null;

    [LoggerMessage(Level = LogLevel.Error, Message = "Payments are not configured: Payments:Paystack:ReturnBaseUrl (or the patron's e-mail) is missing.")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "The payment provider returned an unexpected checkout address for payment {PaymentReference}.")]
    private static partial void LogUnexpectedCheckout(ILogger logger, string paymentReference);
}
