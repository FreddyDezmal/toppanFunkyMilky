using Microsoft.EntityFrameworkCore;
using Milkshake.Application.Payments;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Infrastructure.Payments;

/// <summary>
/// Settles one open attempt by asking the provider (decision P7), used by the patron refresh and the reconciliation job.
/// Two allowances: a very new Initiating attempt is left alone (its request may still be running), and Paystack reports
/// a checkout nobody has paid yet as "abandoned", so for <see cref="CheckoutGrace"/> that counts as still open – the
/// patron may be on the payment page. Attempts still open after <see cref="MaxAge"/> are ended; nothing was paid.
/// </summary>
internal sealed class PaymentSettler(MilkshakeDbContext db, IPaymentGateway gateway, TimeProvider clock)
{
    internal static readonly TimeSpan CheckoutGrace = TimeSpan.FromMinutes(30);

    /// <summary>Matches Paystack's webhook retry window: after this, no notification can still be on its way.</summary>
    internal static readonly TimeSpan MaxAge = TimeSpan.FromHours(72);

    /// <summary>
    /// Returns true if the attempt (and possibly its order) changed and was saved. Throws
    /// <see cref="PaymentGatewayException"/> if the provider cannot be asked, and <see cref="DbUpdateConcurrencyException"/>
    /// if a notification changed the same rows at the same moment; both leave everything unchanged.
    /// </summary>
    public async Task<bool> SettleAsync(Payment payment, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        TimeSpan age = now - payment.CreatedAt;
        if (!PaymentStatusTransitions.IsOpen(payment.Status) ||
            (payment.Status == PaymentStatus.Initiating && age < PaymentService.InitiatingGrace))
        {
            return false;
        }

        GatewayVerification verified = await gateway.VerifyAsync(payment.Reference, cancellationToken);

        bool stillOpen = verified.Found &&
            (verified.Outcome == VerifiedOutcome.Open || (verified.Outcome == VerifiedOutcome.Abandoned && age < CheckoutGrace));

        if (!verified.Found)
        {
            payment.MarkAbandoned("No transaction at the provider.", now);
        }
        else if (stillOpen)
        {
            if (age < MaxAge)
            {
                return false;
            }

            payment.MarkAbandoned("Checkout expired.", now);
        }
        else
        {
            Order order = await db.Orders.SingleAsync(o => o.Id == payment.OrderId, cancellationToken);
            bool otherSuccess = await db.Payments.AnyAsync(
                p => p.OrderId == order.Id && p.Id != payment.Id && p.Status == PaymentStatus.Succeeded, cancellationToken);
            if (!payment.ApplyVerifiedOutcome(order, verified.Outcome, verified.AmountMinor, verified.CurrencyCode,
                    verified.TransactionId, verified.Message, otherSuccess, now))
            {
                return false;
            }
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return true;
    }
}
