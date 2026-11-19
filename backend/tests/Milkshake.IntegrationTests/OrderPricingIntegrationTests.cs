using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Catalog;
using Milkshake.Domain.Pricing;
using Milkshake.Domain.Settings;
using Milkshake.Infrastructure.Persistence;
using Milkshake.IntegrationTests.Infrastructure;

namespace Milkshake.IntegrationTests;

/// <summary>M6 Step 5: the pricing service against PostgreSQL – live VAT, effective dates, and no writes.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class OrderPricingIntegrationTests(PostgresApiFactory factory)
{
    [Fact]
    public async Task AManagersVatChange_AppliesToTheNextCalculation()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);

        PriceBreakdown before = await PriceAsync(factory.Services, menu, drinks: 2);
        await manager.ChangeSettingAsync(SettingKeys.VatRate, "0.16");
        try
        {
            PriceBreakdown after = await PriceAsync(factory.Services, menu, drinks: 2);

            // 2 × (12.00 + 6.00 + 8.00) = 52.00. At 15%: VAT 7.80, total 59.80. At 16%: VAT 8.32, total 60.32.
            Assert.Equal((52.00m, 0.15m, 7.80m, 59.80m), (before.Subtotal.Amount, before.VatRate, before.Vat.Amount, before.Total.Amount));
            Assert.Equal((52.00m, 0.16m, 8.32m, 60.32m), (after.Subtotal.Amount, after.VatRate, after.Vat.Amount, after.Total.Amount));
        }
        finally
        {
            await manager.ChangeSettingAsync(SettingKeys.VatRate, "0.15");
        }
    }

    [Fact]
    public async Task AScheduledPrice_AppliesFromItsStartInstant_NotBefore()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var startsAt = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, TimeSpan.Zero).AddHours(2);

        using HttpResponseMessage scheduled = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post,
            $"/api/manager/catalog/flavours/{menu.Flavour.Id}/prices",
            new { amount = 15.00m, effectiveFrom = startsAt, version = menu.Flavour.Version });
        Assert.Equal(HttpStatusCode.OK, scheduled.StatusCode);

        DateTimeOffset oneMicrosecondBefore = startsAt.AddTicks(-10);
        var clock = new SettableTimeProvider(oneMicrosecondBefore);
        using WebApplicationFactory<ApiAssemblyMarker> app = factory.WithWebHostBuilder(
            builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock)));

        PriceBreakdown justBefore = await PriceAsync(app.Services, menu, drinks: 1);
        clock.Now = startsAt;
        PriceBreakdown atStart = await PriceAsync(app.Services, menu, drinks: 1);

        // Until the start: 12.00 + 6.00 + 8.00 = 26.00. From the start instant: 15.00 + 6.00 + 8.00 = 29.00.
        Assert.Equal((12.00m, 26.00m), (justBefore.Drinks[0].Flavour.Fee.Amount, justBefore.Subtotal.Amount));
        Assert.Equal((15.00m, 29.00m), (atStart.Drinks[0].Flavour.Fee.Amount, atStart.Subtotal.Amount));
        Assert.Equal((oneMicrosecondBefore, startsAt), (justBefore.PricedAtUtc, atStart.PricedAtUtc));
    }

    [Fact]
    public async Task Pricing_WritesNothing_AndLeavesThePriceHistoryUnchanged()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        (int auditRows, string prices) before = await SnapshotAsync();

        PriceBreakdown price = await PriceAsync(factory.Services, menu, drinks: 3);

        (int auditRows, string prices) after = await SnapshotAsync();
        Assert.Equal(78.00m, price.Subtotal.Amount);   // 3 × 26.00
        Assert.Equal(before.auditRows, after.auditRows);
        Assert.Equal(before.prices, after.prices);
    }

    private static async Task<PriceBreakdown> PriceAsync(IServiceProvider services, TestMenu menu, int drinks)
    {
        using IServiceScope scope = services.CreateScope();
        OrderingOptions options = (await scope.ServiceProvider.GetRequiredService<IOrderingCatalogService>().GetOptionsAsync(CancellationToken.None)).Options
            ?? throw new InvalidOperationException("Ordering is unavailable in the test database.");
        string pickupLocal = options.Pickup.LatestLocal.ToString(PickupRules.LocalFormat, CultureInfo.InvariantCulture);
        var draft = new OrderDraft(menu.Restaurant.Id, pickupLocal, Enumerable.Repeat<DraftDrinkSelection?>(menu.Drink(), drinks).ToList());

        DraftPricingResult result = await scope.ServiceProvider.GetRequiredService<IOrderPricingService>().PriceDraftAsync(draft, CancellationToken.None);
        return result.Price
            ?? throw new InvalidOperationException("Pricing failed: " + string.Join(" ", result.Errors.SelectMany(error => error.Value)));
    }

    private async Task<(int AuditRows, string Prices)> SnapshotAsync()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();

        List<PricePeriod> periods =
        [
            .. await db.FlavourPrices.AsNoTracking().ToListAsync(),
            .. await db.ToppingPrices.AsNoTracking().ToListAsync(),
            .. await db.ConsistencyPrices.AsNoTracking().ToListAsync(),
        ];
        string prices = string.Join(
            "|",
            periods
                .Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.GetType().Name}:{p.Id}:{p.Price}:{p.EffectiveFrom:O}:{p.EffectiveTo:O}:{p.UpdatedAt:O}"))
                .Order(StringComparer.Ordinal));

        return (await db.AuditLogs.CountAsync(), prices);
    }
}