using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Milkshake.Domain.Common;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Pricing;

namespace Milkshake.Domain.Payments;

/// <summary>
/// Money returned for one payment (M8b, decisions R3–R5, Q1, Q2). The amounts are fixed when the refund is created; the
/// status changes only with a result verified with the provider. A refund is never Processed because a request was
/// accepted: the provider confirms it later (R4).
/// </summary>
public sealed partial class Refund : IHasTimestamps, IHasRowVersion
{
    public const decimal MaxFeeRate = 0.5m;
    public const string ReferencePattern = "^MSR-[0-9A-HJKMNP-TV-Z]{12}$";

    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private Refund()
    {
    }

    public int Id { get; private set; }

    /// <summary>Our reference, sent to the provider so a repeated request cannot create a second refund (e.g. MSR-7KQ2X9TD4M1A).</summary>
    public string Reference { get; private set; } = string.Empty;

    public int OrderId { get; private set; }

    /// <summary>The payment being refunded; at most one refund per payment (database-enforced).</summary>
    public int PaymentId { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public RefundKind Kind { get; private set; }

    /// <summary>What the patron paid, in minor units (cents).</summary>
    public long PaidAmountMinor { get; private set; }

    /// <summary>The fee rate applied (a fraction of the order's VAT-exclusive amount); 0 when no fee is charged.</summary>
    public decimal FeeRate { get; private set; }

    public long FeeMinor { get; private set; }

    /// <summary>The amount returned: paid minus fee, in minor units.</summary>
    public long AmountMinor { get; private set; }

    public string CurrencyCode { get; private set; } = string.Empty;

    public RefundStatus Status { get; private set; }

    /// <summary>The provider's id for the refund, once known.</summary>
    public long? ProviderRefundId { get; private set; }

    /// <summary>Short, non-sensitive provider message for the last verified result.</summary>
    public string? ProviderMessage { get; private set; }

    /// <summary>When the provider confirmed the final result (Processed or Failed).</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; private set; }

    public static bool IsValidReference(string? value) => value is not null && ReferenceRegex().IsMatch(value);

    public static string NewReference()
    {
        Span<char> chars = stackalloc char[12];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"MSR-{chars}";
    }

    /// <summary>
    /// The fee for cancelling a paid order (B1.4): the rate × the order's VAT-exclusive amount, rounded once to cents,
    /// half away from zero. Example: 10% of R52.00 = R5.20.
    /// </summary>
    public static decimal CancellationFee(Order order, decimal feeRate)
    {
        ArgumentNullException.ThrowIfNull(order);
        CheckFeeRate(feeRate);
        return PriceRounding.ToCents(order.Taxable * feeRate);
    }

    /// <summary>
    /// The refund for a cancelled, paid order. A patron's own cancellation pays the fee (B1.4); a cancellation the
    /// restaurant requested is refunded in full (Q1). The order must already be cancelled.
    /// </summary>
    public static Refund ForCancellation(Order order, Payment payment, string reference, decimal feeRate, bool requestedByRestaurant)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(payment);

        if (payment.OrderId != order.Id || payment.Status != PaymentStatus.Succeeded)
        {
            throw new BusinessRuleException("Only the order's successful payment can be refunded on cancellation.");
        }

        if (order.Status != OrderStatus.Cancelled)
        {
            throw new BusinessRuleException("The order must be cancelled before its payment is refunded.");
        }

        decimal rate = requestedByRestaurant ? 0m : feeRate;
        long fee = MinorUnits.ToMinorOrZero(CancellationFee(order, rate), payment.CurrencyCode);
        return Create(order.Id, payment, reference,
            requestedByRestaurant ? RefundKind.RestaurantCancellation : RefundKind.PatronCancellation, rate, fee);
    }

    /// <summary>A full refund, without a fee, of a payment the order could not accept (Q2).</summary>
    public static Refund ForUnacceptedPayment(Payment payment, string reference)
    {
        ArgumentNullException.ThrowIfNull(payment);

        if (payment.Status != PaymentStatus.RequiresRefund)
        {
            throw new BusinessRuleException("Only a payment that could not be accepted is refunded in full automatically.");
        }

        return Create(payment.OrderId, payment, reference, RefundKind.UnacceptedPayment, 0m, 0);
    }

