using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Milkshake.Domain.Common;
using Milkshake.Domain.Orders;

namespace Milkshake.Domain.Payments;

/// <summary>
/// One attempt to pay an order (M8). The amount is copied from the order's stored total when the attempt is created and
/// never changes. The order is updated only through <see cref="ApplyVerifiedOutcome"/>, with a result verified with the
/// provider: the amount and currency must match exactly, and a charge the order cannot accept is kept as
/// <see cref="PaymentStatus.RequiresRefund"/> instead of being discarded (P6).
/// </summary>
public sealed partial class Payment : IHasTimestamps, IHasRowVersion
{
    public const string PaystackProvider = "Paystack";
    public const int ReferenceLength = 16;
    public const int ProviderMaxLength = 20;
    public const int CheckoutUrlMaxLength = 500;
    public const int ProviderMessageMaxLength = 100;
    public const string ReferencePattern = "^MSP-[0-9A-HJKMNP-TV-Z]{12}$";

    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private Payment()
    {
    }

    public int Id { get; private set; }

    /// <summary>Our reference for this attempt, sent to the provider as its transaction reference (e.g. MSP-7KQ2X9TD4M1A).</summary>
    public string Reference { get; private set; } = string.Empty;

    public int OrderId { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    /// <summary>The patron's Idempotency-Key for the request that created this attempt.</summary>
    public Guid IdempotencyKey { get; private set; }

    /// <summary>Amount in minor units (cents), copied from the order's stored total.</summary>
    public long AmountMinor { get; private set; }

    public string CurrencyCode { get; private set; } = string.Empty;

    public PaymentStatus Status { get; private set; }

    /// <summary>Why a received payment could not be accepted (only for RequiresRefund).</summary>
    public RefundReason? RefundReason { get; private set; }

    /// <summary>The provider's own transaction id, once known.</summary>
    public long? ProviderTransactionId { get; private set; }

    /// <summary>Hosted checkout address returned by the provider (validated before storing).</summary>
    public string? CheckoutUrl { get; private set; }

    /// <summary>Short, non-sensitive provider message for the last verified result (e.g. "Declined").</summary>
    public string? ProviderMessage { get; private set; }

    /// <summary>When the last provider verification changed this attempt.</summary>
    public DateTimeOffset? VerifiedAt { get; private set; }

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

        return $"MSP-{chars}";
    }

    /// <summary>Starts an attempt for an order that is awaiting payment, for exactly its stored total.</summary>
    public static Payment Start(Order order, string reference, string provider, Guid idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        if (!OrderStatusTransitions.AwaitsPayment(order.Status))
        {
            throw new BusinessRuleException($"An order that is {order.Status} cannot be paid.");
        }

        if (!IsValidReference(reference))
        {
            throw new ArgumentException("The payment reference is not in the MSP-XXXXXXXXXXXX format.", nameof(reference));
        }

        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }

