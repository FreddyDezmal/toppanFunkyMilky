using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api.Contracts.Management;
using Milkshake.Application.Management;
using Milkshake.Domain.Auditing;
using Milkshake.Domain.Settings;
using Milkshake.Infrastructure.Persistence;
using Milkshake.IntegrationTests.Infrastructure;

namespace Milkshake.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class ManagementConcurrencyAndAuditTests(PostgresApiFactory factory)
{
    [Theory]
    [InlineData("flavours", "flavour_prices")]
    [InlineData("toppings", "topping_prices")]
    [InlineData("consistencies", "consistency_prices")]
    public async Task CatalogueChanges_AreAuditedWithTheManager_AndNeverRecordTheRowVersion(string kind, string priceTable)
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        string path = $"/api/manager/catalog/{kind}";

        using HttpResponseMessage created = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, path, new { name = UniqueName(), displayOrder = 60, initialPrice = 7.00m });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        CatalogItemResponse item = await created.ReadJsonAsync<CatalogItemResponse>();

        using HttpResponseMessage deactivated = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, $"{path}/{item.Id}/deactivate", new { version = item.Version });
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);

        PriceHistoryResponse history = await manager.Client.GetFromJsonAsync<PriceHistoryResponse>(new Uri($"{path}/{item.Id}/prices", UriKind.Relative))
            ?? throw new InvalidOperationException("No price history returned.");

        using IServiceScope scope = factory.Services.CreateScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        string recordId = item.Id.ToString(CultureInfo.InvariantCulture);
        List<AuditLog> itemAudit = await db.AuditLogs.Where(a => a.TableName == kind && a.RecordId == recordId).ToListAsync();

        Assert.Contains(itemAudit, a => a.Action == AuditAction.Insert);
        AuditLog deactivation = Assert.Single(itemAudit, a => a.Action == AuditAction.Update);
        Assert.Contains("is_active", deactivation.Changes, StringComparison.Ordinal);
        Assert.All(itemAudit, a =>
        {
            Assert.Equal(manager.Email, a.UserEmail);
            Assert.DoesNotContain("xmin", a.Changes, StringComparison.Ordinal);
        });

        string priceId = history.Prices.Single().Id.ToString(CultureInfo.InvariantCulture);
        Assert.True(await db.AuditLogs.AnyAsync(a =>
            a.TableName == priceTable && a.RecordId == priceId && a.Action == AuditAction.Insert && a.UserEmail == manager.Email));
    }

    [Fact]
    public async Task RestaurantChanges_AreAuditedFieldByField()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        string name = $"Restaurant {Guid.NewGuid():N}";
        using HttpResponseMessage created = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, "/api/manager/restaurants", new { name, address = "1 Audit Road" });
        RestaurantResponse restaurant = await created.ReadJsonAsync<RestaurantResponse>();

        string renamed = name + " Renamed";
        using HttpResponseMessage updated = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Put, $"/api/manager/restaurants/{restaurant.Id}", new { name = renamed, address = "1 Audit Road", version = restaurant.Version });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using IServiceScope scope = factory.Services.CreateScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        string recordId = restaurant.Id.ToString(CultureInfo.InvariantCulture);
        List<AuditLog> audit = await db.AuditLogs.Where(a => a.TableName == "restaurants" && a.RecordId == recordId).ToListAsync();

        Assert.Contains(audit, a => a.Action == AuditAction.Insert && a.UserEmail == manager.Email);
        AuditLog update = Assert.Single(audit, a => a.Action == AuditAction.Update);
        using JsonDocument changes = JsonDocument.Parse(update.Changes);
        JsonElement change = Assert.Single(changes.RootElement.EnumerateArray());   // only the name changed
        Assert.Equal("name", change.GetProperty("field").GetString());
        Assert.Equal(name, change.GetProperty("old").GetString());
        Assert.Equal(renamed, change.GetProperty("new").GetString());
    }

    [Fact]
    public async Task SettingsPartnerCheck_RejectsTheSaveWhenTheOtherLimitChangedMeanwhile()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();   // also seeds the settings
        using IServiceScope first = factory.Services.CreateScope();
        using IServiceScope second = factory.Services.CreateScope();
        MilkshakeDbContext managerA = first.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        MilkshakeDbContext managerB = second.ServiceProvider.GetRequiredService<MilkshakeDbContext>();

        SystemSetting minInA = await managerA.SystemSettings.SingleAsync(s => s.Key == SettingKeys.MinDrinksPerOrder);
        SystemSetting maxInA = await managerA.SystemSettings.SingleAsync(s => s.Key == SettingKeys.MaxDrinksPerOrder);
        string originalMin = minInA.Value;

        // Manager B changes the maximum and saves first.
        SystemSetting maxInB = await managerB.SystemSettings.SingleAsync(s => s.Key == SettingKeys.MaxDrinksPerOrder);
        string originalMax = maxInB.Value;
        maxInB.ChangeValue(originalMax == "11" ? "12" : "11");
        await managerB.SaveChangesAsync();

        // Manager A changes the minimum, version-checking the maximum as SettingsManagementService does.
        minInA.ChangeValue(originalMin == "2" ? "1" : "2");
        managerA.Entry(maxInA).Property(s => s.UpdatedAt).IsModified = true;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => managerA.SaveChangesAsync());

        using (IServiceScope check = factory.Services.CreateScope())
        {
            MilkshakeDbContext db = check.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
            Assert.Equal(originalMin, await db.SystemSettings.Where(s => s.Key == SettingKeys.MinDrinksPerOrder).Select(s => s.Value).SingleAsync());
        }

        maxInB.ChangeValue(originalMax);   // restore for other tests
        await managerB.SaveChangesAsync();
    }

    [Fact]
    public async Task ConflictResponse_IsProblemDetailsWithoutInternals()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        CatalogItemResponse item = await CreateAsync(manager, "flavours", UniqueName());
        string path = $"/api/manager/catalog/flavours/{item.Id}";

        using HttpResponseMessage first = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Put, path, new { name = item.Name + " A", displayOrder = 1, version = item.Version });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using HttpResponseMessage stale = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Put, path, new { name = item.Name + " B", displayOrder = 1, version = item.Version });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("application/problem+json", stale.Content.Headers.ContentType?.MediaType);
        string body = await stale.Content.ReadAsStringAsync();
        Assert.Contains(ManagementMessages.ConflictTitle, body, StringComparison.Ordinal);
        foreach (string leak in new[] { "xmin", "DbUpdate", "Exception", "Npgsql", "   at " })
        {
            Assert.DoesNotContain(leak, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DoubleSubmittedCreate_SavesOnlyOneItem()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        string name = UniqueName();
        var body = new { name, displayOrder = 1, initialPrice = 5.00m };

        HttpResponseMessage[] responses = await Task.WhenAll(
            manager.Client.SendWithCsrfAsync(HttpMethod.Post, "/api/manager/catalog/toppings", body),
            manager.Client.SendWithCsrfAsync(HttpMethod.Post, "/api/manager/catalog/toppings", body));
        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }

        using IServiceScope scope = factory.Services.CreateScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        string lowered = name.ToLowerInvariant();
        Assert.Equal(1, await db.Toppings.CountAsync(t => t.Name.ToLower() == lowered));
    }

    [Fact]
    public async Task DoubleSubmittedPriceChange_AppliesOnlyOnce()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        CatalogItemResponse item = await CreateAsync(manager, "flavours", UniqueName());
        string pricesPath = $"/api/manager/catalog/flavours/{item.Id}/prices";
        var body = new { amount = 15.00m, version = item.Version };

        HttpResponseMessage[] responses = await Task.WhenAll(
            manager.Client.SendWithCsrfAsync(HttpMethod.Post, pricesPath, body),
            manager.Client.SendWithCsrfAsync(HttpMethod.Post, pricesPath, body));
        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }

        PriceHistoryResponse history = await manager.Client.GetFromJsonAsync<PriceHistoryResponse>(new Uri(pricesPath, UriKind.Relative))
            ?? throw new InvalidOperationException("No price history returned.");
        Assert.Equal(2, history.Prices.Count);
        Assert.Single(history.Prices, p => p.EffectiveTo is null);
    }

    [Fact]
    public async Task Create_IgnoresFieldsTheClientMayNotSet()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(HttpMethod.Post, "/api/manager/catalog/flavours", new
        {
            id = 999_999,
            name = UniqueName(),
            displayOrder = 1,
            initialPrice = 5.00m,
            isActive = false,
            version = 123u,
            currentPrice = 0.01m,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        CatalogItemResponse item = await response.ReadJsonAsync<CatalogItemResponse>();
        Assert.NotEqual(999_999, item.Id);
        Assert.True(item.IsActive);
        Assert.Equal(5.00m, item.CurrentPrice);
        Assert.NotEqual(123u, item.Version);
    }

    private static string UniqueName() => $"Item {Guid.NewGuid():N}";

    private static async Task<CatalogItemResponse> CreateAsync(ManagerSession manager, string kind, string name)
    {
        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, $"/api/manager/catalog/{kind}", new { name, displayOrder = 1, initialPrice = 10.00m });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<CatalogItemResponse>();
    }
}