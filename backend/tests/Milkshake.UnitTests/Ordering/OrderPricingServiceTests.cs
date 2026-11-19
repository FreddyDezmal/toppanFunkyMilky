using Milkshake.Application.Ordering;
using Milkshake.Domain.Pricing;

namespace Milkshake.UnitTests.Ordering;

public sealed class OrderPricingServiceTests
{
    private static readonly OrderingOption Vanilla = new(3, "Vanilla", 30.00m, "ZAR");
    private static readonly OrderingOption Thick = new(29, "Thick", 6.00m, "ZAR");
    private static readonly OrderingOption OreoCrumbs = new(19, "Oreo crumbs", 10.00m, "ZAR");
    private static readonly OrderingRestaurant BranchA = new(35, "Branch A", "1 Main Road");
    private static readonly TimeZoneInfo Johannesburg = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);   // 12:00 in Johannesburg
    private const string Tomorrow = "2026-11-20T10:00";

    private sealed class FixedCatalog(OrderingOptionsResult result) : IOrderingCatalogService
    {
        public Task<OrderingOptionsResult> GetOptionsAsync(CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private static OrderPricingService ServiceWith(decimal vatRate)
    {
        var settings = new OrderingSettings(1, 10, 15, 7, Johannesburg, "Africa/Johannesburg", "ZAR", "en-ZA", vatRate);
        var options = new OrderingOptions([Vanilla], [Thick], [OreoCrumbs], [BranchA], settings, PickupRules.WindowFor(settings, Now));
        return new OrderPricingService(new FixedCatalog(new OrderingOptionsResult(options)));
    }

    private static OrderDraft Draft(params DraftDrinkSelection?[] drinks) => new(BranchA.Id, Tomorrow, drinks);

    private static DraftDrinkSelection Complete() => new(Vanilla.Id, Thick.Id, OreoCrumbs.Id);

    [Fact]
    public async Task AValidDraft_IsPricedWithTheFeesAndVatInForce_AtTheInstantTheyWereRead()
    {
        DraftPricingResult result = await ServiceWith(0.15m).PriceDraftAsync(Draft(Complete(), Complete()), CancellationToken.None);

        PriceBreakdown price = Assert.IsType<PriceBreakdown>(result.Price);
        Assert.NotNull(result.Draft);
        // 2 × (30.00 + 6.00 + 10.00) = 92.00; VAT 13.80; total 105.80.
        Assert.Equal((92.00m, 13.80m, 105.80m), (price.Subtotal.Amount, price.Vat.Amount, price.Total.Amount));
        Assert.Equal((Now, "ZAR", 0.15m), (price.PricedAtUtc, price.CurrencyCode, price.VatRate));
        Assert.Equal(["Vanilla", "Vanilla"], price.Drinks.Select(d => d.Flavour.Name));
    }

    [Fact]
    public async Task TheVatRate_ComesFromTheSettings()
    {
        DraftPricingResult result = await ServiceWith(0.16m).PriceDraftAsync(Draft(Complete()), CancellationToken.None);

        // 46.00 × 16% = 7.36; total 53.36.
        Assert.Equal((7.36m, 53.36m), (result.Price!.Vat.Amount, result.Price.Total.Amount));
    }

    [Fact]
    public async Task NoDiscountIsApplied_UntilTheRulesAreConfirmed()
    {
        DraftPricingResult result = await ServiceWith(0.15m).PriceDraftAsync(Draft(Complete()), CancellationToken.None);

        Assert.Equal((0.00m, (int?)null), (result.Price!.Discount.Amount.Amount, result.Price.Discount.TierNumber));
    }

    [Fact]
    public async Task AnInvalidDraft_ReturnsTheFieldErrors_AndNoPrice()
    {
        DraftPricingResult result = await ServiceWith(0.15m).PriceDraftAsync(
            Draft(new DraftDrinkSelection(Vanilla.Id, null, OreoCrumbs.Id)), CancellationToken.None);

        Assert.Null(result.Price);
        Assert.Contains(OrderDraftFields.Consistency(0), result.Errors.Keys);
    }

    [Fact]
    public async Task UnavailableOptions_MakePricingUnavailable()
    {
        var service = new OrderPricingService(new FixedCatalog(OrderingOptionsResult.Unavailable()));

        DraftPricingResult result = await service.PriceDraftAsync(Draft(Complete()), CancellationToken.None);

        Assert.True(result.OrderingUnavailable);
        Assert.Null(result.Price);
    }
}