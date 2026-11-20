using Microsoft.Extensions.Logging;
using Milkshake.Application.Payments;
using Milkshake.Domain.Common;
using Milkshake.Domain.Payments;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Infrastructure.Payments;

/// <summary>
/// Sends stored refunds to the provider and applies the provider's answers (decision R4). A refund row is always
/// committed before this is called, so an interruption leaves an Initiating refund with our reference that the
/// reconciliation job submits again; reusing the reference cannot create a second refund at Paystack.
/// </summary>
internal sealed partial class RefundSubmitter(MilkshakeDbContext db, IPaymentGateway gateway, TimeProvider clock, ILogger<RefundSubmitter> logger)
{
    /// <summary>
    /// Asks the provider to return the money for an Initiating refund and saves its answer. Returns false, leaving the
    /// refund Initiating for a later retry, when the provider cannot be reached.
    /// </summary>
    public async Task<bool> SubmitAsync(Refund refund, string paymentReference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refund);
        if (refund.Status != RefundStatus.Initiating)
        {
            return false;
        }

        GatewayRefund answer;
        try
        {
            answer = await gateway.CreateRefundAsync(
                new RefundRequest(refund.Reference, paymentReference, refund.AmountMinor, refund.CurrencyCode, refund.Kind.ToString()),
                cancellationToken);
        }
        catch (PaymentGatewayException)
        {
            LogDeferred(logger, refund.Reference);
            return false;
        }

        if (!Apply(refund, answer))
        {
            return false;
        }

        // Not cancellable: the provider has acted, so its answer is worth keeping even if the caller has gone.
        await db.SaveChangesAsync(CancellationToken.None);
        return true;
    }

    /// <summary>Applies a provider answer in memory; the caller saves. False if nothing changed or it cannot be accepted.</summary>
    public bool Apply(Refund refund, GatewayRefund answer)
    {
        ArgumentNullException.ThrowIfNull(refund);
        ArgumentNullException.ThrowIfNull(answer);
        if (!answer.Found)
        {
            return false;
        }

        try
        {
            return refund.ApplyVerifiedOutcome(answer.Outcome, answer.AmountMinor, answer.RefundId, answer.Message, clock.GetUtcNow());
        }
        catch (BusinessRuleException)
        {
            LogAmountMismatch(logger, refund.Reference);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refund {RefundReference} could not be sent to the payment provider yet; it will be retried.")]
    private static partial void LogDeferred(ILogger logger, string refundReference);

    [LoggerMessage(Level = LogLevel.Error, Message = "The payment provider reported a different amount for refund {RefundReference}; it needs manual attention.")]
    private static partial void LogAmountMismatch(ILogger logger, string refundReference);
}
