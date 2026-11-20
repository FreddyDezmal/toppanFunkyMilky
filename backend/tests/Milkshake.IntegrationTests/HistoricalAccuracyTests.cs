using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api;
using Milkshake.Api.Contracts.Management;
using Milkshake.Api.Contracts.Ordering;
using Milkshake.Application.Payments;
using Milkshake.Domain.Notifications;
using Milkshake.Domain.Payments;
using Milkshake.Domain.Settings;
using Milkshake.IntegrationTests.Infrastructure;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M11 (decision 1; roadmap AC-7.2, AC-10.4, AC-10.5, R-04) against disposable PostgreSQL 17: a paid and cancelled
/// order keeps exactly what was bought – its order page, its place in My orders, its cancellation figures, its stored
/// refund and its receipt email – after the flavour is renamed, the topping deactivated, the restaurant renamed and
/// moved, a price changed, and the VAT rate and cancellation fee rate changed.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class HistoricalAccuracyTests(PostgresApiFactory factory) : IDisposable
{
    private const string Key = "sk_test_integration_tests_only";
    private readonly FakePaymentGateway _gateway = new();
    private WebApplicationFactory<ApiAssemblyMarker>? _app;

    private WebApplicationFactory<ApiAssemblyMarker> App => _app ??= factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Payments:Paystack:ReturnBaseUrl", "https://localhost:5173/");
        builder.UseSetting("Payments:Paystack:SecretKey", Key);
        builder.ConfigureTestServices(services => services.AddSingleton<IPaymentGateway>(_gateway));
    });

    public void Dispose() => _app?.Dispose();

    [Fact]
    public async Task AnOldOrder_IsUnchangedByLaterCatalogueRestaurantPriceVatAndFeeChanges()
    {
        // A menu, a restaurant with an address, and a paid order the patron then cancelled (10% fee).
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        RestaurantResponse restaurant = await SendAsync<RestaurantResponse>(manager.Client, HttpMethod.Put, $"/api/manager/restaurants/{menu.Restaurant.Id}",
            new { name = menu.Restaurant.Name, address = "1 Original Road", version = menu.Restaurant.Version });

        using HttpClient patron = App.CreateSecureClient();
        using (HttpResponseMessage registered = await patron.PostWithCsrfAsync("/api/auth/register", RegisterBody.Valid()))
        {
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        }

        string order = await PlaceOrderAsync(patron, menu);
        await PayAsync(patron, order);
        await PostAsync(patron, $"/api/ordering/orders/{order}/cancel", new { expectedRefund = 27.30m });

        Snapshot before = await SnapshotAsync(patron, order);
        Assert.Contains("1 Original Road", before.OrderPage, StringComparison.Ordinal);
        Assert.Contains(menu.Flavour.Name, before.OrderPage, StringComparison.Ordinal);
        Assert.Equal((2990L, 260L, 2730L), (before.Refund.PaidAmountMinor, before.Refund.FeeMinor, before.Refund.AmountMinor));

        List<SettingResponse> settings = await manager.Client.GetFromJsonAsync<List<SettingResponse>>(new Uri("/api/manager/settings", UriKind.Relative))
            ?? throw new InvalidOperationException("No settings returned.");
        string vat = settings.Single(s => s.Key == SettingKeys.VatRate).Value;
        string feeRate = settings.Single(s => s.Key == SettingKeys.RefundFeeRate).Value;
        try
        {
            // Everything a manager can change about what was bought, how it was priced and what cancelling costs.
            CatalogItemResponse renamed = await SendAsync<CatalogItemResponse>(manager.Client, HttpMethod.Put, $"/api/manager/catalog/flavours/{menu.Flavour.Id}",
                new { name = menu.Flavour.Name + " (new recipe)", displayOrder = menu.Flavour.DisplayOrder, version = menu.Flavour.Version });
            await SendAsync<CatalogItemResponse>(manager.Client, HttpMethod.Post, $"/api/manager/catalog/toppings/{menu.Topping.Id}/deactivate",
                new { version = menu.Topping.Version });
            await SendAsync<RestaurantResponse>(manager.Client, HttpMethod.Put, $"/api/manager/restaurants/{restaurant.Id}",
                new { name = restaurant.Name + " (moved)", address = "99 New Street", version = restaurant.Version });
            await SendAsync<PriceHistoryResponse>(manager.Client, HttpMethod.Post, $"/api/manager/catalog/consistencies/{menu.Consistency.Id}/prices",
                new { amount = 9.99m, effectiveFrom = (DateTimeOffset?)null, version = menu.Consistency.Version });
            await manager.ChangeSettingAsync(SettingKeys.VatRate, vat == "0.2" ? "0.18" : "0.2");
            await manager.ChangeSettingAsync(SettingKeys.RefundFeeRate, feeRate == "0.25" ? "0.3" : "0.25");
            Assert.EndsWith("(new recipe)", renamed.Name, StringComparison.Ordinal);

            Snapshot after = await SnapshotAsync(patron, order);

            Assert.Equal(before.OrderPage, after.OrderPage);
            Assert.Equal(before.MyOrdersEntry, after.MyOrdersEntry);
            Assert.Equal(before.CancellationPage, after.CancellationPage);
            Assert.Equal(before.Refund, after.Refund);
            Assert.Equal(before.ReceiptPayload, after.ReceiptPayload);
            Assert.DoesNotContain("new recipe", after.OrderPage, StringComparison.Ordinal);
            Assert.DoesNotContain("99 New Street", after.OrderPage, StringComparison.Ordinal);
        }
        finally
        {
            await manager.ChangeSettingAsync(SettingKeys.VatRate, vat);
            await manager.ChangeSettingAsync(SettingKeys.RefundFeeRate, feeRate);
        }
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    private sealed record StoredRefund(long PaidAmountMinor, decimal FeeRate, long FeeMinor, long AmountMinor, string Kind, string Reference);

    private sealed record Snapshot(string OrderPage, string MyOrdersEntry, string CancellationPage, StoredRefund Refund, string ReceiptPayload);

    private async Task<Snapshot> SnapshotAsync(HttpClient patron, string order)
    {
        string page = await patron.GetStringAsync(new Uri($"/api/ordering/orders/{order}", UriKind.Relative));
        string cancellation = await patron.GetStringAsync(new Uri($"/api/ordering/orders/{order}/cancellation", UriKind.Relative));
        using JsonDocument list = JsonDocument.Parse(await patron.GetStringAsync(new Uri("/api/ordering/orders", UriKind.Relative)));
        string entry = list.RootElement.EnumerateArray().Single(o => o.GetProperty("reference").GetString() == order).GetRawText();

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        int orderId = await db.Orders.Where(o => o.Reference == order).Select(o => o.Id).SingleAsync();
        Refund refund = await db.Refunds.AsNoTracking().SingleAsync(r => r.OrderId == orderId);
        string receipt = await db.EmailNotifications.Where(n => n.OrderId == orderId && n.Kind == NotificationKind.OrderConfirmed)
            .Select(n => n.Payload).SingleAsync();

        return new Snapshot(page, entry, cancellation,
            new StoredRefund(refund.PaidAmountMinor, refund.FeeRate, refund.FeeMinor, refund.AmountMinor, refund.Kind.ToString(), refund.Reference),
            receipt);
    }

    private static async Task<string> PlaceOrderAsync(HttpClient patron, TestMenu menu)
    {
        OrderingOptionsResponse options = await patron.GetFromJsonAsync<OrderingOptionsResponse>(new Uri("/api/ordering/options", UriKind.Relative))
            ?? throw new InvalidOperationException("No options returned.");
        var draft = new
        {
            restaurantId = menu.Restaurant.Id,
            pickupLocal = options.Pickup.LatestLocal,
            drinks = new[] { new { flavourId = menu.Flavour.Id, consistencyId = menu.Consistency.Id, toppingId = menu.Topping.Id } },
            expectedTotal = 29.90m,
            expectedCurrencyCode = "ZAR",
        };
        using HttpResponseMessage placed = await PostRawAsync(patron, "/api/ordering/orders", JsonSerializer.Serialize(draft), idempotent: true);
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        return (await placed.ReadJsonAsync<OrderResponse>()).Reference;
    }

    private async Task PayAsync(HttpClient patron, string order)
    {
        using HttpResponseMessage started = await PostRawAsync(patron, $"/api/ordering/orders/{order}/payments", "{}", idempotent: true);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        string payment = (await started.ReadJsonAsync<PaymentCheckoutResponse>()).PaymentReference;
        _gateway.OnVerify = reference => reference == payment
            ? FakePaymentGateway.Paid(2990)
            : new GatewayVerification(true, VerifiedOutcome.Open, 0, "ZAR", null, null);
        await PostAsync(patron, $"/api/ordering/orders/{order}/payments/refresh", new { });
    }

    private static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string path, object body)
    {
        using HttpResponseMessage response = await client.SendWithCsrfAsync(method, path, body);
        Assert.True(response.IsSuccessStatusCode, $"{method} {path}: {(int)response.StatusCode}");
        return await response.ReadJsonAsync<T>();
    }

    private static async Task PostAsync(HttpClient client, string path, object body)
    {
        using HttpResponseMessage response = await PostRawAsync(client, path, JsonSerializer.Serialize(body), idempotent: false);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"POST {path}: {(int)response.StatusCode}");
    }

    private static async Task<HttpResponseMessage> PostRawAsync(HttpClient client, string path, string json, bool idempotent)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-XSRF-TOKEN", await client.GetCsrfTokenAsync());
        if (idempotent)
        {
            request.Headers.Add(OrderMessages.IdempotencyKeyHeader, Guid.NewGuid().ToString());
        }

        return await client.SendAsync(request);
    }
}
