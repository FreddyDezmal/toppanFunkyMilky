using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Pricing;
using Milkshake.IntegrationTests.Infrastructure;

namespace Milkshake.IntegrationTests;

/// <summary>M6 Step 6: discounts are deferred (decision P5) – configuration readable, live discount always 0.00.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class DiscountIntegrationTests(PostgresApiFactory factory)
{
    [Fact]
    public async Task TheDiscountConfiguration_IsReadFromTheTierAndSettingTables()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();   // makes sure tiers and settings exist
        using IServiceScope scope = factory.Services.CreateScope();

        DiscountConfiguration configuration = await scope.ServiceProvider.GetRequiredService<IDiscountConfigurationSource>()
            .GetAsync(CancellationToken.None);

        Assert.Equal([1, 2, 3], configuration.Tiers.Select(tier => tier.TierNumber));
        Assert.All(configuration.Tiers, tier => Assert.False(tier.IsActive));   // activation is blocked (M10 D9)
        Assert.Equal("ZAR", configuration.MaxDiscount.CurrencyCode);
    }

    [Fact]
    public async Task WithTiersConfigured_TheLivePriceHasNoDiscount()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        using IServiceScope scope = factory.Services.CreateScope();
        OrderingOptions options = (await scope.ServiceProvider.GetRequiredService<IOrderingCatalogService>().GetOptionsAsync(CancellationToken.None)).Options
            ?? throw new InvalidOperationException("Ordering is unavailable in the test database.");
        var draft = new OrderDraft(
            menu.Restaurant.Id,
            options.Pickup.LatestLocal.ToString(PickupRules.LocalFormat, CultureInfo.InvariantCulture),
            Enumerable.Repeat<DraftDrinkSelection?>(menu.Drink(), 10).ToList());

        DraftPricingResult result = await scope.ServiceProvider.GetRequiredService<IOrderPricingService>().PriceDraftAsync(draft, CancellationToken.None);

        // 10 × 26.00 = 260.00; no discount; VAT 39.00; total 299.00.
        PriceBreakdown price = Assert.IsType<PriceBreakdown>(result.Price);
        Assert.Equal((0.00m, (int?)null, false), (price.Discount.Amount.Amount, price.Discount.TierNumber, price.Discount.CapApplied));
        Assert.Equal((260.00m, 39.00m, 299.00m), (price.Subtotal.Amount, price.Vat.Amount, price.Total.Amount));
    }
}