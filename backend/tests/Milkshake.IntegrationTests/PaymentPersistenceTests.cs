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
/// M8 payment tables against disposable PostgreSQL 17: one success and one open attempt per order enforced by the
/// database (P3), webhook deduplication (AC-8.4), atomic payment + order changes and audit (AC-8.5), check constraints,
/// and restrict-only foreign keys.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class PaymentPersistenceTests(PostgresApiFactory factory)
{
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";
    private const string ForeignKeyViolation = "23503";
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AVerifiedPayment_AndTheOrdersConfirmation_AreSavedTogether_AndAudited()
    {
        Order order = await SavedOrderAsync();
        Payment payment = await SavedPaymentAsync(order);

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            Payment tracked = await db.Payments.SingleAsync(p => p.Id == payment.Id);
            Order trackedOrder = await db.Orders.SingleAsync(o => o.Id == order.Id);
            tracked.ApplyVerifiedOutcome(trackedOrder, VerifiedOutcome.Paid, tracked.AmountMinor, "ZAR", 9001, "Approved", false, Now);
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            Assert.Equal(PaymentStatus.Succeeded, (await db.Payments.SingleAsync(p => p.Id == payment.Id)).Status);
            Assert.Equal(OrderStatus.Confirmed, (await db.Orders.SingleAsync(o => o.Id == order.Id)).Status);
            string paymentId = payment.Id.ToString(CultureInfo.InvariantCulture);
            string orderId = order.Id.ToString(CultureInfo.InvariantCulture);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.TableName == "payments" && a.RecordId == paymentId && a.Action == AuditAction.Insert));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.TableName == "payments" && a.RecordId == paymentId && a.Action == AuditAction.Update));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.TableName == "orders" && a.RecordId == orderId && a.Action == AuditAction.Update));
        }
    }

    [Fact]
    public async Task TheDatabase_AllowsOnlyOneOpenAttemptPerOrder()
    {
        Order order = await SavedOrderAsync();
        await SavedPaymentAsync(order);

        DbUpdateException second = await Assert.ThrowsAsync<DbUpdateException>(() => SavedPaymentAsync(order));

        Assert.Equal("ux_payments_one_open_per_order", Constraint(second));
    }

    [Fact]
    public async Task TheDatabase_AllowsOnlyOneSuccessfulPaymentPerOrder_EvenWhenTheDomainIsBypassed()
    {
        Order order = await SavedOrderAsync();
        Payment first = await SavedPaymentAsync(order);
        await ExecuteAsync("UPDATE milkshake.payments SET status = 'Succeeded' WHERE id = {0}", first.Id);
        Payment second = await SavedPaymentAsync(order);

        PostgresException failure = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync("UPDATE milkshake.payments SET status = 'Succeeded' WHERE id = {0}", second.Id));

        Assert.Equal((UniqueViolation, "ux_payments_one_success_per_order"), (failure.SqlState, failure.ConstraintName));
    }

    [Fact]
    public async Task SimultaneousAttempts_ForOneOrder_StoreExactlyOneOpenAttempt()
    {
        Order order = await SavedOrderAsync();

        Task[] attempts = Enumerable.Range(0, 5).Select(_ => SavedPaymentAsync(order)).ToArray();
        try
        {
            await Task.WhenAll(attempts);
        }
        catch (DbUpdateException)
        {
            // Expected for the losers.
        }

        Assert.Equal(1, attempts.Count(t => t.IsCompletedSuccessfully));
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await Db(scope).Payments.CountAsync(p => p.OrderId == order.Id));
    }

    [Fact]
    public async Task ARepeatedNotification_IsStoredOnce()
    {
        long transactionId = Random.Shared.NextInt64(1, long.MaxValue);
        await SaveEventAsync(new PaymentEvent(Payment.PaystackProvider, "charge.success", transactionId, "MSP-0000000000AA", Now));

        DbUpdateException duplicate = await Assert.ThrowsAsync<DbUpdateException>(() =>
            SaveEventAsync(new PaymentEvent(Payment.PaystackProvider, "charge.success", transactionId, "MSP-0000000000AA", Now)));

        Assert.Equal(UniqueViolation, SqlState(duplicate));
        await SaveEventAsync(new PaymentEvent(Payment.PaystackProvider, "refund.processed", transactionId, "MSP-0000000000AA", Now));
    }

    [Theory]
    [InlineData("UPDATE milkshake.payments SET amount_minor = 0 WHERE id = {0}")]
    [InlineData("UPDATE milkshake.payments SET status = 'Paid' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.payments SET status = 'RequiresRefund' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.payments SET refund_reason = 'AlreadyPaid' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.payments SET reference = 'MSP-123' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.payments SET checkout_url = 'http://checkout.paystack.com/x' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.payments SET currency_code = 'za1' WHERE id = {0}")]
    public async Task CheckConstraints_RejectInvalidPaymentRows(string sql)
    {
        Payment payment = await SavedPaymentAsync(await SavedOrderAsync());

        PostgresException failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(sql, payment.Id));

        Assert.Equal(CheckViolation, failure.SqlState);
    }

    [Fact]
    public async Task AnOrderWithPayments_CannotBeDeleted()
    {
        Order order = await SavedOrderAsync();
        await SavedPaymentAsync(order);

        await ExecuteAsync("DELETE FROM milkshake.order_items WHERE order_id = {0}", order.Id);
        PostgresException failure = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync("DELETE FROM milkshake.orders WHERE id = {0}", order.Id));

        Assert.Equal(ForeignKeyViolation, failure.SqlState);
    }

    private async Task<Payment> SavedPaymentAsync(Order order)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        Order current = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Payment payment = Payment.Start(current, Payment.NewReference(), Payment.PaystackProvider, Guid.NewGuid());
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    private async Task SaveEventAsync(PaymentEvent paymentEvent)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        db.PaymentEvents.Add(paymentEvent);
        await db.SaveChangesAsync();
    }

    private async Task<Order> SavedOrderAsync()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        Guid patron = await db.Users.Where(u => u.Email == manager.Email).Select(u => u.Id).SingleAsync();

        OrderingOptions options = (await scope.ServiceProvider.GetRequiredService<IOrderingCatalogService>().GetOptionsAsync(CancellationToken.None)).Options
            ?? throw new InvalidOperationException("Ordering is unavailable in the test database.");
        string pickupLocal = options.Pickup.LatestLocal.ToString(PickupRules.LocalFormat, CultureInfo.InvariantCulture);
        DraftPricingResult priced = await scope.ServiceProvider.GetRequiredService<IOrderPricingService>().PriceDraftAsync(
            new OrderDraft(menu.Restaurant.Id, pickupLocal, [menu.Drink()]), CancellationToken.None);
        PriceBreakdown price = priced.Price ?? throw new InvalidOperationException("Pricing failed.");

        Order order = Order.Place(patron, OrderReference.New(), new OrderRestaurant(menu.Restaurant.Id, menu.Restaurant.Name, null),
            priced.Draft!.Pickup.Utc.ToUniversalTime(), "UTC", price with { PricedAtUtc = price.PricedAtUtc.ToUniversalTime() },
            Guid.NewGuid(), Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()));
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
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
