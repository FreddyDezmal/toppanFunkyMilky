using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api;
using Milkshake.Api.Contracts.Management;
using Milkshake.Application.Notifications;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Auditing;
using Milkshake.Domain.Notifications;
using Milkshake.Domain.Orders;
using Milkshake.Domain.Pricing;
using Milkshake.IntegrationTests.Infrastructure;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M9 Management → Emails (AC-9.5, D6) against disposable PostgreSQL 17: managers only, delivery state without
/// content, filters, resend of sent or failed emails only (CSRF-protected, rate-limited, audited).
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class ManagerEmailsTests(PostgresApiFactory factory)
{
    private const string ContentMarker = "Secret Content Branch";

    [Fact]
    public async Task TheList_ShowsDeliveryStateNewestFirst_WithCounts_AndNeverTheContent()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        (int orderId, string reference) = await PlacedOrderAsync(manager);
        EmailNotification older = await QueueAsync(orderId, reference, NotificationStatus.Failed);
        EmailNotification newer = await QueueAsync(orderId, reference, NotificationStatus.Sent);

        using HttpResponseMessage response = await manager.Client.GetAsync(new Uri($"/api/manager/emails?order={reference.ToLowerInvariant()}", UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();
        EmailDeliveryListResponse? list = await response.Content.ReadFromJsonAsync<EmailDeliveryListResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([newer.Id, older.Id], list!.Items.Select(i => i.Id).ToArray());
        EmailDeliveryResponse failed = list.Items[1];
        Assert.Equal(("OrderCancelled", "Failed", reference, older.RecipientEmail, 1, "Rejected", true),
            (failed.Kind, failed.Status, failed.OrderReference, failed.RecipientEmail, failed.AttemptCount, failed.LastError, failed.CanResend));
        Assert.Equal(["Failed", "Pending", "Sending", "Sent", "Suppressed"], list.Counts.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.True(list.Counts["Failed"] >= 1);
        Assert.DoesNotContain(ContentMarker, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheList_CanShowOneStatus()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        (int orderId, string reference) = await PlacedOrderAsync(manager);
        await QueueAsync(orderId, reference, NotificationStatus.Sent);
        EmailNotification failed = await QueueAsync(orderId, reference, NotificationStatus.Failed);

        EmailDeliveryListResponse? list = await manager.Client.GetFromJsonAsync<EmailDeliveryListResponse>(
            new Uri($"/api/manager/emails?status=failed&order={reference}", UriKind.Relative));

        Assert.Equal(failed.Id, Assert.Single(list!.Items).Id);
    }

    [Theory]
    [InlineData("status=Delivered")]
    [InlineData("status=1")]
    [InlineData("order=MS-0000-0000-0000-0000-0000-0000-0000")]
    public async Task InvalidFilters_AreRejected(string query)
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        using HttpResponseMessage response = await manager.Client.GetAsync(new Uri($"/api/manager/emails?{query}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(NotificationStatus.Failed)]
    [InlineData(NotificationStatus.Sent)]
    public async Task Resending_QueuesTheEmailAgainWithANewIdempotencyKey_AndIsAudited(NotificationStatus status)
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        (int orderId, string reference) = await PlacedOrderAsync(manager);
        EmailNotification email = await QueueAsync(orderId, reference, status);

        using HttpResponseMessage response = await manager.Client.PostWithCsrfAsync($"/api/manager/emails/{email.Id}/resend", new { });
        EmailDeliveryResponse? resent = await response.Content.ReadFromJsonAsync<EmailDeliveryResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("Pending", 0, 1, false, (string?)null), (resent!.Status, resent.AttemptCount, resent.ResendCount, resent.CanResend, resent.LastError));
        Assert.NotNull(resent.NextAttemptAt);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        EmailNotification stored = await db.EmailNotifications.AsNoTracking().SingleAsync(n => n.Id == email.Id);
        Assert.Equal(email.NotificationKey + "#resend-1", stored.IdempotencyKey);
        string recordId = email.Id.ToString(CultureInfo.InvariantCulture);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.TableName == "email_notifications" && a.RecordId == recordId &&
                                                     a.Action == AuditAction.Update && a.UserEmail == manager.Email));
    }

    [Theory]
    [InlineData(NotificationStatus.Pending)]
    [InlineData(NotificationStatus.Sending)]
    [InlineData(NotificationStatus.Suppressed)]
    public async Task AnEmailThatIsNotSentOrFailed_CannotBeResent(NotificationStatus status)
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        (int orderId, string reference) = await PlacedOrderAsync(manager);
        EmailNotification email = await QueueAsync(orderId, reference, status);

        using HttpResponseMessage response = await manager.Client.PostWithCsrfAsync($"/api/manager/emails/{email.Id}/resend", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(status, (await StoredAsync(email.Id)).Status);
    }

    [Fact]
    public async Task AnUnknownEmail_IsNotFound()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        using HttpResponseMessage response = await manager.Client.PostWithCsrfAsync("/api/manager/emails/2147483647/resend", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AResendWithoutTheAntiForgeryToken_IsRejected()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        (int orderId, string reference) = await PlacedOrderAsync(manager);
        EmailNotification email = await QueueAsync(orderId, reference, NotificationStatus.Failed);

        using HttpResponseMessage response = await manager.Client.PostAsJsonAsync(new Uri($"/api/manager/emails/{email.Id}/resend", UriKind.Relative), new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NotificationStatus.Failed, (await StoredAsync(email.Id)).Status);
    }

    [Fact]
    public async Task PatronsAndAnonymousVisitors_CannotSeeOrResendEmails()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        (int orderId, string reference) = await PlacedOrderAsync(manager);
        EmailNotification email = await QueueAsync(orderId, reference, NotificationStatus.Failed);
        using HttpClient patron = await factory.CreatePatronClientAsync();
        using HttpClient anonymous = factory.CreateHttpsClient();

        using HttpResponseMessage patronList = await patron.GetAsync(new Uri("/api/manager/emails", UriKind.Relative));
        using HttpResponseMessage patronResend = await patron.PostWithCsrfAsync($"/api/manager/emails/{email.Id}/resend", new { });
        using HttpResponseMessage anonymousList = await anonymous.GetAsync(new Uri("/api/manager/emails", UriKind.Relative));

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized),
            (patronList.StatusCode, patronResend.StatusCode, anonymousList.StatusCode));
        Assert.Equal(NotificationStatus.Failed, (await StoredAsync(email.Id)).Status);
    }

    [Fact]
    public async Task Resends_AreRateLimitedPerManager()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        (int orderId, string reference) = await PlacedOrderAsync(manager);
        var emails = new List<EmailNotification>();
        for (int i = 0; i < 3; i++)
        {
            emails.Add(await QueueAsync(orderId, reference, NotificationStatus.Failed));
        }

        using WebApplicationFactory<ApiAssemblyMarker> app = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:EmailResendPermitLimit", "2"));
        using HttpClient client = app.CreateSecureClient();
        using (HttpResponseMessage login = await client.PostWithCsrfAsync("/api/auth/login", new LoginBody(manager.Email, RegisterBody.ValidPassword)))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        var statuses = new List<HttpStatusCode>();
        foreach (EmailNotification email in emails)
        {
            using HttpResponseMessage response = await client.PostWithCsrfAsync($"/api/manager/emails/{email.Id}/resend", new { });
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses.ToArray());
        Assert.Equal(NotificationStatus.Failed, (await StoredAsync(emails[2].Id)).Status);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    /// <summary>An "order cancelled" email for the order, brought to the given status through the domain rules.</summary>
    private async Task<EmailNotification> QueueAsync(int orderId, string reference, NotificationStatus status)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var payload = new OrderCancelledPayload(reference, false, false, 0m, 0m, 0m, ContentMarker, "ZAR", "en-ZA");
        EmailNotification email = EmailNotification.Create(NotificationKind.OrderCancelled, $"manager-emails-test:{Guid.NewGuid():N}", orderId, null, null,
            $"patron-{Guid.NewGuid():N}@example.test", "Thandi", EmailPayloads.CurrentTemplateVersion, EmailPayloads.Serialize(payload), now);
        if (status != NotificationStatus.Pending)
        {
            email.Claim(now, TimeSpan.FromMinutes(5));
        }

        switch (status)
        {
            case NotificationStatus.Sent:
                email.MarkSent("Mailpit", "mp-1", now);
                break;
            case NotificationStatus.Failed:
                email.MarkAttemptFailed(DeliveryError.Rejected, permanent: true, now);
                break;
            case NotificationStatus.Suppressed:
                email.MarkSuppressed();
                break;
        }

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);
        db.EmailNotifications.Add(email);
        await db.SaveChangesAsync();
        return email;
    }

    private async Task<EmailNotification> StoredAsync(int id)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await Db(scope).EmailNotifications.AsNoTracking().SingleAsync(n => n.Id == id);
    }

    /// <summary>A PendingPayment order; the manager's account doubles as the patron.</summary>
    private async Task<(int OrderId, string Reference)> PlacedOrderAsync(ManagerSession manager)
    {
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
