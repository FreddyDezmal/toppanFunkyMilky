using System.Net;
using System.Net.Http.Json;
using System.Text;
using Milkshake.Api.Contracts.Management;
using Milkshake.Application.Ordering;

namespace Milkshake.IntegrationTests.Infrastructure;

/// <summary>A flavour, consistency, topping and restaurant created for one test through the manager API.</summary>
internal sealed record TestMenu(CatalogItemResponse Flavour, CatalogItemResponse Consistency, CatalogItemResponse Topping, RestaurantResponse Restaurant)
{
    /// <summary>One milkshake made of this menu's three items.</summary>
    public DraftDrinkSelection Drink() => new(Flavour.Id, Consistency.Id, Topping.Id);
}

internal static class OrderingTestHelpers
{
    public const string ValidatePath = "/api/ordering/draft/validate";

    /// <summary>POSTs raw JSON text (for malformed and tampered requests) with a fresh anti-forgery token.</summary>
    public static async Task<HttpResponseMessage> PostRawJsonWithCsrfAsync(this HttpClient client, string path, string json)
    {
        string token = await client.GetCsrfTokenAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);

        return await client.SendAsync(request);
    }

    /// <summary>Creates a uniquely named flavour, consistency and topping with the given fees, and a restaurant.</summary>
    public static async Task<TestMenu> CreateTestMenuAsync(this ManagerSession manager, decimal flavourFee, decimal consistencyFee, decimal toppingFee) =>
        new(
            await manager.CreateTestItemAsync("flavours", flavourFee),
            await manager.CreateTestItemAsync("consistencies", consistencyFee),
            await manager.CreateTestItemAsync("toppings", toppingFee),
            await manager.CreateTestRestaurantAsync());

    /// <summary>Creates a uniquely named catalogue item with a starting price.</summary>
    public static async Task<CatalogItemResponse> CreateTestItemAsync(this ManagerSession manager, string kind, decimal price)
    {
        ArgumentNullException.ThrowIfNull(manager);
        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, $"/api/manager/catalog/{kind}", new { name = $"Test {Guid.NewGuid():N}", displayOrder = 95, initialPrice = price });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<CatalogItemResponse>();
    }

    /// <summary>Creates a uniquely named restaurant.</summary>
    public static async Task<RestaurantResponse> CreateTestRestaurantAsync(this ManagerSession manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, "/api/manager/restaurants", new { name = $"Test Branch {Guid.NewGuid():N}", address = (string?)null });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<RestaurantResponse>();
    }

    /// <summary>Changes a setting through the manager API (reads its current version first).</summary>
    public static async Task ChangeSettingAsync(this ManagerSession manager, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(manager);
        List<SettingResponse> settings = await manager.Client.GetFromJsonAsync<List<SettingResponse>>(new Uri("/api/manager/settings", UriKind.Relative))
            ?? throw new InvalidOperationException("No settings returned.");
        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Put, $"/api/manager/settings/{key}", new { value, version = settings.Single(s => s.Key == key).Version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}