    /// <summary>The provider accepted the request; the refund is now on its way (not yet complete).</summary>
    public void Submitted(long providerRefundId)
    {
        if (Status != RefundStatus.Initiating)
        {
            if (Status == RefundStatus.Pending && ProviderRefundId == providerRefundId)
            {
                return;
            }

            throw new BusinessRuleException($"A {Status} refund cannot be submitted.");
        }

        ProviderRefundId = providerRefundId;
        Status = RefundStatus.Pending;
    }

    /// <summary>
    /// Applies a result verified with the provider. Idempotent; Processed and Failed are final. Returns true if anything
    /// changed. An amount different from ours is never accepted as Processed.
    /// </summary>
    public bool ApplyVerifiedOutcome(VerifiedRefundOutcome outcome, long refundedAmountMinor, long? providerRefundId, string? providerMessage, DateTimeOffset verifiedAt)
    {
        if (Status is RefundStatus.Processed or RefundStatus.Failed)
        {
            return false;
        }

        if (outcome == VerifiedRefundOutcome.Pending)
        {
            // Still on its way: record the provider's id if we did not get it before, and any change in its message
            // (e.g. "needs attention").
            bool changed = false;
            if (Status == RefundStatus.Initiating && providerRefundId is long id)
            {
                Submitted(id);
                changed = true;
            }

            string? message = Truncate(providerMessage);
            if (message != ProviderMessage)
            {
                ProviderMessage = message;
                changed = true;
            }

            return changed;
        }

        if (outcome == VerifiedRefundOutcome.Processed && refundedAmountMinor != AmountMinor)
        {
            throw new BusinessRuleException("The provider reported a refund amount different from the one requested.");
        }

        ProviderRefundId = providerRefundId ?? ProviderRefundId;
        ProviderMessage = Truncate(providerMessage);
        Status = outcome == VerifiedRefundOutcome.Processed ? RefundStatus.Processed : RefundStatus.Failed;
        CompletedAt = verifiedAt;
        return true;
    }

    private static Refund Create(int orderId, Payment payment, string reference, RefundKind kind, decimal feeRate, long feeMinor)
    {
        if (!IsValidReference(reference))
        {
            throw new ArgumentException("The refund reference is not in the MSR-XXXXXXXXXXXX format.", nameof(reference));
        }

        if (feeMinor < 0 || feeMinor >= payment.AmountMinor)
        {
            throw new BusinessRuleException("The fee must leave a positive amount to refund.");
        }

        return new Refund
        {
            Reference = reference,
            OrderId = orderId,
            PaymentId = payment.Id,
            Provider = payment.Provider,
            Kind = kind,
            PaidAmountMinor = payment.AmountMinor,
            FeeRate = feeRate,
            FeeMinor = feeMinor,
            AmountMinor = payment.AmountMinor - feeMinor,
            CurrencyCode = payment.CurrencyCode,
            Status = RefundStatus.Initiating,
        };
    }

    private static void CheckFeeRate(decimal feeRate)
    {
        if (feeRate < 0m || feeRate > MaxFeeRate)
        {
            throw new BusinessRuleException($"The refund fee rate must be between 0 and {MaxFeeRate}.");
        }
    }

    private static string? Truncate(string? message) =>
        string.IsNullOrWhiteSpace(message) ? null : message.Trim()[..Math.Min(message.Trim().Length, Payment.ProviderMessageMaxLength)];

    [GeneratedRegex(ReferencePattern, RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceRegex();
}

/// <summary>Why money is returned.</summary>
public enum RefundKind
{
    /// <summary>The patron cancelled a paid order: the fee is kept (B1.4).</summary>
    PatronCancellation,

    /// <summary>The patron accepted the restaurant's cancellation request: full refund (Q1).</summary>
    RestaurantCancellation,

    /// <summary>A payment the order could not accept (late, duplicate or wrong amount): full refund (Q2).</summary>
    UnacceptedPayment,
}

/// <summary>Refund progress. Stored as text; the names are part of a database check constraint.</summary>
public enum RefundStatus
{
    /// <summary>Stored; the provider has not accepted the request yet.</summary>
    Initiating,

    /// <summary>The provider accepted the request; the money is on its way.</summary>
    Pending,

    /// <summary>The provider confirmed the money was returned.</summary>
    Processed,

    /// <summary>The provider could not return the money; it stays with the business for manual handling (Q4).</summary>
    Failed,
}

/// <summary>A refund result reported by the provider's verification API, never by an unverified notification.</summary>
public enum VerifiedRefundOutcome
{
    Pending,
    Processed,
    Failed,
}
