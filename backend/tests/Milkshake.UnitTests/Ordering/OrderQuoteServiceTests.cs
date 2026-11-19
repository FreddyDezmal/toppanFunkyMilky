using Milkshake.Application.Ordering;
using Milkshake.Domain.Pricing;

namespace Milkshake.UnitTests.Ordering;

/// <summary>The live drinks quote (M6 Step 7): per-drink prices while choosing, totals once every drink is complete.</summary>
public sealed class OrderQuoteServiceTests
{
    private static readonly OrderingOption Vanilla = new(3, "Vanilla", 30.00m, "ZAR");
    private static readonly OrderingOption Thick = new(29, "Thick", 6.00m, "ZAR");
    private static readonly OrderingOption OreoCrumbs = new(19, "Oreo crumbs", 10.00m, "ZAR");
    private static readonly OrderingRestaurant BranchA = new(35, "Branch A", null);
    private static readonly TimeZoneInfo Johannesburg = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    private sealed class FixedCatalog(OrderingOptionsResult result) : IOrderingCatalogService
    {
        public Task<OrderingOptionsResult> GetOptionsAsync(CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class NoDiscount : IDiscountResolver
    {
        public Task<DiscountTerms?> ResolveAsync(CancellationToken cancellationToken) => Task.FromResult<DiscountTerms?>(null);
    }

    private static OrderPricingService Service(int maxDrinks = 10)
    {
        var settings = new OrderingSettings(1, maxDrinks, 15, 7, Johannesburg, "Africa/Johannesburg", "ZAR", "en-ZA", 0.15m);
        var options = new OrderingOptions([Vanilla], [Thick], [OreoCrumbs], [BranchA], settings, PickupRules.WindowFor(settings, Now));
        return new OrderPricingService(new FixedCatalog(new OrderingOptionsResult(options)), new NoDiscount());
    }

    private static DraftDrinkSelection Complete() => new(Vanilla.Id, Thick.Id, OreoCrumbs.Id);

    [Fact]
    public async Task CompleteDrinks_ArePricedWithTotals()
    {
        DrinksQuoteResult result = await Service().QuoteDrinksAsync([Complete(), Complete()], CancellationToken.None);

        DrinksQuote quote = Assert.IsType<DrinksQuote>(result.Quote);
        Assert.Equal([46.00m, 46.00m], quote.Lines.Select(line => line.Price!.Amount));
        // 92.00; VAT 13.80; total 105.80.
        Assert.Equal((92.00m, 13.80m, 105.80m), (quote.Totals!.Subtotal.Amount, quote.Totals.Vat.Amount, quote.Totals.Total.Amount));
        Assert.Equal((Now, "ZAR", 0.15m), (quote.PricedAtUtc, quote.CurrencyCode, quote.VatRate));
    }

    [Fact]
    public async Task IncompleteDrinks_HaveNoPrice_AndThereAreNoTotalsYet()
    {
        DrinksQuoteResult result = await Service().QuoteDrinksAsync(
            [Complete(), new DraftDrinkSelection(Vanilla.Id, null, null), null], CancellationToken.None);

        DrinksQuote quote = Assert.IsType<DrinksQuote>(result.Quote);
        Assert.Empty(result.Errors);
        Assert.Equal([(1, (decimal?)46.00m), (2, null), (3, null)], quote.Lines.Select(line => (line.Number, line.Price?.Amount)));
        Assert.Null(quote.Totals);
    }

    [Fact]
    public async Task AnUnknownChoice_IsAnError_EvenWhileChoosing()
    {
        DrinksQuoteResult result = await Service().QuoteDrinksAsync(
            [new DraftDrinkSelection(999, null, null)], CancellationToken.None);

        Assert.Null(result.Quote);
        Assert.Equal(OrderDraftMessages.NoLongerAvailable("flavour", 1), Assert.Single(result.Errors[OrderDraftFields.Flavour(0)]));
    }

    [Fact]
    public async Task MoreDrinksThanTheMaximum_GetOneQuantityMessage()
    {
        DrinksQuoteResult result = await Service(maxDrinks: 2).QuoteDrinksAsync(
            [Complete(), Complete(), Complete()], CancellationToken.None);

        Assert.Equal(new[] { OrderDraftFields.Drinks }, result.Errors.Keys.ToArray());
    }

    [Fact]
    public async Task NoDrinks_IsBelowTheMinimum()
    {
        DrinksQuoteResult result = await Service().QuoteDrinksAsync([], CancellationToken.None);

        Assert.Contains(OrderDraftFields.Drinks, result.Errors.Keys);
    }

    [Fact]
    public async Task UnavailableOptions_MakeTheQuoteUnavailable()
    {
        var service = new OrderPricingService(new FixedCatalog(OrderingOptionsResult.Unavailable()), new NoDiscount());

        DrinksQuoteResult result = await service.QuoteDrinksAsync([Complete()], CancellationToken.None);

        Assert.True(result.OrderingUnavailable);
    }
}