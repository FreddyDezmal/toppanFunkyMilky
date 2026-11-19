using Milkshake.Application.Ordering;

namespace Milkshake.UnitTests.Ordering;

public sealed class OrderDraftRulesTests
{
    private static readonly OrderingOption Vanilla = new(3, "Vanilla", 30.00m, "ZAR");
    private static readonly OrderingOption Thick = new(29, "Thick", 6.00m, "ZAR");
    private static readonly OrderingOption OreoCrumbs = new(19, "Oreo crumbs", 10.00m, "ZAR");
    private static readonly OrderingRestaurant BranchA = new(35, "Branch A", "1 Main Road");

    private static OrderingOptions Options(int min = 1, int max = 10) =>
        new(
            [Vanilla],
            [Thick],
            [OreoCrumbs],
            [BranchA],
            new OrderingSettings(min, max, 15, 7, TimeZoneInfo.Utc, "Etc/UTC", "ZAR", "en-ZA"),
            new PickupWindow(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTime.UnixEpoch, DateTime.UnixEpoch));

    private static DraftDrinkSelection Complete() => new(Vanilla.Id, Thick.Id, OreoCrumbs.Id);

    [Fact]
    public void Validate_CompleteDraft_ResolvesNamesAndFees_AndAllowsIdenticalDrinks()
    {
        DraftValidationResult result = OrderDraftRules.Validate(new OrderDraft(BranchA.Id, [Complete(), Complete()]), Options());

        ValidatedDraft draft = Assert.IsType<ValidatedDraft>(result.Draft);
        Assert.Empty(result.Errors);
        Assert.Equal(BranchA, draft.Restaurant);
        Assert.Equal([1, 2], draft.Drinks.Select(d => d.Number));
        Assert.All(draft.Drinks, d => Assert.Equal((Vanilla, Thick, OreoCrumbs), (d.Flavour, d.Consistency, d.Topping)));
    }

    [Fact]
    public void Validate_MoreDrinksThanTheMaximum_ReportsOnlyTheQuantity()
    {
        DraftDrinkSelection?[] eleven = Enumerable.Range(0, 11).Select(_ => (DraftDrinkSelection?)new DraftDrinkSelection(null, null, null)).ToArray();

        DraftValidationResult result = OrderDraftRules.Validate(new OrderDraft(BranchA.Id, eleven), Options(max: 10));

        Assert.Equal(new[] { OrderDraftFields.Drinks }, result.Errors.Keys.ToArray());
        Assert.Equal(OrderDraftMessages.Quantity(1, 10), Assert.Single(result.Errors[OrderDraftFields.Drinks]));
    }

    [Fact]
    public void Validate_NoDrinks_ReportsTheQuantity()
    {
        DraftValidationResult result = OrderDraftRules.Validate(new OrderDraft(BranchA.Id, []), Options());

        Assert.Null(result.Draft);
        Assert.Contains(OrderDraftFields.Drinks, result.Errors.Keys);
    }

    [Fact]
    public void Validate_MissingChoices_AreReportedPerDrinkAndField()
    {
        var draft = new OrderDraft(BranchA.Id, [new DraftDrinkSelection(Vanilla.Id, null, null), new DraftDrinkSelection(null, Thick.Id, OreoCrumbs.Id)]);

        DraftValidationResult result = OrderDraftRules.Validate(draft, Options());

        Assert.Equal(
            new[] { OrderDraftFields.Consistency(0), OrderDraftFields.Topping(0), OrderDraftFields.Flavour(1) },
            result.Errors.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(OrderDraftMessages.Choose("flavour", 2), Assert.Single(result.Errors[OrderDraftFields.Flavour(1)]));
    }

    [Fact]
    public void Validate_UnknownIds_AreReportedAsNoLongerAvailable()
    {
        var draft = new OrderDraft(BranchA.Id, [new DraftDrinkSelection(999, Thick.Id, -5)]);

        DraftValidationResult result = OrderDraftRules.Validate(draft, Options());

        Assert.Equal(OrderDraftMessages.NoLongerAvailable("flavour", 1), Assert.Single(result.Errors[OrderDraftFields.Flavour(0)]));
        Assert.Equal(OrderDraftMessages.NoLongerAvailable("topping", 1), Assert.Single(result.Errors[OrderDraftFields.Topping(0)]));
    }

    [Fact]
    public void Validate_MissingOrUnknownRestaurant_IsReported()
    {
        DraftValidationResult missing = OrderDraftRules.Validate(new OrderDraft(null, [Complete()]), Options());
        DraftValidationResult unknown = OrderDraftRules.Validate(new OrderDraft(999, [Complete()]), Options());

        Assert.Equal(OrderDraftMessages.RestaurantRequired, Assert.Single(missing.Errors[OrderDraftFields.RestaurantId]));
        Assert.Equal(OrderDraftMessages.RestaurantUnavailable, Assert.Single(unknown.Errors[OrderDraftFields.RestaurantId]));
    }

    [Fact]
    public void Validate_EmptyDrinkEntry_IsReported()
    {
        DraftValidationResult result = OrderDraftRules.Validate(new OrderDraft(BranchA.Id, [Complete(), null]), Options());

        Assert.Equal(OrderDraftMessages.DrinkMissing(2), Assert.Single(result.Errors[OrderDraftFields.Drink(1)]));
    }
}