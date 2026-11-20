using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api.Contracts.Ordering;
using Milkshake.IntegrationTests.Infrastructure;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.IntegrationTests;

/// <summary>M7 order retrieval: own orders only (AC-7.6), stored snapshots after catalogue changes (AC-7.2).</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class OrderRetrievalTests(PostgresApiFactory factory)
{
    private const string OrdersPath = "/api/ordering/orders";

    [Fact]
    public async Task APatron_CanReadTheirOrder_AsItWasPlaced()
    {
        (HttpClient patron, _, OrderResponse placed) = await PlaceOrderAsync();
        using (patron)
        {
            using HttpResponseMessage response = await patron.GetAsync(new Uri($"{OrdersPath}/{placed.Reference}", UriKind.Relative));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
            OrderResponse read = await response.ReadJsonAsync<OrderResponse>();
            Assert.Equal((placed.Reference, "PendingPayment", false, true), (read.Reference, read.Status, read.IsPaid, read.AwaitsPayment));
            Assert.Equal(placed.Totals, read.Totals);
            Assert.Equal(JsonSerializer.Serialize(placed.Drinks), JsonSerializer.Serialize(read.Drinks));
            Assert.Equal(placed.Pickup, read.Pickup);
        }
    }

    [Fact]
    public async Task AStoredOrder_KeepsItsNamesAndPrices_AfterTheCatalogueChanges()
    {
        (HttpClient patron, TestMenu menu, OrderResponse placed) = await PlaceOrderAsync();
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        using (patron)
        {
            using HttpResponseMessage repriced = await manager.Client.SendWithCsrfAsync(
                HttpMethod.Post, $"/api/manager/catalog/flavours/{menu.Flavour.Id}/prices",
                new { amount = 20.00m, effectiveFrom = (DateTimeOffset?)null, version = menu.Flavour.Version });
            Assert.Equal(HttpStatusCode.OK, repriced.StatusCode);
            await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
            {
                MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
                await db.Database.ExecuteSqlAsync($"UPDATE milkshake.flavours SET name = {"Renamed " + Guid.NewGuid().ToString("N")[..8]} WHERE id = {menu.Flavour.Id}");
                await db.Database.ExecuteSqlAsync($"UPDATE milkshake.restaurants SET name = {"Moved " + Guid.NewGuid().ToString("N")[..8]} WHERE id = {menu.Restaurant.Id}");
            }

            OrderResponse read = await patron.GetFromJsonAsync<OrderResponse>(new Uri($"{OrdersPath}/{placed.Reference}", UriKind.Relative))
                ?? throw new InvalidOperationException("No order returned.");

            OrderDrinkResponse drink = Assert.Single(read.Drinks);
            Assert.Equal((menu.Flavour.Name, 12.00m, 26.00m), (drink.Flavour.Name, drink.Flavour.Fee, drink.Price));
            Assert.Equal((menu.Restaurant.Name, 29.90m), (read.Restaurant.Name, read.Totals.Total));
        }
    }

    [Theory]
    [InlineData("other")]
    [InlineData("MS-0000-0000")]
    [InlineData("not-a-reference")]
    [InlineData("ms-0000-0000")]
    public async Task AnotherPatronsOrder_AnUnknownAndAMalformedReference_AllLookTheSame(string which)
    {
        (HttpClient owner, _, OrderResponse placed) = await PlaceOrderAsync();
        using HttpClient other = await factory.CreatePatronClientAsync();
        using (owner)
        {
            string reference = which == "other" ? placed.Reference : which;
            using HttpResponseMessage response = await other.GetAsync(new Uri($"{OrdersPath}/{reference}", UriKind.Relative));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            string body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(placed.Reference, body, StringComparison.Ordinal);
            Assert.DoesNotContain("total", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task TheList_ShowsOnlyThePatronsOwnOrders_NewestFirst()
    {
        (HttpClient patron, TestMenu menu, OrderResponse first) = await PlaceOrderAsync();
        (HttpClient other, _, OrderResponse othersOrder) = await PlaceOrderAsync();
        using (patron)
        using (other)
        {
            OrderResponse second = await PlaceAsync(patron, menu);

            List<OrderSummaryResponse> mine = await patron.GetFromJsonAsync<List<OrderSummaryResponse>>(new Uri(OrdersPath, UriKind.Relative))
                ?? throw new InvalidOperationException("No list returned.");
            List<OrderSummaryResponse> theirs = await other.GetFromJsonAsync<List<OrderSummaryResponse>>(new Uri(OrdersPath, UriKind.Relative))
                ?? throw new InvalidOperationException("No list returned.");

            Assert.Equal(new[] { second.Reference, first.Reference }, mine.Select(o => o.Reference));
            Assert.Equal(new[] { othersOrder.Reference }, theirs.Select(o => o.Reference));
            Assert.All(mine, o => Assert.Equal(("PendingPayment", false, 1, 29.90m, "ZAR"), (o.Status, o.IsPaid, o.DrinkCount, o.Total, o.CurrencyCode)));
        }
    }

    [Fact]
    public async Task AnonymousAndManagers_CannotReadOrders()
    {
        (HttpClient patron, _, OrderResponse placed) = await PlaceOrderAsync();
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        using HttpClient anonymous = factory.CreateHttpsClient();
        using (patron)
        {
            foreach (string path in new[] { OrdersPath, $"{OrdersPath}/{placed.Reference}" })
            {
                using HttpResponseMessage asAnonymous = await anonymous.GetAsync(new Uri(path, UriKind.Relative));
                using HttpResponseMessage asManager = await manager.Client.GetAsync(new Uri(path, UriKind.Relative));
                Assert.Equal(HttpStatusCode.Unauthorized, asAnonymous.StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, asManager.StatusCode);
            }
        }
    }

    private async Task<(HttpClient Patron, TestMenu Menu, OrderResponse Order)> PlaceOrderAsync()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        HttpClient patron = await factory.CreatePatronClientAsync();
        return (patron, menu, await PlaceAsync(patron, menu));
    }

    /// <summary>Places one milkshake (12.00 + 6.00 + 8.00 = 26.00; VAT 3.90; total 29.90).</summary>
    private static async Task<OrderResponse> PlaceAsync(HttpClient patron, TestMenu menu)
    {
        OrderingOptionsResponse options = await patron.GetFromJsonAsync<OrderingOptionsResponse>(new Uri("/api/ordering/options", UriKind.Relative))
            ?? throw new InvalidOperationException("No options returned.");
        var body = new
        {
            restaurantId = menu.Restaurant.Id,
            pickupLocal = options.Pickup.LatestLocal,
            drinks = new[] { new { flavourId = menu.Flavour.Id, consistencyId = menu.Consistency.Id, toppingId = menu.Topping.Id } },
            expectedTotal = 29.90m,
            expectedCurrencyCode = "ZAR",
        };

        string token = await patron.GetCsrfTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(OrdersPath, UriKind.Relative)) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", token);
        request.Headers.Add(OrderMessages.IdempotencyKeyHeader, Guid.NewGuid().ToString());
        using HttpResponseMessage response = await patron.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<OrderResponse>();
    }
}
