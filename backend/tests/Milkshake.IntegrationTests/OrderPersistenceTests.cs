using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Auditing;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Pricing;
using Milkshake.Domain.Settings;
using Milkshake.IntegrationTests.Infrastructure;
using Milkshake.Infrastructure.Persistence;
using Npgsql;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M7 database tests against disposable PostgreSQL 17: atomic order + items + audit (AC-7.1, AC-7.5), snapshots that
/// survive catalogue, restaurant and VAT changes (AC-7.2), the idempotency constraint (decision O5), check
/// constraints, and restrict-only foreign keys that keep order history.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class OrderPersistenceTests(PostgresApiFactory factory)
{
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";
    private const string ForeignKeyViolation = "23503";

    [Fact]
    public async Task AnOrder_ItsItems_AndTheirAuditRows_AreSavedTogether()
    {
        (Guid patron, TestMenu menu) = await ArrangeAsync();
        Order order = Order.Place(patron, OrderReference.New(), Restaurant(menu), PickupUtc(), "Africa/Johannesburg",
            await PriceAsync(menu, drinks: 2), Guid.NewGuid(), Fingerprint());

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            Order stored = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Reference == order.Reference);

            // 2 × (12.00 + 6.00 + 8.00) = 52.00; VAT 15% = 7.80; total 59.80.
            Assert.Equal((OrderStatus.PendingPayment, 52.00m, 0m, 52.00m, 0.15m, 7.80m, 59.80m, "ZAR"),
                (stored.Status, stored.Subtotal, stored.DiscountAmount, stored.Taxable, stored.VatRate, stored.Vat, stored.Total, stored.CurrencyCode));
            Assert.Equal([1, 2], stored.Items.Select(i => i.Number).Order());
            Assert.All(stored.Items, item => Assert.Equal((menu.Flavour.Name, 12.00m, 26.00m), (item.FlavourName, item.FlavourFee, item.Price)));

            string[] itemIds = stored.Items.Select(i => i.Id.ToString(CultureInfo.InvariantCulture)).ToArray();
            int orderAudits = await db.AuditLogs.CountAsync(a =>
                a.TableName == "orders" && a.RecordId == stored.Id.ToString() && a.Action == AuditAction.Insert);
            int itemAudits = await db.AuditLogs.CountAsync(a =>
                a.TableName == "order_items" && itemIds.Contains(a.RecordId) && a.Action == AuditAction.Insert);
            Assert.Equal((1, 2), (orderAudits, itemAudits));
        }
    }

    [Fact]
    public async Task AFailureWhileSaving_LeavesNoOrderNoItemsAndNoAudit()
    {
        (Guid patron, TestMenu menu) = await ArrangeAsync();
        Order order = Order.Place(patron, OrderReference.New(), Restaurant(menu), PickupUtc(), "Africa/Johannesburg",
            await PriceAsync(menu, drinks: 2), Guid.NewGuid(), Fingerprint());
        int auditsBefore = await CountAuditsAsync();

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            db.Orders.Add(order);

            // Force a failure on the LAST row written: the second item's price no longer equals its fees.
            db.Entry(order.Items[1]).Property(i => i.Price).CurrentValue = 1.00m;

            DbUpdateException failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(CheckViolation, SqlState(failure));
        }

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            Assert.False(await db.Orders.AnyAsync(o => o.Reference == order.Reference));
            Assert.False(await db.OrderItems.AnyAsync(i => i.OrderId == order.Id));
        }

        Assert.Equal(auditsBefore, await CountAuditsAsync());
    }

    [Fact]
    public async Task StoredOrders_KeepTheirNamesFeesVatAndRestaurant_AfterManagementChanges()
    {
        (Guid patron, TestMenu menu) = await ArrangeAsync();
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        Order order = Order.Place(patron, OrderReference.New(), Restaurant(menu), PickupUtc(), "Africa/Johannesburg",
            await PriceAsync(menu, drinks: 1), Guid.NewGuid(), Fingerprint());
        await SaveAsync(order);

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            await db.Database.ExecuteSqlAsync($"UPDATE milkshake.flavours SET name = {"Renamed " + Guid.NewGuid().ToString("N")[..8]} WHERE id = {menu.Flavour.Id}");
            await db.Database.ExecuteSqlAsync($"UPDATE milkshake.toppings SET is_active = false WHERE id = {menu.Topping.Id}");
            await db.Database.ExecuteSqlAsync($"UPDATE milkshake.consistency_prices SET amount = amount + 5 WHERE consistency_id = {menu.Consistency.Id}");
            await db.Database.ExecuteSqlAsync($"UPDATE milkshake.restaurants SET name = {"Moved " + Guid.NewGuid().ToString("N")[..8]}, address = 'Elsewhere' WHERE id = {menu.Restaurant.Id}");
        }

        await manager.ChangeSettingAsync(SettingKeys.VatRate, "0.20");
        try
        {
            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            Order stored = await Db(scope).Orders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Id == order.Id);
            OrderItem item = Assert.Single(stored.Items);

            Assert.Equal((menu.Flavour.Name, 12.00m, menu.Consistency.Name, 6.00m, menu.Topping.Name, 8.00m, 26.00m),
                (item.FlavourName, item.FlavourFee, item.ConsistencyName, item.ConsistencyFee, item.ToppingName, item.ToppingFee, item.Price));
            Assert.Equal((menu.Restaurant.Name, 0.15m, 3.90m, 29.90m), (stored.RestaurantName, stored.VatRate, stored.Vat, stored.Total));
        }
        finally
        {
            await manager.ChangeSettingAsync(SettingKeys.VatRate, "0.15");
        }
    }

    [Fact]
    public async Task TheSameKey_CannotCreateASecondOrderForThePatron_ButIsIndependentPerPatron()
    {
        (Guid patron, TestMenu menu) = await ArrangeAsync();
        Guid otherPatron = await OtherUserAsync(patron);
        PriceBreakdown pricing = await PriceAsync(menu, drinks: 1);
        var key = Guid.NewGuid();

        await SaveAsync(Order.Place(patron, OrderReference.New(), Restaurant(menu), PickupUtc(), "UTC", pricing, key, Fingerprint()));

        DbUpdateException duplicate = await Assert.ThrowsAsync<DbUpdateException>(() =>
            SaveAsync(Order.Place(patron, OrderReference.New(), Restaurant(menu), PickupUtc(), "UTC", pricing, key, Fingerprint())));
        Assert.Equal(UniqueViolation, SqlState(duplicate));

        await SaveAsync(Order.Place(otherPatron, OrderReference.New(), Restaurant(menu), PickupUtc(), "UTC", pricing, key, Fingerprint()));

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        Assert.Equal(2, await Db(scope).Orders.CountAsync(o => o.IdempotencyKey == key));
    }

    [Fact]
    public async Task ConcurrentInsertsWithTheSameKey_StoreExactlyOneOrder()
    {
        (Guid patron, TestMenu menu) = await ArrangeAsync();
        PriceBreakdown pricing = await PriceAsync(menu, drinks: 1);
        var key = Guid.NewGuid();

        Task[] attempts = Enumerable.Range(0, 5)
            .Select(_ => SaveAsync(Order.Place(patron, OrderReference.New(), Restaurant(menu), PickupUtc(), "UTC", pricing, key, Fingerprint())))
            .ToArray();
        try
        {
            await Task.WhenAll(attempts);
        }
        catch (DbUpdateException)
        {
            // Expected for the losers; each is checked below.
        }

        Assert.Equal(1, attempts.Count(t => t.IsCompletedSuccessfully));
        Assert.All(attempts.Where(t => t.IsFaulted), t => Assert.Equal(UniqueViolation, SqlState(t.Exception!.InnerException!)));

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await Db(scope).Orders.CountAsync(o => o.PatronId == patron && o.IdempotencyKey == key));
    }

    [Theory]
    [InlineData("UPDATE milkshake.orders SET total = total + 1 WHERE id = {0}")]
    [InlineData("UPDATE milkshake.orders SET taxable = taxable - 1 WHERE id = {0}")]
    [InlineData("UPDATE milkshake.orders SET status = 'Paid' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.orders SET discount_amount = subtotal + 1 WHERE id = {0}")]
    [InlineData("UPDATE milkshake.orders SET vat_rate = 1.5 WHERE id = {0}")]
    [InlineData("UPDATE milkshake.orders SET reference = 'MS-0000-000I' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.order_items SET price = price + 1 WHERE order_id = {0}")]
    [InlineData("UPDATE milkshake.order_items SET number = 0 WHERE order_id = {0}")]
    public async Task CheckConstraints_RejectAmountsThatDoNotAddUp_AndInvalidValues(string sql)
    {
        Order order = await SavedOrderAsync();

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        PostgresException failure = await Assert.ThrowsAsync<PostgresException>(() =>
            Db(scope).Database.ExecuteSqlRawAsync(sql, order.Id));

        Assert.Equal(CheckViolation, failure.SqlState);
    }

    [Theory]
    [InlineData("DELETE FROM milkshake.orders WHERE id = {0}")]
    [InlineData("DELETE FROM milkshake.restaurants WHERE id = (SELECT restaurant_id FROM milkshake.orders WHERE id = {0})")]
    [InlineData("DELETE FROM milkshake.users WHERE id = (SELECT patron_id FROM milkshake.orders WHERE id = {0})")]
    [InlineData("DELETE FROM milkshake.flavours WHERE id = (SELECT flavour_id FROM milkshake.order_items WHERE order_id = {0} LIMIT 1)")]
    public async Task Deleting_AnythingAnOrderDependsOn_IsRefused(string sql)
    {
        Order order = await SavedOrderAsync();

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        PostgresException failure = await Assert.ThrowsAsync<PostgresException>(() =>
            Db(scope).Database.ExecuteSqlRawAsync(sql, order.Id));

        Assert.Equal(ForeignKeyViolation, failure.SqlState);
        Assert.True(await Db(scope).OrderItems.AnyAsync(i => i.OrderId == order.Id));
    }

    [Fact]
    public async Task AStatusChange_IsAuditedWithItsOldAndNewValue()
    {
        Order order = await SavedOrderAsync();

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            Order tracked = await db.Orders.SingleAsync(o => o.Id == order.Id);
            tracked.Cancel(tracked.PickupAtUtc.AddMinutes(-1));
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            AuditLog audit = await Db(scope).AuditLogs.SingleAsync(a =>
                a.TableName == "orders" && a.RecordId == order.Id.ToString() && a.Action == AuditAction.Update);
            Assert.Contains("PendingPayment", audit.Changes, StringComparison.Ordinal);
            Assert.Contains("Cancelled", audit.Changes, StringComparison.Ordinal);
            Assert.DoesNotContain("total", audit.Changes, StringComparison.Ordinal);
        }
    }

    private async Task<Order> SavedOrderAsync()
    {
        (Guid patron, TestMenu menu) = await ArrangeAsync();
        Order order = Order.Place(patron, OrderReference.New(), Restaurant(menu), PickupUtc(), "UTC",
            await PriceAsync(menu, drinks: 2), Guid.NewGuid(), Fingerprint());
        await SaveAsync(order);
        return order;
    }

    /// <summary>A fresh menu and a patron to own the order. Database tests use the manager's user ID as the owner;
    /// role checks are API concerns, tested with the endpoints.</summary>
    private async Task<(Guid Patron, TestMenu Menu)> ArrangeAsync()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        Guid patron = await Db(scope).Users.Where(u => u.Email == manager.Email).Select(u => u.Id).SingleAsync();
        return (patron, menu);
    }

    private async Task<Guid> OtherUserAsync(Guid except)
    {
        using HttpClient patronClient = await factory.CreatePatronClientAsync();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await Db(scope).Users.Where(u => u.Id != except).OrderByDescending(u => u.CreatedAt).Select(u => u.Id).FirstAsync();
    }

    private async Task<PriceBreakdown> PriceAsync(TestMenu menu, int drinks)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        OrderingOptions options = (await scope.ServiceProvider.GetRequiredService<IOrderingCatalogService>().GetOptionsAsync(CancellationToken.None)).Options
            ?? throw new InvalidOperationException("Ordering is unavailable in the test database.");
        string pickupLocal = options.Pickup.LatestLocal.ToString(PickupRules.LocalFormat, CultureInfo.InvariantCulture);
        var draft = new OrderDraft(menu.Restaurant.Id, pickupLocal, Enumerable.Repeat<DraftDrinkSelection?>(menu.Drink(), drinks).ToList());
        DraftPricingResult result = await scope.ServiceProvider.GetRequiredService<IOrderPricingService>().PriceDraftAsync(draft, CancellationToken.None);
        return result.Price ?? throw new InvalidOperationException("Pricing failed.");
    }

    private async Task SaveAsync(Order order)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
    }

    private async Task<int> CountAuditsAsync()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await Db(scope).AuditLogs.CountAsync(a => a.TableName == "orders" || a.TableName == "order_items");
    }

    private static MilkshakeDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();

    private static OrderRestaurant Restaurant(TestMenu menu) => new(menu.Restaurant.Id, menu.Restaurant.Name, null);

    private static DateTimeOffset PickupUtc() => new(DateTimeOffset.UtcNow.AddDays(1).UtcDateTime.Date.AddHours(8), TimeSpan.Zero);

    private static string Fingerprint() => Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + Convert.ToHexStringLower(Guid.NewGuid().ToByteArray());

    private static string? SqlState(Exception exception) =>
        exception switch
        {
            PostgresException postgres => postgres.SqlState,
            { InnerException: { } inner } => SqlState(inner),
            _ => null,
        };
}
