using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api.Contracts.Management;
using Milkshake.Api.Contracts.Ordering;
using Milkshake.Application.Management;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Settings;
using Milkshake.Infrastructure.Persistence;
using Milkshake.IntegrationTests.Infrastructure;
using static Milkshake.IntegrationTests.Infrastructure.OrderingTestHelpers;

namespace Milkshake.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class OrderDraftValidationTests(PostgresApiFactory factory)
{
    private sealed record Menu(CatalogItemResponse Flavour, CatalogItemResponse Consistency, CatalogItemResponse Topping, RestaurantResponse Restaurant)
    {
        public object Drink() => new { flavourId = Flavour.Id, consistencyId = Consistency.Id, toppingId = Topping.Id };
    }

    [Fact]
    public async Task ValidDraft_ReturnsTheResolvedSummary_AndStoresNothing()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        Menu menu = await CreateMenuAsync(manager);
        using HttpClient patron = await factory.CreatePatronClientAsync();
        OrderingOptionsResponse options = await LoadOptionsAsync(patron);
        string pickupLocal = options.Pickup.LatestLocal;   // the latest time only moves later, so it stays valid
        int auditRowsBefore = await CountAuditRowsAsync();

        using HttpResponseMessage response = await patron.SendWithCsrfAsync(
            HttpMethod.Post, ValidatePath, new { restaurantId = menu.Restaurant.Id, pickupLocal, drinks = new[] { menu.Drink(), menu.Drink() } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        DraftSummaryResponse summary = await response.ReadJsonAsync<DraftSummaryResponse>();
        Assert.True(summary.IsDraft);
        Assert.Equal(DraftSummaryResponse.DraftNote, summary.Note);
        Assert.Equal(menu.Restaurant.Id, summary.Restaurant.Id);
        Assert.Equal((pickupLocal, options.Pickup.LatestUtc, "Africa/Johannesburg"), (summary.Pickup.Local, summary.Pickup.Utc, summary.Pickup.TimeZone));
        Assert.Equal([1, 2], summary.Drinks.Select(d => d.Number));
        DraftDrinkResponse first = summary.Drinks[0];
        Assert.Equal((menu.Flavour.Name, 12.00m), (first.Flavour.Name, first.Flavour.Fee.Amount));
        Assert.Equal(auditRowsBefore, await CountAuditRowsAsync());   // nothing written
    }

    [Fact]
    public async Task Validate_WhenAnonymous_Returns401()
    {
        using HttpClient client = factory.CreateHttpsClient();

        using HttpResponseMessage response = await client.SendWithCsrfAsync(HttpMethod.Post, ValidatePath, new { restaurantId = 1, drinks = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Validate_WithoutAntiForgeryToken_Returns400()
    {
        using HttpClient patron = await factory.CreatePatronClientAsync();

        using HttpResponseMessage response = await patron.PostAsJsonAsync(new Uri(ValidatePath, UriKind.Relative), new { restaurantId = 1, drinks = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DrinkCount_IsCheckedAgainstTheLimitsInForceAtValidation()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        Menu menu = await CreateMenuAsync(manager);
        using HttpClient patron = await factory.CreatePatronClientAsync();

        // The patron loaded max = 10; a manager lowers it to 2 before the draft is checked.
        await PutSettingAsync(manager, SettingKeys.MaxDrinksPerOrder, "2");
        try
        {
            using HttpResponseMessage tooMany = await patron.SendWithCsrfAsync(
                HttpMethod.Post, ValidatePath, new { restaurantId = menu.Restaurant.Id, drinks = new[] { menu.Drink(), menu.Drink(), menu.Drink() } });
            using HttpResponseMessage none = await patron.SendWithCsrfAsync(
                HttpMethod.Post, ValidatePath, new { restaurantId = menu.Restaurant.Id, drinks = Array.Empty<object>() });

            ValidationErrorBody tooManyBody = await AssertValidationErrorAsync(tooMany, OrderDraftFields.Drinks);
            Assert.Equal(OrderDraftMessages.Quantity(1, 2), Assert.Single(tooManyBody.Errors[OrderDraftFields.Drinks]));
            await AssertValidationErrorAsync(none, OrderDraftFields.Drinks);
        }
        finally
        {
            await PutSettingAsync(manager, SettingKeys.MaxDrinksPerOrder, "10");
        }
    }

    [Fact]
    public async Task MissingChoices_AreReportedPerDrinkAndField()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        Menu menu = await CreateMenuAsync(manager);
        using HttpClient patron = await factory.CreatePatronClientAsync();

        using HttpResponseMessage response = await patron.SendWithCsrfAsync(HttpMethod.Post, ValidatePath, new
        {
            drinks = new object[]
            {
                new { flavourId = menu.Flavour.Id },
                new { consistencyId = menu.Consistency.Id, toppingId = menu.Topping.Id },
            },
        });

        ValidationErrorBody body = await AssertValidationErrorAsync(response, OrderDraftFields.RestaurantId);
        Assert.Contains(OrderDraftFields.PickupLocal, body.Errors.Keys);
        Assert.Contains(OrderDraftFields.Consistency(0), body.Errors.Keys);
        Assert.Contains(OrderDraftFields.Topping(0), body.Errors.Keys);
        Assert.Contains(OrderDraftFields.Flavour(1), body.Errors.Keys);
    }

    [Fact]
    public async Task FlavourDeactivatedAfterLoading_IsRejectedAsNoLongerAvailable()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        Menu menu = await CreateMenuAsync(manager);
        using HttpClient patron = await factory.CreatePatronClientAsync();
        OrderingOptionsResponse loaded = await LoadOptionsAsync(patron);
        Assert.Contains(loaded.Flavours, f => f.Id == menu.Flavour.Id);

        using HttpResponseMessage deactivate = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, $"/api/manager/catalog/flavours/{menu.Flavour.Id}/deactivate", new { version = menu.Flavour.Version });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        using HttpResponseMessage response = await patron.SendWithCsrfAsync(
            HttpMethod.Post, ValidatePath, new { restaurantId = menu.Restaurant.Id, pickupLocal = loaded.Pickup.LatestLocal, drinks = new[] { menu.Drink() } });

        ValidationErrorBody body = await AssertValidationErrorAsync(response, OrderDraftFields.Flavour(0));
        Assert.Equal(OrderDraftMessages.NoLongerAvailable("flavour", 1), Assert.Single(body.Errors[OrderDraftFields.Flavour(0)]));
    }

    [Fact]
    public async Task TamperedIds_AreRejected()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        Menu menu = await CreateMenuAsync(manager);
        using HttpClient patron = await factory.CreatePatronClientAsync();

        // A restaurant id used as a topping id, a negative id and one that does not exist.
        using HttpResponseMessage response = await patron.SendWithCsrfAsync(HttpMethod.Post, ValidatePath, new
        {
            restaurantId = 999_999,
            drinks = new[] { new { flavourId = -1, consistencyId = menu.Consistency.Id, toppingId = menu.Restaurant.Id } },
        });

        ValidationErrorBody body = await AssertValidationErrorAsync(response, OrderDraftFields.RestaurantId);
        Assert.Contains(OrderDraftFields.Flavour(0), body.Errors.Keys);
        Assert.Contains(OrderDraftFields.Topping(0), body.Errors.Keys);
    }

    [Fact]
    public async Task InactiveRestaurant_IsRejected()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        Menu menu = await CreateMenuAsync(manager);
        using HttpResponseMessage deactivate = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, $"/api/manager/restaurants/{menu.Restaurant.Id}/deactivate", new { version = menu.Restaurant.Version });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        using HttpClient patron = await factory.CreatePatronClientAsync();

        using HttpResponseMessage response = await patron.SendWithCsrfAsync(
            HttpMethod.Post, ValidatePath, new { restaurantId = menu.Restaurant.Id, drinks = new[] { menu.Drink() } });

        ValidationErrorBody body = await AssertValidationErrorAsync(response, OrderDraftFields.RestaurantId);
        Assert.Equal(OrderDraftMessages.RestaurantUnavailable, Assert.Single(body.Errors[OrderDraftFields.RestaurantId]));
    }

