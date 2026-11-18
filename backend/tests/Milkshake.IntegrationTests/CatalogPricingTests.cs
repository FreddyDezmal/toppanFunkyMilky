using System.Globalization;
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
public sealed class CatalogPricingTests(PostgresApiFactory factory)
{
    private const string FlavoursPath = "/api/manager/catalog/flavours";

    [Fact]
    public async Task History_ForNewItem_HasOneCurrentPrice()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        CatalogItemResponse item = await CreateFlavourAsync(manager);

        PriceHistoryResponse history = await GetHistoryAsync(manager, item.Id);

        PricePeriodResponse only = Assert.Single(history.Prices);
        Assert.Equal("Current", only.Status);
        Assert.Equal(12.50m, only.Amount);
        Assert.Null(only.EffectiveTo);
        Assert.False(only.CanCancel);
    }

    [Fact]
    public async Task Schedule_Immediately_ReplacesCurrentPriceAndKeepsTheOldOneAsHistory()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        CatalogItemResponse item = await CreateFlavourAsync(manager);

        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, PricesPath(item.Id), new { amount = 15.00m, effectiveFrom = (DateTimeOffset?)null, version = item.Version });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PriceHistoryResponse history = await response.ReadJsonAsync<PriceHistoryResponse>();
        Assert.Equal(15.00m, history.Item.CurrentPrice);
        Assert.NotEqual(item.Version, history.Item.Version);
        Assert.Equal(2, history.Prices.Count);
        PricePeriodResponse newest = history.Prices[0];
        PricePeriodResponse oldest = history.Prices[1];
        Assert.Equal(("Current", 15.00m), (newest.Status, newest.Amount));
        Assert.Equal(("Past", 12.50m), (oldest.Status, oldest.Amount));
        Assert.Equal(newest.EffectiveFrom, oldest.EffectiveTo);
    }

    [Fact]
    public async Task Schedule_InTheFuture_KeepsCurrentPriceUntilItStarts()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        CatalogItemResponse item = await CreateFlavourAsync(manager);
        DateTimeOffset start = MidnightInDays(7);

        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, PricesPath(item.Id), new { amount = 20.00m, effectiveFrom = start, version = item.Version });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PriceHistoryResponse history = await response.ReadJsonAsync<PriceHistoryResponse>();
        Assert.Equal(12.50m, history.Item.CurrentPrice);
        Assert.Equal(20.00m, history.Item.ScheduledPrice);
        Assert.Equal(start, history.Item.ScheduledFrom);
        PricePeriodResponse scheduled = history.Prices[0];
        Assert.Equal("Scheduled", scheduled.Status);
        Assert.True(scheduled.CanCancel);
    }

    [Fact]
    public async Task Schedule_InThePast_Returns400ForEffectiveFrom()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        CatalogItemResponse item = await CreateFlavourAsync(manager);

        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, PricesPath(item.Id), new { amount = 20.00m, effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1), version = item.Version });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("effectiveFrom", (await response.ReadJsonAsync<ValidationErrorBody>()).Errors.Keys);
        Assert.Single((await GetHistoryAsync(manager, item.Id)).Prices);
    }

    [Fact]
    public async Task Schedule_WithThreeDecimalPlaces_Returns400ForAmount()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        CatalogItemResponse item = await CreateFlavourAsync(manager);

        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, PricesPath(item.Id), new { amount = 10.555m, version = item.Version });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("amount", (await response.ReadJsonAsync<ValidationErrorBody>()).Errors.Keys);
    }

    [Fact]
    public async Task Schedule_WithStaleVersion_Returns409AndChangesNothing()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        CatalogItemResponse item = await CreateFlavourAsync(manager);

        using HttpResponseMessage first = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, PricesPath(item.Id), new { amount = 15.00m, version = item.Version });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using HttpResponseMessage stale = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, PricesPath(item.Id), new { amount = 99.00m, version = item.Version });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        PriceHistoryResponse history = await GetHistoryAsync(manager, item.Id);
        Assert.Equal(2, history.Prices.Count);
        Assert.Equal(15.00m, history.Item.CurrentPrice);
    }

    [Fact]
    public async Task CancelScheduled_ReopensThePreviousPriceAndIsAudited()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        CatalogItemResponse item = await CreateFlavourAsync(manager);
        using HttpResponseMessage schedule = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, PricesPath(item.Id), new { amount = 20.00m, effectiveFrom = MidnightInDays(7), version = item.Version });
        PriceHistoryResponse scheduledHistory = await schedule.ReadJsonAsync<PriceHistoryResponse>();
        PricePeriodResponse scheduled = scheduledHistory.Prices[0];

        using HttpResponseMessage cancel = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, $"{PricesPath(item.Id)}/{scheduled.Id}/cancel", new { version = scheduledHistory.Item.Version });

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        PriceHistoryResponse after = await cancel.ReadJsonAsync<PriceHistoryResponse>();
        PricePeriodResponse remaining = Assert.Single(after.Prices);
        Assert.Equal("Current", remaining.Status);
        Assert.Null(remaining.EffectiveTo);
        Assert.Null(after.Item.ScheduledPrice);

        using IServiceScope scope = factory.Services.CreateScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        string recordId = scheduled.Id.ToString(CultureInfo.InvariantCulture);
        Assert.True(await db.AuditLogs.AnyAsync(a =>
            a.TableName == "flavour_prices" && a.RecordId == recordId && a.Action == AuditAction.Delete && a.UserEmail == manager.Email));
    }

    [Fact]
    public async Task Cancel_PriceThatHasTakenEffect_Returns400()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        CatalogItemResponse item = await CreateFlavourAsync(manager);
        PricePeriodResponse current = Assert.Single((await GetHistoryAsync(manager, item.Id)).Prices);

        using HttpResponseMessage cancel = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, $"{PricesPath(item.Id)}/{current.Id}/cancel", new { version = item.Version });

        Assert.Equal(HttpStatusCode.BadRequest, cancel.StatusCode);
        Assert.Contains("priceId", (await cancel.ReadJsonAsync<ValidationErrorBody>()).Errors.Keys);
    }

    private static string PricesPath(int id) => $"{FlavoursPath}/{id}/prices";

    // Whole-second UTC value, so it round-trips through PostgreSQL (microsecond precision) unchanged.
    private static DateTimeOffset MidnightInDays(int days) => new(DateTime.UtcNow.Date.AddDays(days), TimeSpan.Zero);

    private static async Task<CatalogItemResponse> CreateFlavourAsync(ManagerSession manager)
    {
        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, FlavoursPath, new { name = $"Flavour {Guid.NewGuid():N}", displayOrder = 50, initialPrice = 12.50m });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<CatalogItemResponse>();
    }

    private static async Task<PriceHistoryResponse> GetHistoryAsync(ManagerSession manager, int id) =>
        await manager.Client.GetFromJsonAsync<PriceHistoryResponse>(new Uri(PricesPath(id), UriKind.Relative))
        ?? throw new InvalidOperationException("No price history returned.");
}