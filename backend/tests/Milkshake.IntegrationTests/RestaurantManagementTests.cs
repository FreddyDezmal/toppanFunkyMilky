using System.Net;
using Milkshake.Api.Contracts.Management;
using Milkshake.IntegrationTests.Infrastructure;

namespace Milkshake.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class RestaurantManagementTests(PostgresApiFactory factory)
{
    private const string RestaurantsPath = "/api/manager/restaurants";

    [Fact]
    public async Task Create_ReturnsActiveRestaurant()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        string name = UniqueName();

        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, RestaurantsPath, new { name, address = "1 Test Street" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        RestaurantResponse restaurant = await response.ReadJsonAsync<RestaurantResponse>();
        Assert.Equal(name, restaurant.Name);
        Assert.Equal("1 Test Street", restaurant.Address);
        Assert.True(restaurant.IsActive);
    }

    [Fact]
    public async Task Create_WithNameDifferingOnlyByCase_Returns400()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        string name = UniqueName();
        await CreateAsync(manager, name);

        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, RestaurantsPath, new { name = name.ToLowerInvariant(), address = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("name", (await response.ReadJsonAsync<ValidationErrorBody>()).Errors.Keys);
    }

    [Fact]
    public async Task Create_WithAddressTooLong_Returns400()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, RestaurantsPath, new { name = UniqueName(), address = new string('a', 251) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("address", (await response.ReadJsonAsync<ValidationErrorBody>()).Errors.Keys);
    }

    [Fact]
    public async Task Update_WithStaleVersion_Returns409()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        string name = UniqueName();
        RestaurantResponse created = await CreateAsync(manager, name);
        string path = $"{RestaurantsPath}/{created.Id}";

        using HttpResponseMessage first = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Put, path, new { name, address = "New address", version = created.Version });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using HttpResponseMessage stale = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Put, path, new { name, address = "Other address", version = created.Version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Deactivate_HidesFromActiveList()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        RestaurantResponse created = await CreateAsync(manager, UniqueName());

        using HttpResponseMessage deactivate = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, $"{RestaurantsPath}/{created.Id}/deactivate", new { version = created.Version });

        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        Assert.False((await deactivate.ReadJsonAsync<RestaurantResponse>()).IsActive);
        using HttpResponseMessage active = await manager.Client.GetAsync(new Uri($"{RestaurantsPath}?status=active", UriKind.Relative));
        List<RestaurantResponse> activeList = await active.ReadJsonAsync<List<RestaurantResponse>>();
        Assert.DoesNotContain(activeList, r => r.Id == created.Id);
    }

    private static string UniqueName() => $"Restaurant {Guid.NewGuid():N}";

    private static async Task<RestaurantResponse> CreateAsync(ManagerSession manager, string name)
    {
        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, RestaurantsPath, new { name, address = (string?)null });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<RestaurantResponse>();
    }
}