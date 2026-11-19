using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api.Contracts.Ordering;
using Milkshake.Application.Ordering;
using Milkshake.Infrastructure.Persistence;
using Milkshake.IntegrationTests.Infrastructure;
using static Milkshake.IntegrationTests.Infrastructure.OrderingTestHelpers;

namespace Milkshake.IntegrationTests;

/// <summary>M6 Step 7: the live quote endpoint and the estimated totals on draft validation.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class OrderQuoteTests(PostgresApiFactory factory)
{
    private const string QuotePath = "/api/ordering/quote";

    private static object Drink(TestMenu menu) =>
        new { flavourId = menu.Flavour.Id, consistencyId = menu.Consistency.Id, toppingId = menu.Topping.Id };

    [Fact]
    public async Task Quote_PricesEachCompleteDrink_AndTotalsOnlyOnceAllAreComplete()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        using HttpClient patron = await factory.CreatePatronClientAsync();

        using HttpResponseMessage partial = await patron.SendWithCsrfAsync(
            HttpMethod.Post, QuotePath, new { drinks = new object[] { Drink(menu), new { flavourId = menu.Flavour.Id } } });
        using HttpResponseMessage complete = await patron.SendWithCsrfAsync(
            HttpMethod.Post, QuotePath, new { drinks = new[] { Drink(menu), Drink(menu) } });

        Assert.Equal(HttpStatusCode.OK, partial.StatusCode);
        QuoteResponse partialQuote = await partial.ReadJsonAsync<QuoteResponse>();
        Assert.True(partialQuote.IsEstimate);
        Assert.Equal(EstimateNotes.Estimate, partialQuote.Note);
        Assert.Equal([(1, (decimal?)26.00m), (2, null)], partialQuote.Drinks.Select(d => (d.Number, d.Price)));
        Assert.Null(partialQuote.Totals);

        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        OrderTotalsResponse totals = (await complete.ReadJsonAsync<QuoteResponse>()).Totals
            ?? throw new InvalidOperationException("Expected totals once every drink is complete.");
        // 2 × 26.00 = 52.00; no discount; VAT 15% = 7.80; total 59.80.
        Assert.Equal(
            ("ZAR", 52.00m, 0.00m, 52.00m, 0.15m, 7.80m, 59.80m),
            (totals.CurrencyCode, totals.Subtotal, totals.Discount.Amount, totals.Taxable, totals.VatRate, totals.Vat, totals.Total));
        Assert.Null(totals.Discount.TierNumber);
    }

    [Fact]
    public async Task Quote_ReflectsAManagersPriceChange_Immediately()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        using HttpClient patron = await factory.CreatePatronClientAsync();
        object body = new { drinks = new[] { Drink(menu) } };

        using HttpResponseMessage before = await patron.SendWithCsrfAsync(HttpMethod.Post, QuotePath, body);
        using HttpResponseMessage change = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post,
            $"/api/manager/catalog/flavours/{menu.Flavour.Id}/prices",
            new { amount = 15.00m, effectiveFrom = (DateTimeOffset?)null, version = menu.Flavour.Version });
        using HttpResponseMessage after = await patron.SendWithCsrfAsync(HttpMethod.Post, QuotePath, body);

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Equal(26.00m, Assert.Single((await before.ReadJsonAsync<QuoteResponse>()).Drinks).Price);
        Assert.Equal(29.00m, Assert.Single((await after.ReadJsonAsync<QuoteResponse>()).Drinks).Price);   // 15 + 6 + 8
    }

    [Fact]
    public async Task Quote_ADeactivatedChoice_IsRejectedAsNoLongerAvailable()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        using HttpResponseMessage deactivate = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, $"/api/manager/catalog/toppings/{menu.Topping.Id}/deactivate", new { version = menu.Topping.Version });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        using HttpClient patron = await factory.CreatePatronClientAsync();

        using HttpResponseMessage response = await patron.SendWithCsrfAsync(HttpMethod.Post, QuotePath, new { drinks = new[] { Drink(menu) } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ValidationErrorBody body = await response.ReadJsonAsync<ValidationErrorBody>();
        Assert.Equal(OrderDraftMessages.NoLongerAvailable("topping", 1), Assert.Single(body.Errors[OrderDraftFields.Topping(0)]));
    }

    [Fact]
    public async Task Quote_MoreDrinksThanTheMaximum_IsRejected()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        using HttpClient patron = await factory.CreatePatronClientAsync();

        using HttpResponseMessage response = await patron.SendWithCsrfAsync(
            HttpMethod.Post, QuotePath, new { drinks = Enumerable.Repeat(Drink(menu), 11).ToArray() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(OrderDraftFields.Drinks, (await response.ReadJsonAsync<ValidationErrorBody>()).Errors.Keys);
    }

    [Theory]
    [InlineData(QuotePath, """{"drinks":[{"flavourId":1,"consistencyId":1,"toppingId":1,"price":0.01}]}""")]
    [InlineData(QuotePath, """{"drinks":[],"vatRate":0}""")]
    [InlineData(QuotePath, """{"drinks":[],"discountRate":0.5}""")]
    [InlineData(QuotePath, """{"drinks":[],"previousOrders":99}""")]
    [InlineData(ValidatePath, """{"restaurantId":1,"drinks":[],"vatRate":0}""")]
    [InlineData(ValidatePath, """{"restaurantId":1,"drinks":[],"total":0.01}""")]
    public async Task ForgedPricingFields_AreRejectedBeforeAnythingIsPriced(string path, string json)
    {
        using HttpClient patron = await factory.CreatePatronClientAsync();

        using HttpResponseMessage response = await patron.PostRawJsonWithCsrfAsync(path, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // Rejected while reading the request: the drinks were never checked (an empty list would have been).
        Assert.DoesNotContain(OrderDraftFields.Drinks, (await response.ReadJsonAsync<ValidationErrorBody>()).Errors.Keys);
    }

    [Fact]
    public async Task Quote_WhenAnonymous_Returns401()
    {
        using HttpClient client = factory.CreateHttpsClient();

        using HttpResponseMessage response = await client.SendWithCsrfAsync(HttpMethod.Post, QuotePath, new { drinks = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Quote_ForAManagerOnlyAccount_Returns403()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(HttpMethod.Post, QuotePath, new { drinks = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Quote_WithoutAntiForgeryToken_Returns400()
    {
        using HttpClient patron = await factory.CreatePatronClientAsync();

        using HttpResponseMessage response = await patron.PostAsJsonAsync(new Uri(QuotePath, UriKind.Relative), new { drinks = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Quote_AnOversizedBody_Returns413()
    {
        using HttpClient patron = await factory.CreatePatronClientAsync();
        string json = """{"drinks":[""" + string.Join(",", Enumerable.Repeat("""{"flavourId":2147483647,"consistencyId":2147483647,"toppingId":2147483647}""", 400)) + "]}";
        Assert.True(Encoding.UTF8.GetByteCount(json) > Milkshake.Api.Controllers.OrderingController.MaxDraftBodyBytes);

        using HttpResponseMessage response = await patron.PostRawJsonWithCsrfAsync(QuotePath, json);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Quote_WritesNothing()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        using HttpClient patron = await factory.CreatePatronClientAsync();
        int auditRowsBefore = await CountAuditRowsAsync();

        using HttpResponseMessage response = await patron.SendWithCsrfAsync(HttpMethod.Post, QuotePath, new { drinks = new[] { Drink(menu), Drink(menu) } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(auditRowsBefore, await CountAuditRowsAsync());
    }

    [Fact]
    public async Task Validate_ReturnsTheDraftWithEstimatedTotals()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        using HttpClient patron = await factory.CreatePatronClientAsync();
        OrderingOptionsResponse options = await patron.GetFromJsonAsync<OrderingOptionsResponse>(new Uri("/api/ordering/options", UriKind.Relative))
            ?? throw new InvalidOperationException("No options returned.");
        DateTimeOffset requestedAt = DateTimeOffset.UtcNow;

        using HttpResponseMessage response = await patron.SendWithCsrfAsync(
            HttpMethod.Post,
            ValidatePath,
            new { restaurantId = menu.Restaurant.Id, pickupLocal = options.Pickup.LatestLocal, drinks = new[] { Drink(menu), Drink(menu) } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        DraftSummaryResponse summary = await response.ReadJsonAsync<DraftSummaryResponse>();
        Assert.True(summary.IsEstimate);
        Assert.Equal(DraftSummaryResponse.DraftNote, summary.Note);
        Assert.Equal([26.00m, 26.00m], summary.Drinks.Select(d => d.Price));
        Assert.Equal((52.00m, 0.00m, 7.80m, 59.80m), (summary.Totals.Subtotal, summary.Totals.Discount.Amount, summary.Totals.Vat, summary.Totals.Total));
        Assert.InRange(summary.PricedAtUtc, requestedAt.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }

    private async Task<int> CountAuditRowsAsync()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>().AuditLogs.CountAsync();
    }
}