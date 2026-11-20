using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Auditing;
using Milkshake.Domain.Notifications;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Pricing;
using Milkshake.IntegrationTests.Infrastructure;
using Milkshake.Infrastructure.Persistence;
using Npgsql;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M9 outbox table against disposable PostgreSQL 17: one email per notification key, checks that keep status, lease,
/// timestamps and references consistent, restrict-only foreign keys, and an audit trail that never shows the address
/// or content.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class EmailNotificationPersistenceTests(PostgresApiFactory factory)
{
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";
    private const string ForeignKeyViolation = "23503";
    private const string Address = "thandi.persistence@example.test";

    [Fact]
    public async Task AnEmail_IsStoredWithItsJsonPayload_AndAuditedWithoutTheAddressOrContent()
    {
        int orderId = await SavedOrderAsync();
        EmailNotification email = await SaveAsync(Notification(orderId));

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        EmailNotification stored = await db.EmailNotifications.AsNoTracking().SingleAsync(n => n.Id == email.Id);
        Assert.Equal((NotificationStatus.Pending, Address, 1), (stored.Status, stored.RecipientEmail, stored.TemplateVersion));
        Assert.Contains("MS-7KQ2-X9TD", stored.Payload, StringComparison.Ordinal);

        string recordId = email.Id.ToString(CultureInfo.InvariantCulture);
        AuditLog audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.TableName == "email_notifications" && a.RecordId == recordId && a.Action == AuditAction.Insert);
        string changes = audit.Changes;
        Assert.DoesNotContain(Address, changes, StringComparison.Ordinal);
        Assert.DoesNotContain("MS-7KQ2-X9TD", changes, StringComparison.Ordinal);
        Assert.DoesNotContain("Thandi", changes, StringComparison.Ordinal);
        Assert.Contains("***", changes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSameEvent_CanOnlyQueueOneEmail()
    {
        int orderId = await SavedOrderAsync();
        await SaveAsync(Notification(orderId));

        DbUpdateException duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(Notification(orderId)));

        Assert.Equal((UniqueViolation, "ix_email_notifications_notification_key"), (SqlState(duplicate), Constraint(duplicate)));
    }

    [Theory]
    [InlineData("UPDATE milkshake.email_notifications SET status = 'Sent' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.email_notifications SET status = 'Sending' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.email_notifications SET status = 'Delivered' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.email_notifications SET kind = 'Newsletter' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.email_notifications SET kind = 'RefundProcessed' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.email_notifications SET kind = 'CancellationRequested' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.email_notifications SET attempt_count = -1 WHERE id = {0}")]
    [InlineData("UPDATE milkshake.email_notifications SET last_error = 'Exploded' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.email_notifications SET recipient_email = E'a@example.test\\r\\nBcc: b@example.test' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.email_notifications SET recipient_email = 'a@example.test, b@example.test' WHERE id = {0}")]
    [InlineData("UPDATE milkshake.email_notifications SET recipient_email = 'nobody' WHERE id = {0}")]
    public async Task CheckConstraints_RejectInconsistentRows(string sql)
    {
        EmailNotification email = await SaveAsync(Notification(await SavedOrderAsync()));

        PostgresException failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(sql, email.Id));

        Assert.Equal(CheckViolation, failure.SqlState);
    }

    [Fact]
    public async Task AnOrderWithEmails_CannotBeDeleted()
    {
        int orderId = await SavedOrderAsync();
        await SaveAsync(Notification(orderId));

        await ExecuteAsync("DELETE FROM milkshake.order_items WHERE order_id = {0}", orderId);
        PostgresException failure = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync("DELETE FROM milkshake.orders WHERE id = {0}", orderId));

        Assert.Equal(ForeignKeyViolation, failure.SqlState);
    }

    [Fact]
    public async Task AClaimAndASend_AreStoredConsistently()
    {
        EmailNotification email = await SaveAsync(Notification(await SavedOrderAsync()));

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            EmailNotification tracked = await db.EmailNotifications.SingleAsync(n => n.Id == email.Id);
            tracked.Claim(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
            await db.SaveChangesAsync();
            tracked.MarkSent("Mailpit", "mp-1", DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        await using AsyncServiceScope check = factory.Services.CreateAsyncScope();
        EmailNotification stored = await Db(check).EmailNotifications.AsNoTracking().SingleAsync(n => n.Id == email.Id);
        Assert.Equal((NotificationStatus.Sent, 1, "mp-1"), (stored.Status, stored.AttemptCount, stored.ProviderMessageId));
        Assert.NotNull(stored.SentAt);
        Assert.Null(stored.LockedUntil);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    private static EmailNotification Notification(int orderId) =>
        EmailNotification.Create(NotificationKind.OrderConfirmed, EmailNotification.KeyFor(NotificationKind.OrderConfirmed, orderId), orderId,
            null, null, Address, "Thandi", 1, "{\"reference\":\"MS-7KQ2-X9TD\",\"total\":29.90}", DateTimeOffset.UtcNow);

    private async Task<EmailNotification> SaveAsync(EmailNotification email)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        db.EmailNotifications.Add(email);
        await db.SaveChangesAsync();
        return email;
    }

    private async Task<int> SavedOrderAsync()
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
        return order.Id;
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
