using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Milkshake.Api;
using Milkshake.Api.Contracts.Management;
using Milkshake.Application.Abstractions;
using Milkshake.Application.Notifications;
using Milkshake.Application.Ordering;
using Milkshake.Application.Payments;
using Milkshake.Domain.Auditing;
using Milkshake.Domain.Notifications;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;
using Milkshake.Domain.Pricing;
using Milkshake.Domain.Restaurants;
using Milkshake.IntegrationTests.Infrastructure;
using Milkshake.Infrastructure.Persistence;
using Npgsql;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M11 against disposable PostgreSQL 17: audit records and security events are append-only in the database and in
/// the application (AC-11.3), and every audit record names who acted (AC-11.1) – the signed-in user, "anonymous", or
/// a named system process that is never confused with a browser user.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class AuditIntegrityTests(PostgresApiFactory factory) : IDisposable
{
    private const string RaiseException = "P0001";
    private readonly FakeEmailSender _sender = new();
    private readonly FakePaymentGateway _gateway = new();
    private WebApplicationFactory<ApiAssemblyMarker>? _app;

    private WebApplicationFactory<ApiAssemblyMarker> App => _app ??= factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Email:PublicBaseUrl", "https://shop.example.test/");
        builder.UseSetting("Email:Dispatch:BatchSize", "10000");
        builder.UseSetting("Payments:Reconciliation:BatchSize", "10000");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(_sender);
            services.AddSingleton<IPaymentGateway>(_gateway);
        });
    });

    public void Dispose() => _app?.Dispose();

    // ---- append-only (AC-11.3) -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("UPDATE milkshake.audit_logs SET table_name = 'tampered' WHERE id = (SELECT max(id) FROM milkshake.audit_logs)")]
    [InlineData("UPDATE milkshake.audit_logs SET changes = '[]'::jsonb WHERE id = (SELECT max(id) FROM milkshake.audit_logs)")]
    [InlineData("DELETE FROM milkshake.audit_logs WHERE id = (SELECT max(id) FROM milkshake.audit_logs)")]
    [InlineData("TRUNCATE milkshake.audit_logs")]
    [InlineData("UPDATE milkshake.security_events SET email = 'tampered@example.test' WHERE id = (SELECT max(id) FROM milkshake.security_events)")]
    [InlineData("DELETE FROM milkshake.security_events WHERE id = (SELECT max(id) FROM milkshake.security_events)")]
    [InlineData("TRUNCATE milkshake.security_events")]
    public async Task TheDatabase_RefusesToChangeOrDeleteAuditHistory(string sql)
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();   // guarantees an audit row and a login event
        (long audits, long events) before = await CountsAsync();

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(sql));

        Assert.Equal(RaiseException, refused.SqlState);
        Assert.Contains("append-only", refused.MessageText, StringComparison.Ordinal);
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task TheApplication_RefusesToChangeOrDeleteAnAuditRecord()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        AuditLog latest = await db.AuditLogs.OrderByDescending(a => a.Id).FirstAsync();

        db.Entry(latest).Property(a => a.Changes).CurrentValue = "[]";
        InvalidOperationException changed = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(latest).State = EntityState.Unchanged;
        db.AuditLogs.Remove(latest);
        InvalidOperationException deleted = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        Assert.Contains("append-only", changed.Message, StringComparison.Ordinal);
        Assert.Contains("append-only", deleted.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheApplication_RefusesToDeleteASecurityEvent()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        db.SecurityEvents.Remove(await db.SecurityEvents.OrderByDescending(e => e.Id).FirstAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    // ---- actor attribution (AC-11.1) -----------------------------------------------------------------------------

    [Fact]
    public async Task AManagersChange_IsAttributedToTheManager()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        RestaurantResponse restaurant = await manager.CreateTestRestaurantAsync();

        AuditLog audit = await SingleAuditAsync("restaurants", restaurant.Id, AuditAction.Insert);
        Guid managerId = await UserIdAsync(manager.Email);
        Assert.Equal((AuditActors.User, (Guid?)managerId, manager.Email), (audit.Actor, audit.UserId, audit.UserEmail));
    }

    [Fact]
    public async Task ARegistration_IsAttributedToAnAnonymousVisitor()
    {
        string email = RegisterBody.UniqueEmail();
        using HttpClient client = factory.CreateHttpsClient();

        using (HttpResponseMessage registered = await client.PostWithCsrfAsync("/api/auth/register", RegisterBody.Valid(email)))
        {
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        }

        AuditLog audit = await SingleAuditAsync("users", await UserIdAsync(email), AuditAction.Insert);
        Assert.Equal((AuditActors.Anonymous, (Guid?)null, (string?)null), (audit.Actor, audit.UserId, audit.UserEmail));
    }

    [Fact]
    public async Task TheEmailDispatchersClaimAndResult_AreAuditedAndAttributedToIt()
    {
        int emailId = await QueueEmailAsync();

        await using (AsyncServiceScope scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IEmailDispatcher>().DispatchAsync(CancellationToken.None);
        }

        // The claim (to Sending) and the result (to Sent) are both ordinary audited saves (M11; the M9 claim was raw SQL).
        List<AuditLog> updates = await AuditsAsync("email_notifications", emailId, AuditAction.Update);
        Assert.Equal(2, updates.Count);
        Assert.Contains("\"Sending\"", updates[0].Changes, StringComparison.Ordinal);
        Assert.Contains("\"Sent\"", updates[1].Changes, StringComparison.Ordinal);
        Assert.All(updates, a => Assert.Equal((AuditActors.EmailDispatcher, (Guid?)null), (a.Actor, a.UserId)));
    }

    [Fact]
    public async Task PaymentReconciliation_IsAttributedToItself()
    {
        (int orderId, int paymentId, string paymentReference, long amount) = await OpenPaymentAsync();
        _gateway.OnVerify = reference => reference == paymentReference
            ? FakePaymentGateway.Paid(amount)
            : new GatewayVerification(true, VerifiedOutcome.Open, 0, "ZAR", null, null);

        await using (AsyncServiceScope scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPaymentReconciler>().ReconcileAsync(CancellationToken.None);
        }

        AuditLog payment = Assert.Single(await AuditsAsync("payments", paymentId, AuditAction.Update));
        AuditLog order = Assert.Single(await AuditsAsync("orders", orderId, AuditAction.Update));
        Assert.All(new[] { payment, order }, a => Assert.Equal((AuditActors.PaymentReconciliation, (Guid?)null), (a.Actor, a.UserId)));
    }

    [Fact]
    public async Task ASystemScope_IsNeverAttributedToTheSignedInUser()
    {
        var someone = new FixedCurrentUser(Guid.NewGuid(), "someone@example.test");
        using WebApplicationFactory<ApiAssemblyMarker> app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICurrentUser>();
                services.AddScoped<ICurrentUser>(_ => someone);
            }));

        Restaurant asUser = new($"Audit actor user {Guid.NewGuid():N}", null);
        Restaurant asSystem = new($"Audit actor system {Guid.NewGuid():N}", null);
        await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            db.Restaurants.Add(asUser);
            await db.SaveChangesAsync();

            using (AuditActorScope.Begin(AuditActors.Seeding))
            {
                db.Restaurants.Add(asSystem);
                await db.SaveChangesAsync();
            }
        }

        AuditLog userAudit = await SingleAuditAsync("restaurants", asUser.Id, AuditAction.Insert);
        AuditLog systemAudit = await SingleAuditAsync("restaurants", asSystem.Id, AuditAction.Insert);
        Assert.Equal((AuditActors.User, (Guid?)someone.UserId, someone.Email), (userAudit.Actor, userAudit.UserId, userAudit.UserEmail));
        Assert.Equal((AuditActors.Seeding, (Guid?)null, (string?)null), (systemAudit.Actor, systemAudit.UserId, systemAudit.UserEmail));
    }

    [Fact]
    public async Task TheDatabase_RefusesAnUnknownActor()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            "INSERT INTO milkshake.audit_logs (id, action, changes, occurred_at, record_id, table_name, actor) " +
            "VALUES (-1, 'Insert', '[]', now(), '1', 'restaurants', 'root')"));

        Assert.Equal("23514", refused.SqlState);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    private sealed class FixedCurrentUser(Guid userId, string email) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;

        public string? Email { get; } = email;
    }

    private async Task<(long Audits, long Events)> CountsAsync()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        return (await db.AuditLogs.LongCountAsync(), await db.SecurityEvents.LongCountAsync());
    }

    private async Task<List<AuditLog>> AuditsAsync(string table, object id, AuditAction action)
    {
        string recordId = Convert.ToString(id, CultureInfo.InvariantCulture)!;
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await Db(scope).AuditLogs.AsNoTracking()
            .Where(a => a.TableName == table && a.RecordId == recordId && a.Action == action)
            .OrderBy(a => a.Id)
            .ToListAsync();
    }

    private async Task<AuditLog> SingleAuditAsync(string table, object id, AuditAction action) =>
        Assert.Single(await AuditsAsync(table, id, action));

    private async Task<Guid> UserIdAsync(string email)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await Db(scope).Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await Db(scope).Database.ExecuteSqlRawAsync(sql);
    }

    private async Task<int> QueueEmailAsync()
    {
        (int orderId, string reference, _) = await PlacedOrderAsync();
        var payload = new OrderCancelledPayload(reference, false, false, 0m, 0m, 0m, null, "ZAR", "en-ZA");
        EmailNotification email = EmailNotification.Create(NotificationKind.OrderCancelled, $"audit-test:{Guid.NewGuid():N}", orderId, null, null,
            $"patron-{Guid.NewGuid():N}@example.test", "Thandi", EmailPayloads.CurrentTemplateVersion, EmailPayloads.Serialize(payload), DateTimeOffset.UtcNow);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        db.EmailNotifications.Add(email);
        await db.SaveChangesAsync();
        return email.Id;
    }

    /// <summary>An order with an open (Pending) payment attempt, as after the patron opened the checkout.</summary>
    private async Task<(int OrderId, int PaymentId, string PaymentReference, long AmountMinor)> OpenPaymentAsync()
    {
        (int orderId, _, _) = await PlacedOrderAsync();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        Order order = await db.Orders.SingleAsync(o => o.Id == orderId);
        Payment payment = Payment.Start(order, Payment.NewReference(), Payment.PaystackProvider, Guid.NewGuid());
        payment.CheckoutCreated(new Uri("https://checkout.paystack.com/audit-test"), "checkout.paystack.com");
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return (orderId, payment.Id, payment.Reference, payment.AmountMinor);
    }

    /// <summary>A PendingPayment order; the manager's account doubles as the patron.</summary>
    private async Task<(int OrderId, string Reference, string PatronEmail)> PlacedOrderAsync()
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
        return (order.Id, order.Reference, manager.Email);
    }

    private static MilkshakeDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
}