        return new Payment
        {
            Reference = reference,
            OrderId = order.Id,
            Provider = provider,
            IdempotencyKey = idempotencyKey,
            AmountMinor = MinorUnits.ToMinor(order.Total, order.CurrencyCode),
            CurrencyCode = order.CurrencyCode,
            Status = PaymentStatus.Initiating,
        };
    }

    /// <summary>The provider created the checkout. Only an https URL on the expected host is accepted (P8).</summary>
    public void CheckoutCreated(Uri checkoutUrl, string expectedHost)
    {
        ArgumentNullException.ThrowIfNull(checkoutUrl);

        if (!checkoutUrl.IsAbsoluteUri || checkoutUrl.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(checkoutUrl.Host, expectedHost, StringComparison.OrdinalIgnoreCase) ||
            checkoutUrl.AbsoluteUri.Length > CheckoutUrlMaxLength)
        {
            throw new BusinessRuleException("The provider returned an unexpected checkout address.");
        }

        if (Status != PaymentStatus.Initiating)
        {
            throw new BusinessRuleException($"A checkout cannot be attached to a {Status} payment.");
        }

        CheckoutUrl = checkoutUrl.AbsoluteUri;
        Status = PaymentStatus.Pending;
    }

    /// <summary>
    /// Ends an open attempt that never became a usable checkout: the provider has no such transaction (we stopped before
    /// reaching it), or it returned an unexpected checkout address. Nothing was paid, so the order is not touched.
    /// </summary>
    public void MarkAbandoned(string reason, DateTimeOffset at)
    {
        if (!PaymentStatusTransitions.IsOpen(Status))
        {
            throw new BusinessRuleException($"A {Status} payment cannot be abandoned.");
        }

        Status = PaymentStatus.Abandoned;
        ProviderMessage = Truncate(reason);
        VerifiedAt = at;
    }

    /// <summary>A newer attempt replaces this open one.</summary>
    public void Supersede()
    {
        if (!PaymentStatusTransitions.IsOpen(Status))
        {
            throw new BusinessRuleException($"A {Status} payment cannot be superseded.");
        }

        Status = PaymentStatus.Superseded;
    }

    /// <summary>
    /// Applies a result verified with the provider to this attempt and its order, in memory; the caller saves both in
    /// one transaction. Idempotent: applying the same result again changes nothing. Returns true if anything changed.
    /// </summary>
    public bool ApplyVerifiedOutcome(
        Order order,
        VerifiedOutcome outcome,
        long paidAmountMinor,
        string paidCurrencyCode,
        long? providerTransactionId,
        string? providerMessage,
        bool orderHasAnotherSuccessfulPayment,
        DateTimeOffset verifiedAt)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.Id != OrderId)
        {
            throw new ArgumentException("The order does not belong to this payment.", nameof(order));
        }

        if (PaymentStatusTransitions.IsFinal(Status) || outcome == VerifiedOutcome.Open)
        {
            return false;
        }

        PaymentStatus before = Status;
        ProviderTransactionId = providerTransactionId ?? ProviderTransactionId;
        ProviderMessage = Truncate(providerMessage);

        switch (outcome)
        {
            case VerifiedOutcome.Paid:
                AcceptPaid(order, paidAmountMinor, paidCurrencyCode, orderHasAnotherSuccessfulPayment);
                break;
            case VerifiedOutcome.Failed:
                Status = PaymentStatus.Failed;

                // Only the attempt the patron is currently using marks the order failed, not a superseded one.
                if (PaymentStatusTransitions.IsOpen(before) && order.Status == OrderStatus.PendingPayment && !orderHasAnotherSuccessfulPayment)
                {
                    order.MarkPaymentFailed();
                }

                break;
            case VerifiedOutcome.Abandoned:
                if (Status != PaymentStatus.Superseded)
                {
                    Status = PaymentStatus.Abandoned;
                }

                break;
        }

        if (Status != before)
        {
            VerifiedAt = verifiedAt;
            return true;
        }

        return false;
    }

    private void AcceptPaid(Order order, long paidAmountMinor, string paidCurrencyCode, bool orderHasAnotherSuccessfulPayment)
    {
        if (paidAmountMinor != AmountMinor || !string.Equals(paidCurrencyCode, CurrencyCode, StringComparison.Ordinal))
        {
            Status = PaymentStatus.RequiresRefund;
            RefundReason = Payments.RefundReason.AmountMismatch;
        }
        else if (orderHasAnotherSuccessfulPayment || order.Status == OrderStatus.Confirmed)
        {
            Status = PaymentStatus.RequiresRefund;
            RefundReason = Payments.RefundReason.AlreadyPaid;
        }
        else if (!OrderStatusTransitions.AwaitsPayment(order.Status))
        {
            Status = PaymentStatus.RequiresRefund;
            RefundReason = Payments.RefundReason.OrderNotPayable;
        }
        else
        {
            Status = PaymentStatus.Succeeded;
            order.MarkConfirmed();
        }
    }

    private static string? Truncate(string? message) =>
        string.IsNullOrWhiteSpace(message) ? null : message.Trim()[..Math.Min(message.Trim().Length, ProviderMessageMaxLength)];

    [GeneratedRegex(ReferencePattern, RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceRegex();
}
