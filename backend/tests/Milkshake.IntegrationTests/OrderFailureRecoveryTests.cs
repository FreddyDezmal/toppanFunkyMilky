using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api.Contracts.Ordering;
using Milkshake.IntegrationTests.Infrastructure;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M7 failure recovery through the real placement path (AC-7.1): a database failure while the order's rows are being
/// written leaves no order, no items and no audit rows, reveals nothing internal to the patron, and does not use up
/// the idempotency key, so the same submission can be retried safely.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class OrderFailureRecoveryTests(PostgresApiFactory factory)
{
    private const string OrdersPath = "/api/ordering/orders";

    [Fact]
    public async Task AFailureWhileSavingTheItems_RollsBackEverything_AndTheSameKeyCanBeRetried()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        using HttpClient patron = await factory.CreatePatronClientAsync();
        object body = await BodyAsync(patron, menu);
        var key = Guid.NewGuid();
        string trigger = "test_fail_items_" + menu.Flavour.Id.ToString(CultureInfo.InvariantCulture);
        int auditRowsBefore = await CountAsync(db => db.AuditLogs.CountAsync(a => a.TableName == "orders" || a.TableName == "order_items"));

        // The order row is inserted first; the trigger then rejects its item, so the failure happens mid-transaction.
        await ExecuteAsync(
            "CREATE FUNCTION milkshake." + trigger + "() RETURNS trigger LANGUAGE plpgsql AS $$ " +
            "BEGIN RAISE EXCEPTION 'forced test failure in order_items'; END $$; " +
            "CREATE TRIGGER " + trigger + " BEFORE INSERT ON milkshake.order_items FOR EACH ROW " +
            "WHEN (NEW.flavour_id = " + menu.Flavour.Id.ToString(CultureInfo.InvariantCulture) + ") " +
            "EXECUTE FUNCTION milkshake." + trigger + "();");
        try
        {
            using HttpResponseMessage failed = await PlaceAsync(patron, body, key);

            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.Equal("application/problem+json", failed.Content.Headers.ContentType?.MediaType);
            string problem = await failed.Content.ReadAsStringAsync();
            foreach (string secret in new[] { "forced test failure", "order_items", "milkshake.", "Npgsql", "Exception", "SELECT", "INSERT" })
            {
                Assert.DoesNotContain(secret, problem, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            await ExecuteAsync(
                "DROP TRIGGER IF EXISTS " + trigger + " ON milkshake.order_items; " +
                "DROP FUNCTION IF EXISTS milkshake." + trigger + "();");
        }

        Assert.Equal(0, await CountAsync(db => db.Orders.CountAsync(o => o.IdempotencyKey == key)));
        Assert.Equal(0, await CountAsync(db => db.OrderItems.CountAsync(i => i.FlavourId == menu.Flavour.Id)));
        Assert.Equal(auditRowsBefore, await CountAsync(db => db.AuditLogs.CountAsync(a => a.TableName == "orders" || a.TableName == "order_items")));

        // The key was not used up by the failed attempt: the patron's retry places the order once.
        using HttpResponseMessage retried = await PlaceAsync(patron, body, key);
        using HttpResponseMessage retriedAgain = await PlaceAsync(patron, body, key);

        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.OK), (retried.StatusCode, retriedAgain.StatusCode));
        Assert.Equal(
            (await retried.ReadJsonAsync<OrderResponse>()).Reference,
            (await retriedAgain.ReadJsonAsync<OrderResponse>()).Reference);
        Assert.Equal(1, await CountAsync(db => db.Orders.CountAsync(o => o.IdempotencyKey == key)));
    }

    private static async Task<object> BodyAsync(HttpClient patron, TestMenu menu)
    {
        OrderingOptionsResponse options = await patron.GetFromJsonAsync<OrderingOptionsResponse>(new Uri("/api/ordering/options", UriKind.Relative))
            ?? throw new InvalidOperationException("No options returned.");

        // 12.00 + 6.00 + 8.00 = 26.00; VAT 3.90; total 29.90.
        return new
        {
            restaurantId = menu.Restaurant.Id,
            pickupLocal = options.Pickup.LatestLocal,
            drinks = new[] { new { flavourId = menu.Flavour.Id, consistencyId = menu.Consistency.Id, toppingId = menu.Topping.Id } },
            expectedTotal = 29.90m,
            expectedCurrencyCode = "ZAR",
        };
    }

    private static async Task<HttpResponseMessage> PlaceAsync(HttpClient patron, object body, Guid key)
    {
        string token = await patron.GetCsrfTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(OrdersPath, UriKind.Relative)) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", token);
        request.Headers.Add(OrderMessages.IdempotencyKeyHeader, key.ToString());
        return await patron.SendAsync(request);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>().Database.ExecuteSqlRawAsync(sql);
    }

    private async Task<int> CountAsync(Func<MilkshakeDbContext, Task<int>> query)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>());
    }
}
