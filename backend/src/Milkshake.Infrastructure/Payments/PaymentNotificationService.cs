using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Milkshake.Application.Payments;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;
using Milkshake.Infrastructure.Persistence;
using Npgsql;
using Milkshake.Application.Abstractions;
using Milkshake.Domain.Auditing;

namespace Milkshake.Infrastructure.Payments;

/// <summary>
/// Settles verified provider notifications (decisions P5, P6). The notification only says "look at this transaction":
/// the outcome, amount and currency come from the provider's verification API. The payment, its order and the
/// notification receipt are saved in ONE SaveChanges, so a repeated notification is a no-op (unique receipt index) and
/// a failure part-way stores nothing, letting the provider's retry process it again.
/// </summary>
internal sealed partial class PaymentNotificationService(
    MilkshakeDbContext db,
    IPaymentGateway gateway,
    RefundSubmitter refunds,
    TimeProvider clock,
    ILogger<PaymentNotificationService> logger) : IPaymentNotificationService
{
    /// <summary>The Paystack event raised for a successful payment. Failures are found by verification (Step 7).</summary>
    public const string ChargeSuccess = "charge.success";

    private const string ReceiptIndex = "ix_payment_events_provider_event_type_provider_transaction_id";
    private const string OneSuccessIndex = "ux_payments_one_success_per_order";

    public async Task<NotificationOutcome> HandleAsync(PaymentNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        using IDisposable actor = AuditActorScope.Begin(AuditActors.PaystackWebhook);   // M11: never the browser user

        if (notification.EventType != ChargeSuccess)
        {
            return NotificationOutcome.Ignored;
        }

        bool seen = await db.PaymentEvents.AnyAsync(e => e.Provider == gateway.Name && e.EventType == notification.EventType &&
            e.ProviderTransactionId == notification.TransactionId, cancellationToken);
        if (seen)
        {
            return NotificationOutcome.Duplicate;
        }

        DateTimeOffset now = clock.GetUtcNow();
        Payment? payment = Payment.IsValidReference(notification.Reference)
            ? await db.Payments.SingleOrDefaultAsync(p => p.Reference == notification.Reference && p.Provider == gateway.Name, cancellationToken)
            : null;
        if (payment is null)
        {
            // Not one of our payments (another integration on the same account, or a test event): note it and move on.
            LogUnknownReference(logger, notification.EventType);
            db.PaymentEvents.Add(Receipt(notification, now));
            return await SaveAsync(NotificationOutcome.Ignored);
        }

        GatewayVerification verified;
        try
        {
            verified = await gateway.VerifyAsync(payment.Reference, cancellationToken);
        }
        catch (PaymentGatewayException)
        {
            return NotificationOutcome.RetryLater;
        }

        if (!verified.Found || verified.Outcome == VerifiedOutcome.Open)
        {
            // The provider does not (yet) confirm what the notification says: let it retry; reconciliation also checks.
            LogNotConfirmed(logger, payment.Reference);
            return NotificationOutcome.RetryLater;
        }

        Order order = await db.Orders.SingleAsync(o => o.Id == payment.OrderId, cancellationToken);
        bool otherSuccess = await db.Payments.AnyAsync(
            p => p.OrderId == order.Id && p.Id != payment.Id && p.Status == PaymentStatus.Succeeded, cancellationToken);
        payment.ApplyVerifiedOutcome(order, verified.Outcome, verified.AmountMinor, verified.CurrencyCode, verified.TransactionId,
            verified.Message, otherSuccess, now);
        if (payment.Status == PaymentStatus.RequiresRefund)
        {
            LogRequiresRefund(logger, payment.Reference, payment.RefundReason?.ToString() ?? "unknown");
        }

        db.PaymentEvents.Add(Receipt(notification, now));
        return await SaveAsync(NotificationOutcome.Processed);
    }

    public async Task<NotificationOutcome> HandleRefundAsync(RefundNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        using IDisposable actor = AuditActorScope.Begin(AuditActors.PaystackWebhook);

        // Paystack treats references case-insensitively; ours are stored in upper case.
        string? refundReference = notification.RefundReference?.Trim().ToUpperInvariant();
        Refund? refund = Refund.IsValidReference(refundReference)
            ? await db.Refunds.SingleOrDefaultAsync(r => r.Reference == refundReference && r.Provider == gateway.Name, cancellationToken)
            : null;
        if (refund is null && Payment.IsValidReference(notification.TransactionReference))
        {
            refund = await db.Refunds.SingleOrDefaultAsync(
                r => r.Provider == gateway.Name && db.Payments.Any(p => p.Id == r.PaymentId && p.Reference == notification.TransactionReference),
                cancellationToken);
        }

        if (refund is null)
        {
            LogUnknownReference(logger, notification.EventType);
            return NotificationOutcome.Ignored;
        }

        // Receipts are not needed: the provider's answer is applied idempotently and Processed / Failed are final.
        GatewayRefund answer;
        try
        {
            answer = await gateway.GetRefundAsync(refund.Reference, cancellationToken);
        }
        catch (PaymentGatewayException)
        {
            return NotificationOutcome.RetryLater;
        }

        if (!answer.Found)
        {
            LogNotConfirmed(logger, refund.Reference);
            return NotificationOutcome.RetryLater;
        }

        if (!refunds.Apply(refund, answer))
        {
            return NotificationOutcome.Duplicate;
        }

        return await SaveAsync(NotificationOutcome.Processed);
    }

    private async Task<NotificationOutcome> SaveAsync(NotificationOutcome outcome)
    {
        try
        {
            // Not cancellable: once verified, the result is worth storing even if the provider closed the connection.
            await db.SaveChangesAsync(CancellationToken.None);
            return outcome;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return NotificationOutcome.RetryLater;
        }
        catch (DbUpdateException exception) when (ViolatedIndex(exception) == ReceiptIndex)
        {
            // The same notification was delivered twice at the same moment and the other delivery won.
            db.ChangeTracker.Clear();
            return NotificationOutcome.Duplicate;
        }
        catch (DbUpdateException exception) when (ViolatedIndex(exception) == OneSuccessIndex)
        {
            // Another attempt for this order succeeded at the same moment; on retry this one is kept for refund.
            db.ChangeTracker.Clear();
            return NotificationOutcome.RetryLater;
        }
    }

    private PaymentEvent Receipt(PaymentNotification notification, DateTimeOffset now) =>
        new(gateway.Name, notification.EventType, notification.TransactionId, notification.Reference[..Math.Min(notification.Reference.Length, 100)], now);

    private static string? ViolatedIndex(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres ? postgres.ConstraintName : null;

    [LoggerMessage(Level = LogLevel.Information, Message = "Ignored a verified {EventType} notification for a reference that is not one of our payments.")]
    private static partial void LogUnknownReference(ILogger logger, string eventType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The provider did not confirm the notified outcome for {Reference}; asking for a retry.")]
    private static partial void LogNotConfirmed(ILogger logger, string reference);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment {PaymentReference} was received but cannot be accepted ({Reason}); kept for refund.")]
    private static partial void LogRequiresRefund(ILogger logger, string paymentReference, string reason);
}
