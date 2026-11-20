using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Auditing;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;
using Milkshake.Domain.Pricing;
using Milkshake.IntegrationTests.Infrastructure;
using Milkshake.Infrastructure.Persistence;
using Npgsql;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M8b refund and cancellation-request tables against disposable PostgreSQL 17: the cancellation and its refund saved
/// and audited together, one refund per payment and one pending request per order (database-enforced), check
/// constraints that repeat the fee arithmetic, and restrict-only foreign keys.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class RefundPersistenceTests(PostgresApiFactory factory)
{
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";
    private const string ForeignKeyViolation = "23503";

    [Fact]
    public async Task ACancellationAndItsRefund_AreSavedTogether_AndAudited()
    {
        (Order order, Payment payment, _) = await PaidOrderAsync();

        Refund refund = await CancelWithRefundAsync(order, payment);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        Refund stored = await db.Refunds.AsNoTracking().SingleAsync(r => r.Id == refund.Id);

        // 12.00 + 6.00 + 8.00 = 26.00, VAT 3.90, total 29.90; fee 10% of 26.00 = 2.60; refund 27.30.
        Assert.Equal((2990L, 260L, 2730L, 0.10m), (stored.PaidAmountMinor, stored.FeeMinor, stored.AmountMinor, stored.FeeRate));
        Assert.Equal((RefundKind.PatronCancellation, RefundStatus.Initiating, payment.Id), (stored.Kind, stored.Status, stored.PaymentId));
        Assert.Equal(OrderStatus.Cancelled, (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status);

        string refundId = refund.Id.ToString(CultureInfo.InvariantCulture);
        string orderId = order.Id.ToString(CultureInfo.InvariantCulture);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.TableName == "refunds" && a.RecordId == refundId && a.Action == AuditAction.Insert));
        Assert.True(await db.AuditLogs.AnyAsync(a => a.TableName == "orders" && a.RecordId == orderId && a.Action == AuditAction.Update));
    }

    [Fact]
    public async Task ThePaymentIsRefundedAtMostOnce()
    {
        (Order order, Payment payment, _) = await PaidOrderAsync();
        await CancelWithRefundAsync(order, payment);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        Order cancelled = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Payment paid = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.Id);
        db.Refunds.Add(Refund.ForCancellation(cancelled, paid, Refund.NewReference(), 0.10m, requestedByRestaurant: true));

        DbUpdateException second = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.Equal((UniqueViolation, "ux_refunds_one_per_payment"), (SqlState(second), Constraint(second)));
    }

    [Fact]
    public async Task AnOrderHasAtMostOnePendingRequest_AndAnsweredRequestsAreKept()
    {
        (Order order, _, Guid manager) = await PaidOrderAsync();
        CancellationRequest first = await SaveRequestAsync(order, manager, "Out of milk");

        DbUpdateException duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => SaveRequestAsync(order, manager, "Machine broken"));
        Assert.Equal((UniqueViolation, "ux_cancellation_requests_one_pending_per_order"), (SqlState(duplicate), Constraint(duplicate)));

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            CancellationRequest tracked = await db.CancellationRequests.SingleAsync(r => r.Id == first.Id);
            tracked.Decline(await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        CancellationRequest second = await SaveRequestAsync(order, manager, "Machine broken");

        await using AsyncServiceScope check = factory.Services.CreateAsyncScope();
        List<CancellationRequestStatus> statuses = await Db(check).CancellationRequests.Where(r => r.OrderId == order.Id)
            .OrderBy(r => r.Id).Select(r => r.Status).ToListAsync();
        Assert.Equal([CancellationRequestStatus.Declined, CancellationRequestStatus.Pending], statuses.AsEnumerable());
        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory]
    [InlineData("UPDATE milkshake.refunds SET amount_minor = amount_minor - 1 WHERE id = {0}")]
    [InlineData("UPDATE milkshake.refunds SET kind = 'RestaurantCancellation' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.refunds SET fee_rate = 0.6 WHERE id = {0}")]
    [InlineData("UPDATE milkshake.refunds SET fee_minor = paid_amount_minor, amount_minor = 0 WHERE id = {0}")]
    [InlineData("UPDATE milkshake.refunds SET status = 'Done' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.refunds SET status = 'Pending' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.refunds SET status = 'Processed' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.refunds SET reference = 'MSP-0123456789AB' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.refunds SET currency_code = 'za1' WHERE id = {0}")]
    public async Task CheckConstraints_RejectInvalidRefundRows(string sql)
    {
        (Order order, Payment payment, _) = await PaidOrderAsync();
        Refund refund = await CancelWithRefundAsync(order, payment);

        PostgresException failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(sql, refund.Id));

        Assert.Equal(CheckViolation, failure.SqlState);
    }

    [Theory]
    [InlineData("UPDATE milkshake.cancellation_requests SET reason = '' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.cancellation_requests SET reason = ' padded' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.cancellation_requests SET reason = E'two\\nlines' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.cancellation_requests SET reason = repeat('x', 51) WHERE id = {0}")]
    [InlineData("UPDATE milkshake.cancellation_requests SET status = 'Open' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.cancellation_requests SET status = 'Accepted' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.cancellation_requests SET closed_at = now() WHERE id = {0}")]
    public async Task CheckConstraints_RejectInvalidRequestRows(string sql)
    {
        (Order order, _, Guid manager) = await PaidOrderAsync();
        CancellationRequest request = await SaveRequestAsync(order, manager, "Out of milk");

        PostgresException failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(sql, request.Id));

        Assert.True(failure.SqlState is CheckViolation or "22001", failure.SqlState);   // 22001: longer than the column allows
    }

    [Fact]
    public async Task RefundedPaymentsAndRequestedOrders_CannotBeDeleted()
    {
        (Order paidOrder, Payment payment, _) = await PaidOrderAsync();
        await CancelWithRefundAsync(paidOrder, payment);
        (Order requested, _, Guid manager) = await PaidOrderAsync();
        await SaveRequestAsync(requested, manager, "Out of milk");

        PostgresException payments = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync("DELETE FROM milkshake.payments WHERE id = {0}", payment.Id));
        await ExecuteAsync("DELETE FROM milkshake.payments WHERE order_id = {0}", requested.Id);
        await ExecuteAsync("DELETE FROM milkshake.order_items WHERE order_id = {0}", requested.Id);
        PostgresException orders = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync("DELETE FROM milkshake.orders WHERE id = {0}", requested.Id));

        Assert.Equal((ForeignKeyViolation, ForeignKeyViolation), (payments.SqlState, orders.SqlState));
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    private async Task<Refund> CancelWithRefundAsync(Order order, Payment payment)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        Order tracked = await db.Orders.SingleAsync(o => o.Id == order.Id);
        Payment paid = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.Id);
        tracked.Cancel(DateTimeOffset.UtcNow);
        Refund refund = Refund.ForCancellation(tracked, paid, Refund.NewReference(), 0.10m, requestedByRestaurant: false);
        db.Refunds.Add(refund);
        await db.SaveChangesAsync();
        return refund;
    }

    private async Task<CancellationRequest> SaveRequestAsync(Order order, Guid manager, string reason)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        Order current = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        CancellationRequest request = CancellationRequest.Create(current, manager, reason, DateTimeOffset.UtcNow);
        db.CancellationRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    /// <summary>A Confirmed order with its Succeeded payment; the manager's account doubles as the patron.</summary>
    private async Task<(Order Order, Payment Payment, Guid Manager)> PaidOrderAsync()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        Guid user = await db.Users.Where(u => u.Email == manager.Email).Select(u => u.Id).SingleAsync();

        OrderingOptions options = (await scope.ServiceProvider.GetRequiredService<IOrderingCatalogService>().GetOptionsAsync(CancellationToken.None)).Options
            ?? throw new InvalidOperationException("Ordering is unavailable in the test database.");
        string pickupLocal = options.Pickup.LatestLocal.ToString(PickupRules.LocalFormat, CultureInfo.InvariantCulture);
        DraftPricingResult priced = await scope.ServiceProvider.GetRequiredService<IOrderPricingService>().PriceDraftAsync(
            new OrderDraft(menu.Restaurant.Id, pickupLocal, [menu.Drink()]), CancellationToken.None);
        PriceBreakdown price = priced.Price ?? throw new InvalidOperationException("Pricing failed.");

        Order order = Order.Place(user, OrderReference.New(), new OrderRestaurant(menu.Restaurant.Id, menu.Restaurant.Name, null),
            priced.Draft!.Pickup.Utc.ToUniversalTime(), "UTC", price with { PricedAtUtc = price.PricedAtUtc.ToUniversalTime() },
            Guid.NewGuid(), Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()));
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        Payment payment = Payment.Start(order, Payment.NewReference(), Payment.PaystackProvider, Guid.NewGuid());
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        payment.ApplyVerifiedOutcome(order, VerifiedOutcome.Paid, payment.AmountMinor, payment.CurrencyCode,
            Random.Shared.NextInt64(1, long.MaxValue), "Approved", false, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        return (order, payment, user);
    }

    private async Task ExecuteAsync(string sql, int id)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await Db(scope).Database.ExecuteSqlRawAsync(sql, id);
    }

    private static MilkshakeDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();

    private static string? Constraint(Exception exception) =>
        exception switch
        {
            PostgresException postgres => postgres.ConstraintName,
            { InnerException: { } inner } => Constraint(inner),
            _ => null,
        };

    private static string? SqlState(Exception exception) =>
        exception switch
        {
            PostgresException postgres => postgres.SqlState,
            { InnerException: { } inner } => SqlState(inner),
            _ => null,
        };
}
