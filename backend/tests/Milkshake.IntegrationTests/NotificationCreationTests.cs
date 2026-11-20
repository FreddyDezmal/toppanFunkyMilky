using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Application.Notifications;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Auditing;
using Milkshake.Domain.Notifications;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;
using Milkshake.Domain.Pricing;
using Milkshake.Domain.Settings;
using Milkshake.IntegrationTests.Infrastructure;
using Milkshake.Infrastructure.Identity;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M9 Step 7 against disposable PostgreSQL 17: each email-worthy event queues its outbox row in the same SaveChanges as
/// the business change (both or neither), addressed to the patron's registered address, audited, never twice, and
/// never at the cost of the business save.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class NotificationCreationTests(PostgresApiFactory factory)
{
    [Fact]
    public async Task ConfirmingAPaidOrder_QueuesTheReceipt_AndAuditsIt()
    {
        (int orderId, string patronEmail) = await PlacedOrderAsync();
        int paymentId = await StartPaymentAsync(orderId);

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            Order order = await db.Orders.SingleAsync(o => o.Id == orderId);   // items not loaded, as in the payment services
            Payment payment = await db.Payments.SingleAsync(p => p.Id == paymentId);
            Pay(order, payment);
            await db.SaveChangesAsync();
        }

        await using AsyncServiceScope check = factory.Services.CreateAsyncScope();
        MilkshakeDbContext read = Db(check);
        EmailNotification email = await read.EmailNotifications.AsNoTracking().SingleAsync(n => n.OrderId == orderId);
        Order stored = await read.Orders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Id == orderId);
        Payment paid = await read.Payments.AsNoTracking().SingleAsync(p => p.Id == paymentId);
        string locale = await read.SystemSettings.AsNoTracking().Where(s => s.Key == SettingKeys.DisplayLocale).Select(s => s.Value).SingleAsync();

        Assert.Equal((NotificationKind.OrderConfirmed, NotificationStatus.Pending, patronEmail, $"order-confirmed:{orderId}"),
            (email.Kind, email.Status, email.RecipientEmail, email.NotificationKey));
        OrderConfirmedPayload payload = EmailPayloads.Deserialize<OrderConfirmedPayload>(email.Payload);
        Assert.Equal((stored.Reference, stored.Total, paid.Reference, locale, stored.Items.Count),
            (payload.OrderReference, payload.Total, payload.PaymentReference, payload.Locale, payload.Drinks.Count));
        Assert.Equal(stored.Items[0].FlavourName, payload.Drinks[0].Flavour);

        string recordId = email.Id.ToString(CultureInfo.InvariantCulture);
        AuditLog audit = await read.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.TableName == "email_notifications" && a.RecordId == recordId && a.Action == AuditAction.Insert);
        Assert.DoesNotContain(patronEmail, audit.Changes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenTheBusinessSaveFails_NoEmailIsQueued()
    {
        (int orderId, _) = await PlacedOrderAsync();
        int paymentId = await StartPaymentAsync(orderId);

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            Order order = await db.Orders.SingleAsync(o => o.Id == orderId);
            Payment payment = await db.Payments.SingleAsync(p => p.Id == paymentId);
            Pay(order, payment);

            // Someone else changes the order first: the row version no longer matches and the whole save rolls back.
            await ExecuteAsync("UPDATE milkshake.orders SET updated_at = updated_at WHERE id = {0}", orderId);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
        }

        await using AsyncServiceScope check = factory.Services.CreateAsyncScope();
        Assert.False(await Db(check).EmailNotifications.AnyAsync(n => n.OrderId == orderId));
        Assert.Equal(OrderStatus.PendingPayment, (await Db(check).Orders.AsNoTracking().SingleAsync(o => o.Id == orderId)).Status);
    }

    [Fact]
    public async Task APatronCancellation_QueuesTheCancellationWithTheStoredRefund_NotARefundCompletion()
    {
        (int orderId, _) = await PaidOrderAsync();

        Refund refund = await CancelAsync(orderId, byRestaurant: false);

        await using AsyncServiceScope check = factory.Services.CreateAsyncScope();
        EmailNotification email = await Db(check).EmailNotifications.AsNoTracking().SingleAsync(n => n.OrderId == orderId && n.Kind != NotificationKind.OrderConfirmed);
        Assert.Equal((NotificationKind.OrderCancelled, (int?)null), (email.Kind, email.RefundId));
        OrderCancelledPayload payload = EmailPayloads.Deserialize<OrderCancelledPayload>(email.Payload);
        Assert.Equal((true, false, refund.Reference), (payload.WasPaid, payload.AtRestaurantRequest, payload.RefundReference));
        Assert.Equal(MinorUnits.FromMinor(refund.FeeMinor, refund.CurrencyCode), payload.Fee);
        Assert.Equal(MinorUnits.FromMinor(refund.AmountMinor, refund.CurrencyCode), payload.RefundAmount);
        Assert.True(payload.Fee > 0m);
    }

    [Theory]
    [InlineData(VerifiedRefundOutcome.Processed, NotificationKind.RefundProcessed)]
    [InlineData(VerifiedRefundOutcome.Failed, NotificationKind.RefundFailed)]
    public async Task ARefundOutcome_QueuesItsEmail_LinkedToTheRefund(VerifiedRefundOutcome outcome, NotificationKind kind)
    {
        (int orderId, _) = await PaidOrderAsync();
        Refund created = await CancelAsync(orderId, byRestaurant: false);

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            Refund refund = await db.Refunds.SingleAsync(r => r.Id == created.Id);
            Assert.True(refund.ApplyVerifiedOutcome(outcome, refund.AmountMinor, Random.Shared.NextInt64(1, long.MaxValue), null, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        await using AsyncServiceScope check = factory.Services.CreateAsyncScope();
        EmailNotification email = await Db(check).EmailNotifications.AsNoTracking().SingleAsync(n => n.RefundId == created.Id);
        Assert.Equal((kind, $"{(kind == NotificationKind.RefundProcessed ? "refund-processed" : "refund-failed")}:{created.Id}"), (email.Kind, email.NotificationKey));
        RefundOutcomePayload payload = EmailPayloads.Deserialize<RefundOutcomePayload>(email.Payload);
        Assert.Equal((created.Reference, MinorUnits.FromMinor(created.AmountMinor, created.CurrencyCode)), (payload.RefundReference, payload.Amount));
    }

    [Fact]
    public async Task ACancellationRequest_QueuesItsEmail_AndAcceptingItQueuesARestaurantCancellation()
    {
        (int orderId, _) = await PaidOrderAsync();
        int requestId;

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            Order order = await db.Orders.SingleAsync(o => o.Id == orderId);
            CancellationRequest request = CancellationRequest.Create(order, order.PatronId, "Blender broken", Before(order));   // the manager doubles as the patron
            db.CancellationRequests.Add(request);
            await db.SaveChangesAsync();
            requestId = request.Id;
        }

        await using (AsyncServiceScope check = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext read = Db(check);
            EmailNotification email = await read.EmailNotifications.AsNoTracking().SingleAsync(n => n.CancellationRequestId == requestId);
            Order order = await read.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            Assert.Equal((NotificationKind.CancellationRequested, $"cancellation-requested:{requestId}"), (email.Kind, email.NotificationKey));
            CancellationRequestedPayload payload = EmailPayloads.Deserialize<CancellationRequestedPayload>(email.Payload);
            Assert.Equal(("Blender broken", true, order.Total), (payload.Reason, payload.IsPaid, payload.RefundAmount));
        }

        await CancelAsync(orderId, byRestaurant: true, requestId);

        await using AsyncServiceScope after = factory.Services.CreateAsyncScope();
        EmailNotification cancelled = await Db(after).EmailNotifications.AsNoTracking()
            .SingleAsync(n => n.OrderId == orderId && n.Kind == NotificationKind.OrderCancelled);
        OrderCancelledPayload cancelledPayload = EmailPayloads.Deserialize<OrderCancelledPayload>(cancelled.Payload);
        Assert.Equal((true, true, 0m), (cancelledPayload.WasPaid, cancelledPayload.AtRestaurantRequest, cancelledPayload.Fee));
    }

    [Fact]
    public async Task AnUnpaidCancellation_QueuesAnEmailWithoutRefund()
    {
        (int orderId, _) = await PlacedOrderAsync();

        await CancelAsync(orderId, byRestaurant: false);

        await using AsyncServiceScope check = factory.Services.CreateAsyncScope();
        EmailNotification email = await Db(check).EmailNotifications.AsNoTracking().SingleAsync(n => n.OrderId == orderId);
        OrderCancelledPayload payload = EmailPayloads.Deserialize<OrderCancelledPayload>(email.Payload);
        Assert.Equal((NotificationKind.OrderCancelled, false, 0m), (email.Kind, payload.WasPaid, payload.RefundAmount));
    }

    [Fact]
    public async Task AnEmailAlreadyQueued_DoesNotBlockTheBusinessSave_OrQueueASecond()
    {
        (int orderId, string patronEmail) = await PlacedOrderAsync();
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            db.EmailNotifications.Add(EmailNotification.Create(NotificationKind.OrderCancelled, EmailNotification.KeyFor(NotificationKind.OrderCancelled, orderId),
                orderId, null, null, patronEmail, "Earlier", EmailPayloads.CurrentTemplateVersion, "{}", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        await CancelAsync(orderId, byRestaurant: false);

        await using AsyncServiceScope check = factory.Services.CreateAsyncScope();
        MilkshakeDbContext read = Db(check);
        Assert.Equal(OrderStatus.Cancelled, (await read.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId)).Status);
        EmailNotification only = await read.EmailNotifications.AsNoTracking().SingleAsync(n => n.OrderId == orderId);
        Assert.Equal("Earlier", only.RecipientName);
    }

    [Fact]
    public async Task APatronWithoutAValidAddress_StillCancels_AndNoEmailIsQueued()
    {
        Guid patron;
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            string address = RegisterBody.UniqueEmail();
            var user = new ApplicationUser { UserName = address, Email = address, FirstName = "Sipho" };
            IdentityResult created = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user);
            Assert.True(created.Succeeded);
            patron = user.Id;
        }

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            await Db(scope).Database.ExecuteSqlRawAsync("UPDATE milkshake.users SET email = 'sipho at example' WHERE id = {0}", patron);
        }

        (int orderId, _) = await PlacedOrderAsync(patron);

        await CancelAsync(orderId, byRestaurant: false);

        await using AsyncServiceScope check = factory.Services.CreateAsyncScope();
        MilkshakeDbContext read = Db(check);
        Assert.Equal(OrderStatus.Cancelled, (await read.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId)).Status);
        Assert.False(await read.EmailNotifications.AnyAsync(n => n.OrderId == orderId));
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    /// <summary>A moment before the pickup time, so the order may still be cancelled whatever the time of day.</summary>
    private static DateTimeOffset Before(Order order) => order.PickupAtUtc.AddMinutes(-5);

    private static void Pay(Order order, Payment payment) =>
        payment.ApplyVerifiedOutcome(order, VerifiedOutcome.Paid, payment.AmountMinor, payment.CurrencyCode,
            Random.Shared.NextInt64(1, long.MaxValue), "Approved", false, DateTimeOffset.UtcNow);

    /// <summary>Cancels as the cancellation service does: order, accepted request and refund in one SaveChanges.</summary>
    private async Task<Refund> CancelAsync(int orderId, bool byRestaurant, int? requestId = null)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        Order order = await db.Orders.SingleAsync(o => o.Id == orderId);
        DateTimeOffset at = Before(order);
        if (requestId is int id)
        {
            CancellationRequest request = await db.CancellationRequests.SingleAsync(r => r.Id == id);
            request.Accept(order, at);
        }

        Payment? paid = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.OrderId == orderId && p.Status == PaymentStatus.Succeeded);
        order.Cancel(at);
        Refund? refund = null;
        if (paid is not null)
        {
            refund = Refund.ForCancellation(order, paid, Refund.NewReference(), 0.10m, byRestaurant);
            db.Refunds.Add(refund);
        }

        await db.SaveChangesAsync();
        return refund!;
    }

    private async Task<(int OrderId, string PatronEmail)> PaidOrderAsync()
    {
        (int orderId, string email) = await PlacedOrderAsync();
        int paymentId = await StartPaymentAsync(orderId);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        Pay(await db.Orders.SingleAsync(o => o.Id == orderId), await db.Payments.SingleAsync(p => p.Id == paymentId));
        await db.SaveChangesAsync();
        return (orderId, email);
    }

    private async Task<int> StartPaymentAsync(int orderId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        Order order = await db.Orders.SingleAsync(o => o.Id == orderId);
        Payment payment = Payment.Start(order, Payment.NewReference(), Payment.PaystackProvider, Guid.NewGuid());
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return payment.Id;
    }

    /// <summary>A PendingPayment order; by default the manager's account doubles as the patron.</summary>
    private async Task<(int OrderId, string PatronEmail)> PlacedOrderAsync(Guid? patronId = null)
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        Guid patron = patronId ?? await db.Users.Where(u => u.Email == manager.Email).Select(u => u.Id).SingleAsync();

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
        return (order.Id, manager.Email);
    }

    private async Task ExecuteAsync(string sql, int id)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await Db(scope).Database.ExecuteSqlRawAsync(sql, id);
    }

    private static MilkshakeDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
}
