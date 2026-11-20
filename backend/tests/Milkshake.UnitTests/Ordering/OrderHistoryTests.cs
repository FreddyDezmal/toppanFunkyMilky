using Milkshake.Application.Ordering;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;

namespace Milkshake.UnitTests.Ordering;

/// <summary>M12 patron history: the payment label rule (decision R3), the history cursor and strict query parsing.</summary>
public sealed class OrderHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    private static RefundFacts Refund(int id, RefundKind kind, RefundStatus status, long fee = 0, int minutes = 0) =>
        new(id, kind, status, fee, T0.AddMinutes(minutes));

    // ---- payment labels ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.PendingPayment, PaymentLabel.Unpaid)]
    [InlineData(OrderStatus.PaymentFailed, PaymentLabel.Unpaid)]
    [InlineData(OrderStatus.Confirmed, PaymentLabel.Paid)]
    public void OpenAndPaidOrders_UseTheUrsLabels_WithoutRefundDetail(OrderStatus status, PaymentLabel expected)
    {
        Assert.Equal(new PaymentState(expected, RefundLabel.None), PaymentLabels.For(status, []));
    }

    [Fact]
    public void APaidOrder_StaysPaid_WhenAnExtraPaymentIsRefunded()
    {
        PaymentState state = PaymentLabels.For(OrderStatus.Confirmed, [Refund(1, RefundKind.UnacceptedPayment, RefundStatus.Processed)]);

        Assert.Equal(new PaymentState(PaymentLabel.Paid, RefundLabel.None), state);
    }

    [Fact]
    public void ACancelledOrderWithoutRefunds_WasNotPaid()
    {
        Assert.Equal(new PaymentState(PaymentLabel.Cancelled, RefundLabel.NotPaid), PaymentLabels.For(OrderStatus.Cancelled, []));
    }

    [Theory]
    [InlineData(RefundKind.PatronCancellation, RefundStatus.Initiating, 260L, RefundLabel.RefundInProgress)]
    [InlineData(RefundKind.PatronCancellation, RefundStatus.Pending, 260L, RefundLabel.RefundInProgress)]
    [InlineData(RefundKind.PatronCancellation, RefundStatus.Processed, 260L, RefundLabel.RefundedLessFee)]
    [InlineData(RefundKind.PatronCancellation, RefundStatus.Failed, 260L, RefundLabel.RefundFailed)]
    [InlineData(RefundKind.RestaurantCancellation, RefundStatus.Processed, 0L, RefundLabel.Refunded)]
    [InlineData(RefundKind.PatronCancellation, RefundStatus.Processed, 0L, RefundLabel.Refunded)]
    [InlineData(RefundKind.UnacceptedPayment, RefundStatus.Pending, 0L, RefundLabel.RefundInProgress)]
    [InlineData(RefundKind.UnacceptedPayment, RefundStatus.Processed, 0L, RefundLabel.Refunded)]
    public void ACancelledOrder_ShowsWhatHappenedToTheMoney(RefundKind kind, RefundStatus status, long fee, RefundLabel expected)
    {
        PaymentState state = PaymentLabels.For(OrderStatus.Cancelled, [Refund(1, kind, status, fee)]);

        Assert.Equal(new PaymentState(PaymentLabel.Cancelled, expected), state);
    }

    [Fact]
    public void TheCancellationsOwnRefund_Decides_EvenWhenALaterRefundExists()
    {
        PaymentState state = PaymentLabels.For(OrderStatus.Cancelled,
        [
            Refund(1, RefundKind.PatronCancellation, RefundStatus.Processed, fee: 260, minutes: 0),
            Refund(2, RefundKind.UnacceptedPayment, RefundStatus.Failed, minutes: 5),
        ]);

        Assert.Equal(RefundLabel.RefundedLessFee, state.Refund);
    }

    [Fact]
    public void WithoutACancellationRefund_TheNewestOtherRefundDecides()
    {
        PaymentState state = PaymentLabels.For(OrderStatus.Cancelled,
        [
            Refund(1, RefundKind.UnacceptedPayment, RefundStatus.Failed, minutes: 0),
            Refund(2, RefundKind.UnacceptedPayment, RefundStatus.Pending, minutes: 5),
        ]);

        Assert.Equal(RefundLabel.RefundInProgress, state.Refund);
    }

    [Fact]
    public void RefundsCreatedAtTheSameTime_AreOrderedById()
    {
        PaymentState state = PaymentLabels.For(OrderStatus.Cancelled,
        [
            Refund(7, RefundKind.UnacceptedPayment, RefundStatus.Processed),
            Refund(3, RefundKind.UnacceptedPayment, RefundStatus.Failed),
        ]);

        Assert.Equal(RefundLabel.Refunded, state.Refund);
    }

    // ---- cursor ----------------------------------------------------------------------------------------------------

    [Fact]
    public void TheCursor_RoundTrips_AndIsUrlSafe()
    {
        var cursor = new OrderCursor(new DateTimeOffset(2026, 11, 20, 8, 15, 30, 123, TimeSpan.Zero).AddTicks(4560), 4711);

        string encoded = cursor.Encode();

        Assert.Matches("^[A-Za-z0-9_-]+$", encoded);
        Assert.Equal(cursor, OrderCursor.TryDecode(encoded));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("MTIzNDU")] // "12345": no separator
    [InlineData("LTE6NQ")] // "-1:5": negative ticks
    [InlineData("MTIzOi01")] // "123:-5": negative id
    [InlineData("MTIzOjA")] // "123:0": id 0
    [InlineData("MTIzOjU6Nw")] // "123:5:7": too many parts
    [InlineData("OTk5OTk5OTk5OTk5OTk5OTk5OTk6NQ")] // ticks too large
    public void InvalidCursors_AreRejected(string? value)
    {
        Assert.Null(OrderCursor.TryDecode(value));
    }

    [Fact]
    public void AnOverlongCursor_IsRejected()
    {
        Assert.Null(OrderCursor.TryDecode(new string('A', 65)));
    }

    // ---- query parsing -------------------------------------------------------------------------------------------

    private static (OrderHistoryQuery? Query, Dictionary<string, string> Errors) Parse(params (string Key, string[] Values)[] pairs)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        OrderHistoryQuery? query = OrderHistoryQueryParser.Parse(pairs.ToDictionary(p => p.Key, p => p.Values, StringComparer.Ordinal), errors);
        return (query, errors);
    }

    [Fact]
    public void NoParameters_GiveTheFirstPageOfTwenty()
    {
        (OrderHistoryQuery? query, Dictionary<string, string> errors) = Parse();

        Assert.Empty(errors);
        Assert.Equal(new OrderHistoryQuery(null, 20), query);
    }

    [Fact]
    public void ACursorAndLimit_AreAccepted_CaseInsensitively()
    {
        var cursor = new OrderCursor(T0, 12);

        (OrderHistoryQuery? query, _) = Parse(("Cursor", new[] { cursor.Encode() }), ("LIMIT", new[] { "50" }));

        Assert.Equal(new OrderHistoryQuery(cursor, 50), query);
    }

    [Fact]
    public void BlankValues_AreIgnored()
    {
        (OrderHistoryQuery? query, _) = Parse(("cursor", new[] { " " }), ("limit", new[] { "" }));

        Assert.Equal(new OrderHistoryQuery(null, 20), query);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("51")]
    [InlineData("-1")]
    [InlineData("+5")]
    [InlineData("ten")]
    [InlineData("1.5")]
    public void ALimitOutsideOneToFifty_IsRejected(string limit)
    {
        (OrderHistoryQuery? query, Dictionary<string, string> errors) = Parse(("limit", new[] { limit }));

        Assert.Null(query);
        Assert.Equal("Use a number from 1 to 50.", errors["limit"]);
    }

    [Fact]
    public void AnInvalidCursor_IsRejected()
    {
        (OrderHistoryQuery? query, Dictionary<string, string> errors) = Parse(("cursor", new[] { "nope!" }));

        Assert.Null(query);
        Assert.True(errors.ContainsKey("cursor"));
    }

    [Fact]
    public void UnknownOrRepeatedParameters_AreRejected_IncludingAPatronId()
    {
        (OrderHistoryQuery? query, Dictionary<string, string> errors) = Parse(
            ("patronId", new[] { Guid.NewGuid().ToString() }),
            ("limit", new[] { "5", "10" }));

        Assert.Null(query);
        Assert.Equal(new[] { "limit", "patronId" }, errors.Keys.Order(StringComparer.Ordinal));
    }
}
