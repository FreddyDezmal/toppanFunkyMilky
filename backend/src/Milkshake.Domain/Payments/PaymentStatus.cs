namespace Milkshake.Domain.Payments;

/// <summary>
/// State of one payment attempt (M8 decision P2). Stored as text; the names are part of a database check constraint.
/// The provider is the source of truth for whether money moved, so a later verified result may correct an earlier one
/// (out-of-order notifications); only Succeeded and RequiresRefund are final in M8a.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Row stored with our reference; the provider has not (yet) confirmed a checkout.</summary>
    Initiating,

    /// <summary>Checkout created at the provider; waiting for the patron and for verification.</summary>
    Pending,

    /// <summary>Verified paid, with the order's exact amount and currency, and accepted for the order.</summary>
    Succeeded,

    /// <summary>The provider reports the payment itself failed (e.g. declined).</summary>
    Failed,

    /// <summary>The patron left checkout without paying, or the provider has no such transaction.</summary>
    Abandoned,

    /// <summary>Replaced by a newer attempt for the same order.</summary>
    Superseded,

    /// <summary>
    /// Money was received but cannot be accepted (order cancelled or already paid, or amount/currency mismatch).
    /// Kept as evidence for refund handling (M8b); never discarded.
    /// </summary>
    RequiresRefund,
}

/// <summary>Why a received payment could not be accepted.</summary>
public enum RefundReason
{
    OrderNotPayable,
    AlreadyPaid,
    AmountMismatch,
}

/// <summary>A payment result verified with the provider (never taken from the browser).</summary>
public enum VerifiedOutcome
{
    Paid,
    Failed,
    Abandoned,

    /// <summary>Still in progress at the provider.</summary>
    Open,
}

public static class PaymentStatusTransitions
{
    private static readonly PaymentStatus[] Open = [PaymentStatus.Initiating, PaymentStatus.Pending];

    public static bool IsOpen(PaymentStatus status) => Open.Contains(status);

    public static bool IsFinal(PaymentStatus status) => status is PaymentStatus.Succeeded or PaymentStatus.RequiresRefund;
}
