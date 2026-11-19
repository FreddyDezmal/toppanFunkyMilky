using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api.Contracts.Management;
using Milkshake.Api.Contracts.Ordering;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Catalog;
using Milkshake.Domain.Settings;
using Milkshake.Infrastructure.Persistence;
using Milkshake.IntegrationTests.Infrastructure;

namespace Milkshake.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class OrderingOptionsTests(PostgresApiFactory factory)
{
    private const string OptionsPath = "/api/ordering/options";

    [Fact]
    public async Task Options_WhenAnonymous_Returns401()
    {
        using HttpClient client = factory.CreateHttpsClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(OptionsPath, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Options_ForAManagerOnlyAccount_Returns403()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        using HttpResponseMessage response = await manager.Client.GetAsync(new Uri(OptionsPath, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Options_ListActiveFlavoursWithCurrentFees_AndHideInactiveOnesAndInternals()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        CatalogItemResponse active = await CreateItemAsync(manager, "flavours", 12.50m);
        CatalogItemResponse hidden = await CreateItemAsync(manager, "flavours", 9.00m);
        using HttpResponseMessage deactivate = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, $"/api/manager/catalog/flavours/{hidden.Id}/deactivate", new { version = hidden.Version });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        using HttpClient patron = await factory.CreatePatronClientAsync();

        using HttpResponseMessage response = await patron.GetAsync(new Uri(OptionsPath, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"version\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"isActive\"", json, StringComparison.Ordinal);
        OrderingOptionsResponse options = JsonSerializer.Deserialize<OrderingOptionsResponse>(json, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("No options returned.");
        OrderingOptionResponse listed = Assert.Single(options.Flavours, f => f.Id == active.Id);
        Assert.Equal(new FeeResponse(12.50m, "ZAR"), listed.Fee);
        Assert.DoesNotContain(options.Flavours, f => f.Id == hidden.Id);
    }

    [Fact]
    public async Task Options_ExcludeActiveItemsWithoutACurrentPrice()
    {
        int unpricedId;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
            var topping = new Topping($"Unpriced {Guid.NewGuid():N}", 1);   // active, but no price
            db.Toppings.Add(topping);
            await db.SaveChangesAsync();
            unpricedId = topping.Id;
        }

        using HttpClient patron = await factory.CreatePatronClientAsync();
        OrderingOptionsResponse options = await GetOptionsAsync(patron);

        Assert.DoesNotContain(options.Toppings, t => t.Id == unpricedId);
    }

    [Fact]
    public async Task Options_IncludeOnlyActiveRestaurants()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        RestaurantResponse open = await CreateRestaurantAsync(manager);
        RestaurantResponse closed = await CreateRestaurantAsync(manager);
        using HttpResponseMessage deactivate = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, $"/api/manager/restaurants/{closed.Id}/deactivate", new { version = closed.Version });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        using HttpClient patron = await factory.CreatePatronClientAsync();
        OrderingOptionsResponse options = await GetOptionsAsync(patron);

        Assert.Contains(options.Restaurants, r => r.Id == open.Id && r.Address == "1 Order Street");
        Assert.DoesNotContain(options.Restaurants, r => r.Id == closed.Id);
    }

    [Fact]
    public async Task DrinkLimits_FollowTheCurrentSettings()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        using HttpClient patron = await factory.CreatePatronClientAsync();

        await PutSettingAsync(manager, SettingKeys.MaxDrinksPerOrder, "12");
        try
        {
            OrderingOptionsResponse options = await GetOptionsAsync(patron);
            Assert.Equal(12, options.DrinkLimits.Max);
            Assert.Equal(1, options.DrinkLimits.Min);
        }
        finally
        {
            await PutSettingAsync(manager, SettingKeys.MaxDrinksPerOrder, "10");
        }
    }

    [Fact]
    public async Task PickupWindow_UsesTheBusinessTimeZone_NotTheCaller()
    {
        using HttpClient patron = await factory.CreatePatronClientAsync();
        DateTimeOffset before = DateTimeOffset.UtcNow;

        OrderingOptionsResponse options = await GetOptionsAsync(patron);

        PickupWindowResponse pickup = options.Pickup;
        Assert.Equal("Africa/Johannesburg", pickup.TimeZone);
        Assert.True(pickup.HasAvailableTimes);
        Assert.InRange(pickup.EarliestUtc, before.AddMinutes(pickup.LeadTimeMinutes - 1), before.AddMinutes(pickup.LeadTimeMinutes + 2));
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");
        string lastDay = TimeZoneInfo.ConvertTime(before, zone).Date.AddDays(pickup.MaxAdvanceDays)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Equal($"{lastDay}T23:59", pickup.LatestLocal);
        Assert.Equal(TimeSpan.Zero, pickup.LatestUtc.Offset);
    }

    [Fact]
    public async Task InvalidSettings_MakeOrderingUnavailable_WithoutGuessing()
    {
        using HttpClient patron = await factory.CreatePatronClientAsync();
        using IServiceScope scope = factory.Services.CreateScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        string key = SettingKeys.MaxDrinksPerOrder;

        // Bypass the management rules on purpose to simulate a corrupted value.
        await db.Database.ExecuteSqlAsync($"UPDATE milkshake.system_settings SET value = {"not-a-number"} WHERE key = {key}");
        try
        {
            using HttpResponseMessage response = await patron.GetAsync(new Uri(OptionsPath, UriKind.Relative));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Contains(OrderingMessages.UnavailableTitle, body, StringComparison.Ordinal);
            Assert.DoesNotContain("not-a-number", body, StringComparison.Ordinal);
        }
        finally
        {
            await db.Database.ExecuteSqlAsync($"UPDATE milkshake.system_settings SET value = {"10"} WHERE key = {key}");
        }
    }

    private static async Task<OrderingOptionsResponse> GetOptionsAsync(HttpClient patron) =>
        await patron.GetFromJsonAsync<OrderingOptionsResponse>(new Uri(OptionsPath, UriKind.Relative))
        ?? throw new InvalidOperationException("No options returned.");

    private static async Task<CatalogItemResponse> CreateItemAsync(ManagerSession manager, string kind, decimal price)
    {
        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, $"/api/manager/catalog/{kind}", new { name = $"Order {Guid.NewGuid():N}", displayOrder = 90, initialPrice = price });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<CatalogItemResponse>();
    }

    private static async Task<RestaurantResponse> CreateRestaurantAsync(ManagerSession manager)
    {
        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, "/api/manager/restaurants", new { name = $"Branch {Guid.NewGuid():N}", address = "1 Order Street" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<RestaurantResponse>();
    }

    private static async Task PutSettingAsync(ManagerSession manager, string key, string value)
    {
        List<SettingResponse> settings = await manager.Client.GetFromJsonAsync<List<SettingResponse>>(new Uri("/api/manager/settings", UriKind.Relative))
            ?? throw new InvalidOperationException("No settings returned.");
        SettingResponse setting = settings.Single(s => s.Key == key);
        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Put, $"/api/manager/settings/{key}", new { value, version = setting.Version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}