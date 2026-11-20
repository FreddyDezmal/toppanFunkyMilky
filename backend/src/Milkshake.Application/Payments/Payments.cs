using Milkshake.Domain.Payments;

namespace Milkshake.Application.Payments;

/// <summary>What the gateway needs to create a hosted checkout. The amount comes from the stored order only.</summary>
public sealed record CheckoutRequest(string Reference, long AmountMinor, string CurrencyCode, string CustomerEmail, Uri CallbackUrl);

/// <summary>
/// A transaction's state as reported by the provider's own verification API (never by the browser).
/// <see cref="Found"/> is false when the provider has no transaction with this reference.
/// </summary>
public sealed record GatewayVerification(bool Found, VerifiedOutcome Outcome, long AmountMinor, string CurrencyCode, long? TransactionId, string? Message)
{
    public static GatewayVerification NotFound { get; } = new(false, VerifiedOutcome.Abandoned, 0, string.Empty, null, null);
}

/// <summary>What the gateway needs to return money. Our reference makes a repeated request harmless (no second refund).</summary>
public sealed record RefundRequest(string Reference, string TransactionReference, long AmountMinor, string CurrencyCode, string MerchantNote);

/// <summary>
/// A refund's state as reported by the provider's own API (never by an unverified notification). <see cref="Found"/> is
/// false when the provider has no refund with our reference.
/// </summary>
public sealed record GatewayRefund(bool Found, VerifiedRefundOutcome Outcome, long AmountMinor, long? RefundId, string? Message)
{
    /// <summary>Stored as the provider message when Paystack needs bank details we do not collect (Q4).</summary>
    public const string NeedsAttentionMessage = "Needs attention at Paystack.";

    public static GatewayRefund NotFound { get; } = new(false, VerifiedRefundOutcome.Pending, 0, null, null);
}

/// <summary>The provider could not be reached or answered unexpectedly. Never contains secrets or payloads.</summary>
public sealed class PaymentGatewayException : Exception
{
    public PaymentGatewayException()
    {
    }

    public PaymentGatewayException(string message)
        : base(message)
    {
    }

    public PaymentGatewayException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A payment provider (Paystack in M8; decision A1). Implementations live in Infrastructure.</summary>
public interface IPaymentGateway
{
    /// <summary>Provider name stored on each payment, e.g. "Paystack".</summary>
    string Name { get; }

    /// <summary>The only host the patron may be sent to for checkout (P8).</summary>
    string CheckoutHost { get; }

    /// <summary>Creates a hosted checkout for our reference. Throws <see cref="PaymentGatewayException"/> on failure.</summary>
    Task<Uri> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>Asks the provider for the transaction's current state. Throws <see cref="PaymentGatewayException"/> on failure.</summary>
    Task<GatewayVerification> VerifyAsync(string reference, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the provider to return money (M8b). Repeating a request with the same reference returns the existing refund
    /// instead of creating another. Throws <see cref="PaymentGatewayException"/> on failure.
    /// </summary>
    Task<GatewayRefund> CreateRefundAsync(RefundRequest request, CancellationToken cancellationToken);

    /// <summary>The refund's current state by our reference. Throws <see cref="PaymentGatewayException"/> on failure.</summary>
    Task<GatewayRefund> GetRefundAsync(string reference, CancellationToken cancellationToken);
}

public enum StartPaymentOutcome
{
    /// <summary>Send the patron to <see cref="StartPaymentResult.CheckoutUrl"/>.</summary>
    Checkout,

    /// <summary>The order is already paid (possibly just found out by verifying an open attempt).</summary>
    AlreadyPaid,

    /// <summary>No such order for this patron.</summary>
    NotFound,

    /// <summary>The order cannot be paid (e.g. cancelled).</summary>
    NotPayable,

    /// <summary>The pickup time has passed (decision A2): place a new order.</summary>
    PickupPassed,

    /// <summary>Another request is preparing a payment for this order right now.</summary>
    InProgress,

    /// <summary>This Idempotency-Key belongs to an attempt that has ended; start a new one with a new key.</summary>
    KeyUsed,

    /// <summary>The payment provider is unavailable or misconfigured; nothing was charged.</summary>
    GatewayUnavailable,
}

public sealed record StartPaymentResult(StartPaymentOutcome Outcome, string? PaymentReference = null, Uri? CheckoutUrl = null);

/// <summary>Patron payment use cases (M8). Every amount comes from the stored order; nothing from the browser.</summary>
public interface IPaymentService
{
    /// <summary>
    /// Starts (or resumes) paying one of the signed-in patron's orders (decision P4). Idempotent per key; at most one open
    /// attempt per order; an open attempt is checked with the provider before a new one is created.
    /// </summary>
    Task<StartPaymentResult> StartAsync(string orderReference, Guid idempotencyKey, CancellationToken cancellationToken);
}
