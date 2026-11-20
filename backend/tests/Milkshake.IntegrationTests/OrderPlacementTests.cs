using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api.Contracts.Ordering;
using Milkshake.Domain.Orders;
using Milkshake.IntegrationTests.Infrastructure;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M7 POST /api/ordering/orders: server repricing, price-change conflict (O6), idempotent retries and concurrent
/// submissions (O5, O7), revalidation, strict requests and access control.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class OrderPlacementTests(PostgresApiFactory factory)
{
    private const string OrdersPath = "/api/ordering/orders";

    [Fact]
    public async Task PlacingAValidDraft_StoresAnUnpaidOrderWithTheServerPrice()
    {
        (HttpClient patron, TestMenu menu, object draft) = await ArrangeAsync(drinks: 2);
        using (patron)
        {
            using HttpResponseMessage response = await PlaceAsync(patron, draft, 59.80m, "ZAR", Guid.NewGuid());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            OrderResponse order = await response.ReadJsonAsync<OrderResponse>();
            Assert.True(OrderReference.IsValid(order.Reference));
            Assert.Equal($"/api/ordering/orders/{order.Reference}", response.Headers.Location?.OriginalString);
            Assert.Equal(("PendingPayment", false, true), (order.Status, order.IsPaid, order.AwaitsPayment));
            Assert.Equal(OrderResponse.AwaitingPaymentNote, order.Note);

            // 2 × (12.00 + 6.00 + 8.00) = 52.00; VAT 15% = 7.80; total 59.80.
            Assert.Equal((52.00m, 0m, 7.80m, 59.80m, "ZAR"),
                (order.Totals.Subtotal, order.Totals.Discount.Amount, order.Totals.Vat, order.Totals.Total, order.Totals.CurrencyCode));
            Assert.Equal(new[] { 1, 2 }, order.Drinks.Select(d => d.Number));
            Assert.All(order.Drinks, d => Assert.Equal((menu.Flavour.Name, 12.00m, 26.00m), (d.Flavour.Name, d.Flavour.Fee, d.Price)));
            Assert.Equal(menu.Restaurant.Name, order.Restaurant.Name);

            Assert.Equal(1, await CountOrdersAsync(order.Reference));
        }
    }

    [Fact]
    public async Task RetryingWithTheSameKeyAndContent_ReturnsTheSameOrder_WithoutCreatingAnother()
    {
        (HttpClient patron, _, object draft) = await ArrangeAsync(drinks: 1);
        using (patron)
        {
            var key = Guid.NewGuid();
            using HttpResponseMessage first = await PlaceAsync(patron, draft, 29.90m, "ZAR", key);
            using HttpResponseMessage retry = await PlaceAsync(patron, draft, 29.90m, "ZAR", key);

            Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK), (first.StatusCode, retry.StatusCode));
            OrderResponse original = await first.ReadJsonAsync<OrderResponse>();
            OrderResponse replayed = await retry.ReadJsonAsync<OrderResponse>();
            Assert.Equal(original.Reference, replayed.Reference);
            Assert.Equal(original.Totals, replayed.Totals);
            Assert.Equal(1, await CountOrdersForKeyAsync(key));
        }
    }

    [Fact]
    public async Task ReusingAKeyForDifferentContent_IsRefused()
    {
        (HttpClient patron, TestMenu menu, object draft) = await ArrangeAsync(drinks: 1);
        using (patron)
        {
            var key = Guid.NewGuid();
            using HttpResponseMessage first = await PlaceAsync(patron, draft, 29.90m, "ZAR", key);
            object twoDrinks = await DraftAsync(patron, menu, drinks: 2);
            using HttpResponseMessage reused = await PlaceAsync(patron, twoDrinks, 59.80m, "ZAR", key);

            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, reused.StatusCode);
            Assert.Equal("application/problem+json", reused.Content.Headers.ContentType?.MediaType);
            Assert.Equal(1, await CountOrdersForKeyAsync(key));
        }
    }

    [Fact]
    public async Task AChangedPrice_ReturnsTheNewTotals_StoresNothing_AndCanBeConfirmedWithTheSameKey()
    {
        (HttpClient patron, TestMenu menu, object draft) = await ArrangeAsync(drinks: 1);
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        using (patron)
        {
            // The patron reviewed 29.90; then the manager raises the flavour from 12.00 to 14.00 with immediate effect.
            using HttpResponseMessage repriced = await manager.Client.SendWithCsrfAsync(
                HttpMethod.Post, $"/api/manager/catalog/flavours/{menu.Flavour.Id}/prices",
                new { amount = 14.00m, effectiveFrom = (DateTimeOffset?)null, version = menu.Flavour.Version });
            Assert.Equal(HttpStatusCode.OK, repriced.StatusCode);

            var key = Guid.NewGuid();
            using HttpResponseMessage conflict = await PlaceAsync(patron, draft, 29.90m, "ZAR", key);

            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            using JsonDocument problem = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
            // 14.00 + 6.00 + 8.00 = 28.00; VAT 4.20; total 32.20.
            Assert.Equal(32.20m, problem.RootElement.GetProperty("currentTotals").GetProperty("total").GetDecimal());
            Assert.Equal(0, await CountOrdersForKeyAsync(key));

            using HttpResponseMessage confirmed = await PlaceAsync(patron, draft, 32.20m, "ZAR", key);
            Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
            Assert.Equal(32.20m, (await confirmed.ReadJsonAsync<OrderResponse>()).Totals.Total);
        }
    }

    [Fact]
    public async Task ADifferentCurrency_IsTreatedAsAChangedPrice()
    {
        (HttpClient patron, _, object draft) = await ArrangeAsync(drinks: 1);
        using (patron)
        {
            using HttpResponseMessage response = await PlaceAsync(patron, draft, 29.90m, "USD", Guid.NewGuid());
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
    }

    [Fact]
    public async Task SimultaneousSubmissionsWithOneKey_CreateExactlyOneOrder()
    {
        (HttpClient patron, _, object draft) = await ArrangeAsync(drinks: 1);
        using (patron)
        {
            var key = Guid.NewGuid();
            string json = JsonSerializer.Serialize(WithTotal(draft, 29.90m, "ZAR"));
            string token = await patron.GetCsrfTokenAsync(); // one token, so parallel requests share one anti-forgery cookie
            HttpResponseMessage[] responses = await Task.WhenAll(
                Enumerable.Range(0, 5).Select(_ => SendAsync(patron, json, key.ToString(), token)));
            try
            {
                Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK }));
                Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
                string[] references = await Task.WhenAll(responses.Select(async r => (await r.ReadJsonAsync<OrderResponse>()).Reference));
                Assert.Single(references.Distinct());
                Assert.Equal(1, await CountOrdersForKeyAsync(key));
            }
            finally
            {
                foreach (HttpResponseMessage response in responses)
                {
                    response.Dispose();
                }
            }
        }
    }

    [Fact]
    public async Task TheSameKeyFromAnotherPatron_IsASeparateOrder()
    {
        (HttpClient first, _, object draft) = await ArrangeAsync(drinks: 1);
        using HttpClient second = await factory.CreatePatronClientAsync();
        using (first)
        {
            var key = Guid.NewGuid();
            using HttpResponseMessage a = await PlaceAsync(first, draft, 29.90m, "ZAR", key);
            using HttpResponseMessage b = await PlaceAsync(second, draft, 29.90m, "ZAR", key);

            Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (a.StatusCode, b.StatusCode));
            Assert.NotEqual((await a.ReadJsonAsync<OrderResponse>()).Reference, (await b.ReadJsonAsync<OrderResponse>()).Reference);
            Assert.Equal(2, await CountOrdersForKeyAsync(key));
        }
    }

    [Fact]
    public async Task ADeactivatedChoice_IsRevalidated_AndNothingIsStored()
    {
        (HttpClient patron, TestMenu menu, object draft) = await ArrangeAsync(drinks: 1);
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        using (patron)
        {
            using HttpResponseMessage deactivated = await manager.Client.SendWithCsrfAsync(
                HttpMethod.Post, $"/api/manager/catalog/toppings/{menu.Topping.Id}/deactivate", new { version = menu.Topping.Version });
            Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);

            var key = Guid.NewGuid();
            using HttpResponseMessage response = await PlaceAsync(patron, draft, 29.90m, "ZAR", key);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("drinks[0].toppingId", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(0, await CountOrdersForKeyAsync(key));
        }
    }

    [Fact]
    public async Task APickupTimeNoLongerAllowed_IsRevalidated()
    {
        (HttpClient patron, TestMenu menu, _) = await ArrangeAsync(drinks: 1);
        using (patron)
        {
            var past = new { restaurantId = menu.Restaurant.Id, pickupLocal = "2020-01-01T10:00", drinks = new[] { Drink(menu) } };
            using HttpResponseMessage response = await PlaceAsync(patron, past, 29.90m, "ZAR", Guid.NewGuid());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("pickupLocal", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task AMissingOrInvalidIdempotencyKey_IsRefused(string? key)
    {
        (HttpClient patron, _, object draft) = await ArrangeAsync(drinks: 1);
        using (patron)
        {
            using HttpResponseMessage response = await SendAsync(patron, JsonSerializer.Serialize(WithTotal(draft, 29.90m, "ZAR")), key);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(OrderMessages.IdempotencyKeyHeader, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AMissingExpectedTotal_IsRefused()
    {
        (HttpClient patron, _, object draft) = await ArrangeAsync(drinks: 1);
        using (patron)
        {
            using HttpResponseMessage response = await SendAsync(patron, JsonSerializer.Serialize(draft), Guid.NewGuid().ToString());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("expectedTotal", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("total", "0.01")]
    [InlineData("subtotal", "0.01")]
    [InlineData("vatRate", "0")]
    [InlineData("discountRate", "1")]
    [InlineData("status", "\"Confirmed\"")]
    [InlineData("isPaid", "true")]
    [InlineData("patronId", "\"8d3f0d4e-1f1c-4a55-9c43-3c1f8a1b2c3d\"")]
    [InlineData("reference", "\"MS-0000-0000\"")]
    public async Task ForgedFields_AreRejected_AndNothingIsStored(string field, string value)
    {
        (HttpClient patron, _, object draft) = await ArrangeAsync(drinks: 1);
        using (patron)
        {
            string json = JsonSerializer.Serialize(WithTotal(draft, 29.90m, "ZAR"));
            string forged = json[..^1] + $",\"{field}\":{value}}}";
            var key = Guid.NewGuid();

            using HttpResponseMessage response = await SendAsync(patron, forged, key.ToString());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(0, await CountOrdersForKeyAsync(key));
        }
    }

    [Fact]
    public async Task AnonymousManagerAndMissingCsrf_AreRefused()
    {
        (HttpClient patron, _, object draft) = await ArrangeAsync(drinks: 1);
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        using HttpClient anonymous = factory.CreateHttpsClient();
        using (patron)
        {
            string json = JsonSerializer.Serialize(WithTotal(draft, 29.90m, "ZAR"));

            using HttpResponseMessage asAnonymous = await SendAsync(anonymous, json, Guid.NewGuid().ToString());
            using HttpResponseMessage asManager = await SendAsync(manager.Client, json, Guid.NewGuid().ToString());
            using var noCsrf = new HttpRequestMessage(HttpMethod.Post, new Uri(OrdersPath, UriKind.Relative))
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            };
            noCsrf.Headers.Add(OrderMessages.IdempotencyKeyHeader, Guid.NewGuid().ToString());
            using HttpResponseMessage withoutCsrf = await patron.SendAsync(noCsrf);

            Assert.Equal(HttpStatusCode.Unauthorized, asAnonymous.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, asManager.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, withoutCsrf.StatusCode);
        }
    }

    [Fact]
    public async Task AnOversizedBody_IsRefused()
    {
        (HttpClient patron, _, _) = await ArrangeAsync(drinks: 1);
        using (patron)
        {
            string json = $"{{\"restaurantId\":1,\"pickupLocal\":\"{new string('x', 20_000)}\"}}";
            using HttpResponseMessage response = await SendAsync(patron, json, Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        }
    }

    private async Task<(HttpClient Patron, TestMenu Menu, object Draft)> ArrangeAsync(int drinks)
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        HttpClient patron = await factory.CreatePatronClientAsync();
        return (patron, menu, await DraftAsync(patron, menu, drinks));
    }

    private static async Task<object> DraftAsync(HttpClient patron, TestMenu menu, int drinks)
    {
        OrderingOptionsResponse options = await patron.GetFromJsonAsync<OrderingOptionsResponse>(new Uri("/api/ordering/options", UriKind.Relative))
            ?? throw new InvalidOperationException("No options returned.");
        return new { restaurantId = menu.Restaurant.Id, pickupLocal = options.Pickup.LatestLocal, drinks = Enumerable.Repeat(Drink(menu), drinks).ToArray() };
    }

    private static object Drink(TestMenu menu) => new { flavourId = menu.Flavour.Id, consistencyId = menu.Consistency.Id, toppingId = menu.Topping.Id };

    private static Dictionary<string, object?> WithTotal(object draft, decimal total, string currency)
    {
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(draft))!;
        body["expectedTotal"] = total;
        body["expectedCurrencyCode"] = currency;
        return body;
    }

    private static Task<HttpResponseMessage> PlaceAsync(HttpClient client, object draft, decimal total, string currency, Guid key) =>
        SendAsync(client, JsonSerializer.Serialize(WithTotal(draft, total, currency)), key.ToString());

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string json, string? idempotencyKey, string? csrfToken = null)
    {
        string token = csrfToken ?? await client.GetCsrfTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(OrdersPath, UriKind.Relative))
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation(OrderMessages.IdempotencyKeyHeader, idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    private async Task<int> CountOrdersAsync(string reference)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>().Orders.CountAsync(o => o.Reference == reference);
    }

    private async Task<int> CountOrdersForKeyAsync(Guid key)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>().Orders.CountAsync(o => o.IdempotencyKey == key);
    }
}
