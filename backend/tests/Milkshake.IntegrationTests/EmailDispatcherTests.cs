using System.Globalization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Milkshake.Api;
using Milkshake.Application.Notifications;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Notifications;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Payments;
using Milkshake.Domain.Pricing;
using Milkshake.IntegrationTests.Infrastructure;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M9 Step 8 against disposable PostgreSQL 17: the dispatcher sends each due email once with its idempotency key,
/// retries temporary failures with backoff, stops on permanent ones, suppresses recipients not allow-listed for a
/// real-inbox sender (D4), takes back expired leases, never lets two runs send the same email, and never touches the
/// order or payment.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class EmailDispatcherTests(PostgresApiFactory factory) : IDisposable
{
    private const string AllowedAddress = "allowed.dispatch@example.test";
    // Whole seconds, so times read back from PostgreSQL (microseconds) compare exactly.
    private readonly SettableTimeProvider _clock = new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    private readonly FakeEmailSender _sender = new();
    private WebApplicationFactory<ApiAssemblyMarker>? _app;

    private WebApplicationFactory<ApiAssemblyMarker> App => _app ??= factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Email:PublicBaseUrl", "https://shop.example.test/");
        builder.UseSetting("Email:Dispatch:BatchSize", "10000");
        builder.UseSetting("Email:AllowedRecipients:0", AllowedAddress);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(_sender);
            services.AddSingleton<TimeProvider>(_clock);
        });
    });

    public void Dispose() => _app?.Dispose();

    [Fact]
    public async Task ADueEmail_IsSentOnce_WithItsRenderedContentAndIdempotencyKey()
    {
        (EmailNotification queued, string reference) = await QueueAsync();

        await DispatchAsync();
        await DispatchAsync();

        EmailMessage message = Assert.Single(_sender.Attempts, m => m.NotificationKey == queued.NotificationKey);
        Assert.Equal((queued.RecipientEmail, "Thandi", queued.NotificationKey), (message.To, message.ToName, message.IdempotencyKey));
        Assert.Equal($"Order {reference} has been cancelled", message.Subject);
        Assert.Contains($"https://shop.example.test/orders/{reference}", message.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(reference, message.TextBody, StringComparison.Ordinal);

        EmailNotification stored = await StoredAsync(queued.Id);
        Assert.Equal((NotificationStatus.Sent, 1, "Fake", "fake-" + queued.NotificationKey), (stored.Status, stored.AttemptCount, stored.Provider, stored.ProviderMessageId));
        Assert.NotNull(stored.SentAt);
        Assert.Null(stored.LockedUntil);
    }

    [Fact]
    public async Task ATemporaryFailure_WaitsWithBackoff_ThenSucceeds()
    {
        (EmailNotification queued, _) = await QueueAsync();
        _sender.Respond = m => m.NotificationKey == queued.NotificationKey
            ? EmailSendResult.Temporary(DeliveryError.ProviderUnavailable)
            : EmailSendResult.Success(null);

        await DispatchAsync();
        EmailNotification waiting = await StoredAsync(queued.Id);
        Assert.Equal((NotificationStatus.Pending, 1, DeliveryError.ProviderUnavailable), (waiting.Status, waiting.AttemptCount, waiting.LastError));
        Assert.Equal(_clock.Now + TimeSpan.FromMinutes(1), waiting.NextAttemptAt);

        await DispatchAsync();   // not due yet
        Assert.Equal(1, _sender.AttemptsFor(queued.NotificationKey));

        _clock.Now += TimeSpan.FromSeconds(61);
        _sender.Respond = _ => EmailSendResult.Success("ok");
        await DispatchAsync();

        EmailNotification sent = await StoredAsync(queued.Id);
        Assert.Equal((NotificationStatus.Sent, 2, (DeliveryError?)null), (sent.Status, sent.AttemptCount, sent.LastError));
        Assert.Equal(2, _sender.AttemptsFor(queued.NotificationKey));
    }

    [Fact]
    public async Task ARateLimit_WaitsAsLongAsTheProviderAsks()
    {
        (EmailNotification queued, _) = await QueueAsync();
        _sender.Respond = _ => EmailSendResult.Temporary(DeliveryError.RateLimited, TimeSpan.FromMinutes(10));

        await DispatchAsync();

        EmailNotification waiting = await StoredAsync(queued.Id);
        Assert.Equal((NotificationStatus.Pending, DeliveryError.RateLimited), (waiting.Status, waiting.LastError));
        Assert.Equal(_clock.Now + TimeSpan.FromMinutes(10), waiting.NextAttemptAt);
    }

    [Fact]
    public async Task APermanentFailure_EndsAsFailed_AndIsNotRetried()
    {
        (EmailNotification queued, _) = await QueueAsync();
        _sender.Respond = _ => EmailSendResult.Permanent(DeliveryError.InvalidRecipient);

        await DispatchAsync();
        _clock.Now += TimeSpan.FromHours(2);
        await DispatchAsync();

        EmailNotification failed = await StoredAsync(queued.Id);
        Assert.Equal((NotificationStatus.Failed, 1, DeliveryError.InvalidRecipient), (failed.Status, failed.AttemptCount, failed.LastError));
        Assert.Equal(1, _sender.AttemptsFor(queued.NotificationKey));
    }

    [Fact]
    public async Task EightTemporaryFailures_EndAsFailed()
    {
        (EmailNotification queued, _) = await QueueAsync();
        _sender.Respond = _ => EmailSendResult.Temporary(DeliveryError.Timeout);

        for (int attempt = 0; attempt < 10; attempt++)
        {
            await DispatchAsync();
            _clock.Now += TimeSpan.FromMinutes(61);
        }

        EmailNotification failed = await StoredAsync(queued.Id);
        Assert.Equal((NotificationStatus.Failed, EmailNotification.MaxAttempts, DeliveryError.Timeout), (failed.Status, failed.AttemptCount, failed.LastError));
        Assert.Equal(EmailNotification.MaxAttempts, _sender.AttemptsFor(queued.NotificationKey));
    }

    [Fact]
    public async Task AnUnexpectedSenderError_IsRetriedLater()
    {
        (EmailNotification queued, _) = await QueueAsync();
        _sender.Respond = _ => throw new InvalidOperationException("Simulated sender bug.");

        await DispatchAsync();

        EmailNotification waiting = await StoredAsync(queued.Id);
        Assert.Equal((NotificationStatus.Pending, 1, DeliveryError.ProviderUnavailable), (waiting.Status, waiting.AttemptCount, waiting.LastError));
    }

    [Fact]
    public async Task AnUnrenderableEmail_FailsWithoutBeingSent()
    {
        (EmailNotification queued, _) = await QueueAsync(templateVersion: 99);

        await DispatchAsync();

        EmailNotification failed = await StoredAsync(queued.Id);
        Assert.Equal((NotificationStatus.Failed, DeliveryError.Rejected), (failed.Status, failed.LastError));
        Assert.Equal(0, _sender.AttemptsFor(queued.NotificationKey));
    }

    [Fact]
    public async Task ARealInboxSender_SendsOnlyToAllowListedRecipients()
    {
        _sender.DeliversToRealInboxes = true;
        (EmailNotification allowed, _) = await QueueAsync(recipient: AllowedAddress.ToUpperInvariant());
        (EmailNotification other, _) = await QueueAsync(recipient: "someone.else@example.test");

        await DispatchAsync();

        Assert.Equal(NotificationStatus.Sent, (await StoredAsync(allowed.Id)).Status);
        Assert.Equal(NotificationStatus.Suppressed, (await StoredAsync(other.Id)).Status);
        Assert.Equal(0, _sender.AttemptsFor(other.NotificationKey));
    }

    [Fact]
    public async Task ACaptureSender_IgnoresTheAllowList()
    {
        (EmailNotification queued, _) = await QueueAsync(recipient: "someone.else@example.test");

        await DispatchAsync();

        Assert.Equal(NotificationStatus.Sent, (await StoredAsync(queued.Id)).Status);
    }

    [Fact]
    public async Task WithoutAConfiguredSender_EmailsStayQueued()
    {
        _sender.IsConfigured = false;
        (EmailNotification queued, _) = await QueueAsync();

        int handled = await DispatchAsync();

        EmailNotification stored = await StoredAsync(queued.Id);
        Assert.Equal((0, NotificationStatus.Pending, 0), (handled, stored.Status, stored.AttemptCount));
        Assert.Empty(_sender.Attempts);
    }

    [Theory]
    [InlineData("http://shop.example.test/")]
    [InlineData("ftp://shop.example.test/")]
    public async Task OutsideDevelopment_APlainHttpSiteAddress_KeepsEmailsQueued(string address)
    {
        (EmailNotification queued, _) = await QueueAsync();
        using WebApplicationFactory<ApiAssemblyMarker> insecure = App.WithWebHostBuilder(builder => builder.UseSetting("Email:PublicBaseUrl", address));

        await using (AsyncServiceScope scope = insecure.Services.CreateAsyncScope())
        {
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IEmailDispatcher>().DispatchAsync(CancellationToken.None));
        }

        EmailNotification stored = await StoredAsync(queued.Id);
        Assert.Equal((NotificationStatus.Pending, 0), (stored.Status, stored.AttemptCount));
        Assert.Empty(_sender.Attempts);
    }

    [Fact]
    public async Task AHeldLease_IsRespected_AndAnExpiredOneIsTakenBack()
    {
        (EmailNotification queued, _) = await QueueAsync();
        await using (AsyncServiceScope scope = App.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            EmailNotification tracked = await db.EmailNotifications.SingleAsync(n => n.Id == queued.Id);
            tracked.Claim(_clock.Now, TimeSpan.FromMinutes(5));   // a dispatcher that stopped mid-send
            await db.SaveChangesAsync();
        }

        await DispatchAsync();
        Assert.Equal(0, _sender.AttemptsFor(queued.NotificationKey));

        _clock.Now += TimeSpan.FromMinutes(6);
        await DispatchAsync();

        EmailNotification sent = await StoredAsync(queued.Id);
        Assert.Equal((NotificationStatus.Sent, 2), (sent.Status, sent.AttemptCount));
        Assert.Equal(queued.NotificationKey, Assert.Single(_sender.Attempts, m => m.NotificationKey == queued.NotificationKey).IdempotencyKey);
    }

    [Fact]
    public async Task TwoRunsAtOnce_NeverSendTheSameEmailTwice()
    {
        var queued = new List<EmailNotification>();
        for (int i = 0; i < 6; i++)
        {
            queued.Add((await QueueAsync()).Email);
        }

        await Task.WhenAll(DispatchAsync(), DispatchAsync(), DispatchAsync());

        Assert.All(queued, email => Assert.Equal(1, _sender.AttemptsFor(email.NotificationKey)));
    }

    [Fact]
    public async Task AProviderOutage_LeavesTheConfirmedOrderAndPaymentUntouched()
    {
        _sender.Respond = _ => EmailSendResult.Temporary(DeliveryError.ProviderUnavailable);
        (int orderId, _) = await PlacedOrderAsync();
        int paymentId;
        await using (AsyncServiceScope scope = App.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            Order order = await db.Orders.SingleAsync(o => o.Id == orderId);
            Payment payment = Payment.Start(order, Payment.NewReference(), Payment.PaystackProvider, Guid.NewGuid());
            db.Payments.Add(payment);
            await db.SaveChangesAsync();
            payment.ApplyVerifiedOutcome(order, VerifiedOutcome.Paid, payment.AmountMinor, payment.CurrencyCode,
                Random.Shared.NextInt64(1, long.MaxValue), "Approved", false, _clock.Now);
            await db.SaveChangesAsync();
            paymentId = payment.Id;
        }

        await DispatchAsync();

        await using AsyncServiceScope check = App.Services.CreateAsyncScope();
        MilkshakeDbContext read = Db(check);
        Assert.Equal(OrderStatus.Confirmed, (await read.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId)).Status);
        Assert.Equal(PaymentStatus.Succeeded, (await read.Payments.AsNoTracking().SingleAsync(p => p.Id == paymentId)).Status);
        EmailNotification email = await read.EmailNotifications.AsNoTracking().SingleAsync(n => n.OrderId == orderId);
        Assert.Equal((NotificationKind.OrderConfirmed, NotificationStatus.Pending, 1), (email.Kind, email.Status, email.AttemptCount));
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    private async Task<int> DispatchAsync()
    {
        await using AsyncServiceScope scope = App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IEmailDispatcher>().DispatchAsync(CancellationToken.None);
    }

    /// <summary>A renderable "order cancelled" email with its own key, due now.</summary>
    private async Task<(EmailNotification Email, string Reference)> QueueAsync(string? recipient = null, int templateVersion = EmailPayloads.CurrentTemplateVersion)
    {
        (int orderId, string reference) = await PlacedOrderAsync();
        var payload = new OrderCancelledPayload(reference, false, false, 0m, 0m, 0m, null, "ZAR", "en-ZA");
        EmailNotification email = EmailNotification.Create(NotificationKind.OrderCancelled, $"dispatch-test:{Guid.NewGuid():N}", orderId, null, null,
            recipient ?? $"patron-{Guid.NewGuid():N}@example.test", "Thandi", templateVersion, EmailPayloads.Serialize(payload), _clock.Now);

        await using AsyncServiceScope scope = App.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        db.EmailNotifications.Add(email);
        await db.SaveChangesAsync();
        return (email, reference);
    }

    private async Task<EmailNotification> StoredAsync(int id)
    {
        await using AsyncServiceScope scope = App.Services.CreateAsyncScope();
        return await Db(scope).EmailNotifications.AsNoTracking().SingleAsync(n => n.Id == id);
    }

    /// <summary>A PendingPayment order; the manager's account doubles as the patron.</summary>
    private async Task<(int OrderId, string Reference)> PlacedOrderAsync()
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
        return (order.Id, order.Reference);
    }

    private static MilkshakeDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
}