    [Fact]
    public async Task ABrowserSuppliedPrice_IsRejectedNotIgnored()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        Menu menu = await CreateMenuAsync(manager);
        using HttpClient patron = await factory.CreatePatronClientAsync();
        string json = string.Create(CultureInfo.InvariantCulture,
            $$"""{"restaurantId":{{menu.Restaurant.Id}},"drinks":[{"flavourId":{{menu.Flavour.Id}},"consistencyId":{{menu.Consistency.Id}},"toppingId":{{menu.Topping.Id}},"price":0.01}]}""");

        using HttpResponseMessage response = await patron.PostRawJsonWithCsrfAsync(ValidatePath, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty((await response.ReadJsonAsync<ValidationErrorBody>()).Errors);
    }

    [Theory]
    [InlineData("""{"restaurantId":"abc","drinks":[]}""")]                    // wrong type
    [InlineData("""{"restaurantId":1,"drinks":[""")]                          // truncated JSON
    [InlineData("""{"restaurantId":1,"restaurantId":2,"drinks":[]}""")]       // duplicate property
    public async Task MalformedRequests_AreRejected(string json)
    {
        using HttpClient patron = await factory.CreatePatronClientAsync();

        using HttpResponseMessage response = await patron.PostRawJsonWithCsrfAsync(ValidatePath, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PickupTime_InThePastOrBeyondTheAdvanceLimit_IsRejected()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        Menu menu = await CreateMenuAsync(manager);
        using HttpClient patron = await factory.CreatePatronClientAsync();
        OrderingOptionsResponse options = await LoadOptionsAsync(patron);
        string anHourAgo = ShiftLocal(options.Pickup.EarliestLocal, TimeSpan.FromMinutes(-(options.Pickup.LeadTimeMinutes + 60)));
        string twoDaysTooFar = ShiftLocal(options.Pickup.LatestLocal, TimeSpan.FromDays(2));

        using HttpResponseMessage past = await patron.SendWithCsrfAsync(
            HttpMethod.Post, ValidatePath, new { restaurantId = menu.Restaurant.Id, pickupLocal = anHourAgo, drinks = new[] { menu.Drink() } });
        using HttpResponseMessage tooFar = await patron.SendWithCsrfAsync(
            HttpMethod.Post, ValidatePath, new { restaurantId = menu.Restaurant.Id, pickupLocal = twoDaysTooFar, drinks = new[] { menu.Drink() } });

        ValidationErrorBody pastBody = await AssertValidationErrorAsync(past, OrderDraftFields.PickupLocal);
        KeyValuePair<string, string[]> pastError = Assert.Single(pastBody.Errors);   // everything else is valid
        Assert.Equal(PickupMessages.InThePast, Assert.Single(pastError.Value));

        ValidationErrorBody tooFarBody = await AssertValidationErrorAsync(tooFar, OrderDraftFields.PickupLocal);
        DateTime latestLocal = DateTime.ParseExact(options.Pickup.LatestLocal, PickupRules.LocalFormat, CultureInfo.InvariantCulture);
        Assert.Equal(
            PickupMessages.TooFarAhead(options.Pickup.MaxAdvanceDays, latestLocal),
            Assert.Single(tooFarBody.Errors[OrderDraftFields.PickupLocal]));
    }

    [Theory]
    [InlineData("2026-11-20 14:30")]
    [InlineData("2026-11-20T14:30:00Z")]
    [InlineData("tomorrow at two")]
    public async Task PickupTime_InAnyOtherFormat_IsRejected(string pickupLocal)
    {
        using HttpClient patron = await factory.CreatePatronClientAsync();

        using HttpResponseMessage response = await patron.SendWithCsrfAsync(
            HttpMethod.Post, ValidatePath, new { restaurantId = (int?)null, pickupLocal, drinks = Array.Empty<object>() });

        ValidationErrorBody body = await AssertValidationErrorAsync(response, OrderDraftFields.PickupLocal);
        Assert.Equal(PickupMessages.Format, Assert.Single(body.Errors[OrderDraftFields.PickupLocal]));
    }

    [Fact]
    public async Task LeadTime_IsCheckedAgainstTheSettingInForceAtValidation()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        Menu menu = await CreateMenuAsync(manager);
        using HttpClient patron = await factory.CreatePatronClientAsync();
        OrderingOptionsResponse loaded = await LoadOptionsAsync(patron);
        int original = loaded.Pickup.LeadTimeMinutes;
        int raised = original + 60;
        Assert.InRange(raised, 1, SettingRules.MaxLeadTimeMinutes);

        // The patron was offered the earliest time; a manager then lengthens the lead time.
        await PutSettingAsync(manager, SettingKeys.MinPickupLeadTimeMinutes, raised.ToString(CultureInfo.InvariantCulture));
        try
        {
            using HttpResponseMessage response = await patron.SendWithCsrfAsync(
                HttpMethod.Post, ValidatePath, new { restaurantId = menu.Restaurant.Id, pickupLocal = loaded.Pickup.EarliestLocal, drinks = new[] { menu.Drink() } });

            ValidationErrorBody body = await AssertValidationErrorAsync(response, OrderDraftFields.PickupLocal);
            Assert.Contains($"{raised.ToString(CultureInfo.InvariantCulture)} minutes", Assert.Single(body.Errors[OrderDraftFields.PickupLocal]), StringComparison.Ordinal);
        }
        finally
        {
            await PutSettingAsync(manager, SettingKeys.MinPickupLeadTimeMinutes, original.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static async Task<OrderingOptionsResponse> LoadOptionsAsync(HttpClient patron) =>
        await patron.GetFromJsonAsync<OrderingOptionsResponse>(new Uri("/api/ordering/options", UriKind.Relative))
            ?? throw new InvalidOperationException("No options returned.");

    private static string ShiftLocal(string local, TimeSpan by) =>
        DateTime.ParseExact(local, PickupRules.LocalFormat, CultureInfo.InvariantCulture)
            .Add(by)
            .ToString(PickupRules.LocalFormat, CultureInfo.InvariantCulture);

    private async Task<Menu> CreateMenuAsync(ManagerSession manager) =>
        new(
            await CreateItemAsync(manager, "flavours", 12.00m),
            await CreateItemAsync(manager, "consistencies", 6.00m),
            await CreateItemAsync(manager, "toppings", 8.00m),
            await CreateRestaurantAsync(manager));

    private static async Task<CatalogItemResponse> CreateItemAsync(ManagerSession manager, string kind, decimal price)
    {
        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, $"/api/manager/catalog/{kind}", new { name = $"Draft {Guid.NewGuid():N}", displayOrder = 95, initialPrice = price });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<CatalogItemResponse>();
    }

    private static async Task<RestaurantResponse> CreateRestaurantAsync(ManagerSession manager)
    {
        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, "/api/manager/restaurants", new { name = $"Draft Branch {Guid.NewGuid():N}", address = (string?)null });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<RestaurantResponse>();
    }

    private static async Task PutSettingAsync(ManagerSession manager, string key, string value)
    {
        List<SettingResponse> settings = await manager.Client.GetFromJsonAsync<List<SettingResponse>>(new Uri("/api/manager/settings", UriKind.Relative))
            ?? throw new InvalidOperationException("No settings returned.");
        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Put, $"/api/manager/settings/{key}", new { value, version = settings.Single(s => s.Key == key).Version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<int> CountAuditRowsAsync()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>().AuditLogs.CountAsync();
    }

    private static async Task<ValidationErrorBody> AssertValidationErrorAsync(HttpResponseMessage response, string expectedKey)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ValidationErrorBody body = await response.ReadJsonAsync<ValidationErrorBody>();
        Assert.Contains(expectedKey, body.Errors.Keys);
        return body;
    }
}