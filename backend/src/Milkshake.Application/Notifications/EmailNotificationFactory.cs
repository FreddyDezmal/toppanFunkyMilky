using Milkshake.Domain.Notifications;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;

namespace Milkshake.Application.Notifications;

/// <summary>The patron's registered address and first name, read from their account when the event happens.</summary>
public sealed record NotificationRecipient(string Email, string Name);

/// <summary>
/// Builds the outbox row for each email (D2) from the stored order, payment, refund or request – never from today's
/// catalogue or settings (AC-9.3). Returns null when the recipient is not a valid single address: the business change
/// still saves, the email is skipped.
/// </summary>
public static class EmailNotificationFactory
{
    /// <summary>Email 1 (D1): order and payment confirmation with the final receipt.</summary>
    public static EmailNotification? OrderConfirmed(Order order, Payment payment, NotificationRecipient recipient, string locale, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(payment);

        var payload = new OrderConfirmedPayload(
            order.Reference,
            order.RestaurantName,
            order.RestaurantAddress,
            order.PickupAtUtc,
            order.PickupTimeZone,
            order.Items.OrderBy(i => i.Number)
                .Select(i => new ReceiptDrink(i.Number, i.FlavourName, i.FlavourFee, i.ConsistencyName, i.ConsistencyFee, i.ToppingName, i.ToppingFee, i.Price))
                .ToList(),
            order.Subtotal,
            order.DiscountAmount,
            order.VatRate,
            order.Vat,
            order.Total,
            order.CurrencyCode,
            payment.Reference,
            payment.VerifiedAt ?? now,
            locale);
        return Create(NotificationKind.OrderConfirmed, order.Id, order.Id, null, null, recipient, payload, now);
    }

    /// <summary>Email 2: a manager asks the patron to accept cancelling. On acceptance everything paid is refunded (Q1).</summary>
    public static EmailNotification? CancellationRequested(
        Order order, CancellationRequest request, Payment? paidPayment, NotificationRecipient recipient, string locale, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(request);

        var payload = new CancellationRequestedPayload(
            order.Reference,
            request.Reason,
            order.PickupAtUtc,
            order.PickupTimeZone,
            paidPayment is not null,
            paidPayment is null ? 0m : MinorUnits.FromMinor(paidPayment.AmountMinor, paidPayment.CurrencyCode),
            paidPayment?.CurrencyCode ?? order.CurrencyCode,
            locale);
        return Create(NotificationKind.CancellationRequested, request.Id, order.Id, null, request.Id, recipient, payload, now);
    }

    /// <summary>Email 3: the order was cancelled. The refund figures come from the stored refund, never recalculated.</summary>
    public static EmailNotification? OrderCancelled(
        Order order, Refund? refund, bool atRestaurantRequest, NotificationRecipient recipient, string locale, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);

        string currency = refund?.CurrencyCode ?? order.CurrencyCode;
        var payload = new OrderCancelledPayload(
            order.Reference,
            refund is not null,
            atRestaurantRequest || refund?.Kind == RefundKind.RestaurantCancellation,
            refund is null ? 0m : MinorUnits.FromMinor(refund.PaidAmountMinor, currency),
            refund is null ? 0m : MinorUnits.FromMinor(refund.FeeMinor, currency),
            refund is null ? 0m : MinorUnits.FromMinor(refund.AmountMinor, currency),
            refund?.Reference,
            currency,
            locale);
        return Create(NotificationKind.OrderCancelled, order.Id, order.Id, null, null, recipient, payload, now);
    }

    /// <summary>Emails 4 and 5: Paystack confirmed the refund as processed, or it could not be completed.</summary>
    public static EmailNotification? RefundOutcome(Order order, Refund refund, NotificationRecipient recipient, string locale, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(refund);

        NotificationKind kind = refund.Status switch
        {
            RefundStatus.Processed => NotificationKind.RefundProcessed,
            RefundStatus.Failed => NotificationKind.RefundFailed,
            _ => throw new InvalidOperationException("Only a processed or failed refund has an outcome email."),
        };

        var payload = new RefundOutcomePayload(
            order.Reference,
            refund.Reference,
            refund.Kind,
            MinorUnits.FromMinor(refund.AmountMinor, refund.CurrencyCode),
            MinorUnits.FromMinor(refund.FeeMinor, refund.CurrencyCode),
            refund.CompletedAt ?? now,
            order.PickupTimeZone,
            refund.CurrencyCode,
            locale);
        return Create(kind, refund.Id, order.Id, refund.Id, null, recipient, payload, now);
    }

    private static EmailNotification? Create<T>(
        NotificationKind kind, int eventId, int orderId, int? refundId, int? requestId, NotificationRecipient recipient, T payload, DateTimeOffset now)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(recipient);
        if (!EmailNotification.IsValidRecipient(recipient.Email))
        {
            return null;
        }

        return EmailNotification.Create(
            kind,
            EmailNotification.KeyFor(kind, eventId),
            orderId,
            refundId,
            requestId,
            recipient.Email,
            recipient.Name,
            EmailPayloads.CurrentTemplateVersion,
            EmailPayloads.Serialize(payload),
            now);
    }
}
