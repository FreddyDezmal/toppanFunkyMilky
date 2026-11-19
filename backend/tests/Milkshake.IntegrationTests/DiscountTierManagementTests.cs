using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api.Contracts.Management;
using Milkshake.Domain.Auditing;
using Milkshake.Infrastructure.Persistence;
using Milkshake.IntegrationTests.Infrastructure;

namespace Milkshake.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class DiscountTierManagementTests(PostgresApiFactory factory)
{
    private const string TiersPath = "/api/manager/discount-tiers";

    [Fact]
    public async Task List_ReturnsThreeTiers_WithActivationBlocked()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        List<DiscountTierResponse> tiers = await ListAsync(manager);

        Assert.Equal([1, 2, 3], tiers.Select(t => t.TierNumber));
        Assert.All(tiers, t =>
        {
            Assert.False(t.CanActivate);
            Assert.Contains("Q-01 to Q-04", t.ActivationBlockedReason, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Update_SavesThresholdsAndRate_AndIsAudited()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        DiscountTierResponse tier = await GetAsync(manager, 2);
        int previousOrders = tier.MinPreviousOrders == 12 ? 11 : 12;

        using HttpResponseMessage response = await PutAsync(manager, 2, new
        {
            minPreviousOrders = previousOrders, minDrinksPerOrder = 3, discountRate = 0.125m, version = tier.Version,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        DiscountTierResponse updated = await response.ReadJsonAsync<DiscountTierResponse>();
        Assert.Equal(previousOrders, updated.MinPreviousOrders);
        Assert.Equal(0.125m, updated.DiscountRate);
        Assert.False(updated.IsActive);

        using IServiceScope scope = factory.Services.CreateScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        int tierId = await db.DiscountTiers.Where(t => t.TierNumber == 2).Select(t => t.Id).SingleAsync();
        string recordId = tierId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(await db.AuditLogs.AnyAsync(a =>
            a.TableName == "discount_tiers" && a.RecordId == recordId && a.Action == AuditAction.Update && a.UserEmail == manager.Email));
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("0.12345")]
    public async Task Update_WithInvalidRate_Returns400(string rate)
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        DiscountTierResponse tier = await GetAsync(manager, 1);
        decimal discountRate = decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture);

        using HttpResponseMessage response = await PutAsync(manager, 1, new
        {
            minPreviousOrders = 5, minDrinksPerOrder = 2, discountRate, version = tier.Version,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("discountRate", (await response.ReadJsonAsync<ValidationErrorBody>()).Errors.Keys);
    }

    [Fact]
    public async Task Activate_IsRefused_AndTierStaysInactive()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        DiscountTierResponse tier = await GetAsync(manager, 1);

        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, $"{TiersPath}/1/activate", new { version = tier.Version });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("isActive", (await response.ReadJsonAsync<ValidationErrorBody>()).Errors.Keys);
        Assert.False((await GetAsync(manager, 1)).IsActive);
    }

    [Fact]
    public async Task Update_WithStaleVersion_Returns409()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        DiscountTierResponse tier = await GetAsync(manager, 3);

        using HttpResponseMessage first = await PutAsync(manager, 3, new
        {
            minPreviousOrders = tier.MinPreviousOrders + 1, minDrinksPerOrder = tier.MinDrinksPerOrder, discountRate = tier.DiscountRate, version = tier.Version,
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using HttpResponseMessage stale = await PutAsync(manager, 3, new
        {
            minPreviousOrders = 99, minDrinksPerOrder = 4, discountRate = 0.15m, version = tier.Version,
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Update_UnknownTier_Returns404()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        using HttpResponseMessage response = await PutAsync(manager, 4, new
        {
            minPreviousOrders = 1, minDrinksPerOrder = 1, discountRate = 0.1m, version = 1u,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<List<DiscountTierResponse>> ListAsync(ManagerSession manager) =>
        await manager.Client.GetFromJsonAsync<List<DiscountTierResponse>>(new Uri(TiersPath, UriKind.Relative))
        ?? throw new InvalidOperationException("No tiers returned.");

    private static async Task<DiscountTierResponse> GetAsync(ManagerSession manager, int tierNumber) =>
        (await ListAsync(manager)).Single(t => t.TierNumber == tierNumber);

    private static Task<HttpResponseMessage> PutAsync<T>(ManagerSession manager, int tierNumber, T body) =>
        manager.Client.SendWithCsrfAsync(HttpMethod.Put, $"{TiersPath}/{tierNumber}", body);
}