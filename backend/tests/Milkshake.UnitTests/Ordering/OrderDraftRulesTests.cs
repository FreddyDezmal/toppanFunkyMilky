using Milkshake.Application.Ordering;

namespace Milkshake.UnitTests.Ordering;

public sealed class OrderDraftRulesTests
{
    private static readonly OrderingOption Vanilla = new(3, "Vanilla", 30.00m, "ZAR");
    private static readonly OrderingOption Thick = new(29, "Thick", 6.00m, "ZAR");
    private static readonly OrderingOption OreoCrumbs = new(19, "Oreo crumbs", 10.00m, "ZAR");
    private static readonly OrderingRestaurant BranchA = new(35, "Branch A", "1 Main Road");

    private static readonly TimeZoneInfo Johannesburg = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);   // 12:00 in Johannesburg
    private const string Tomorrow = "2026-11-20T10:00";

    private static OrderingOptions Options(int min = 1, int max = 10)
    {
        var settings = new OrderingSettings(min, max, 15, 7, Johannesburg, "Africa/Johannesburg", "ZAR", "en-ZA", 0.15m);
        return new OrderingOptions([Vanilla], [Thick], [OreoCrumbs], [BranchA], settings, PickupRules.WindowFor(settings, Now));
    }

    private static DraftDrinkSelection Complete() => new(Vanilla.Id, Thick.Id, OreoCrumbs.Id);

    [Fact]
    public void Validate_CompleteDraft_ResolvesNamesFeesAndPickup_AndAllowsIdenticalDrinks()
    {
        DraftValidationResult result = OrderDraftRules.Validate(new OrderDraft(BranchA.Id, Tomorrow, [Complete(), Complete()]), Options());

        ValidatedDraft draft = Assert.IsType<ValidatedDraft>(result.Draft);
        Assert.Empty(result.Errors);
        Assert.Equal(BranchA, draft.Restaurant);
        Assert.Equal(new DateTime(2026, 11, 20, 10, 0, 0), draft.Pickup.Local);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero), draft.Pickup.Utc);
        Assert.Equal([1, 2], draft.Drinks.Select(d => d.Number));
        Assert.All(draft.Drinks, d => Assert.Equal((Vanilla, Thick, OreoCrumbs), (d.Flavour, d.Consistency, d.Topping)));
    }

    [Fact]
    public void Validate_MoreDrinksThanTheMaximum_ReportsOnlyTheQuantity()
    {
        DraftDrinkSelection?[] eleven = Enumerable.Range(0, 11).Select(_ => (DraftDrinkSelection?)new DraftDrinkSelection(null, null, null)).ToArray();

        DraftValidationResult result = OrderDraftRules.Validate(new OrderDraft(BranchA.Id, Tomorrow, eleven), Options(max: 10));

        Assert.Equal(new[] { OrderDraftFields.Drinks }, result.Errors.Keys.ToArray());
        Assert.Equal(OrderDraftMessages.Quantity(1, 10), Assert.Single(result.Errors[OrderDraftFields.Drinks]));
    }

    [Fact]
    public void Validate_NoDrinks_ReportsTheQuantity()
    {
        DraftValidationResult result = OrderDraftRules.Validate(new OrderDraft(BranchA.Id, Tomorrow, []), Options());

        Assert.Null(result.Draft);
        Assert.Contains(OrderDraftFields.Drinks, result.Errors.Keys);
    }

    [Fact]
    public void Validate_MissingChoices_AreReportedPerDrinkAndField()
    {
        var draft = new OrderDraft(BranchA.Id, Tomorrow, [new DraftDrinkSelection(Vanilla.Id, null, null), new DraftDrinkSelection(null, Thick.Id, OreoCrumbs.Id)]);

        DraftValidationResult result = OrderDraftRules.Validate(draft, Options());

        Assert.Equal(
            new[] { OrderDraftFields.Consistency(0), OrderDraftFields.Topping(0), OrderDraftFields.Flavour(1) },
            result.Errors.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(OrderDraftMessages.Choose("flavour", 2), Assert.Single(result.Errors[OrderDraftFields.Flavour(1)]));
    }

    [Fact]
    public void Validate_UnknownIds_AreReportedAsNoLongerAvailable()
    {
        var draft = new OrderDraft(BranchA.Id, Tomorrow, [new DraftDrinkSelection(999, Thick.Id, -5)]);

        DraftValidationResult result = OrderDraftRules.Validate(draft, Options());

        Assert.Equal(OrderDraftMessages.NoLongerAvailable("flavour", 1), Assert.Single(result.Errors[OrderDraftFields.Flavour(0)]));
        Assert.Equal(OrderDraftMessages.NoLongerAvailable("topping", 1), Assert.Single(result.Errors[OrderDraftFields.Topping(0)]));
    }

    [Fact]
    public void Validate_MissingOrUnknownRestaurant_IsReported()
    {
        DraftValidationResult missing = OrderDraftRules.Validate(new OrderDraft(null, Tomorrow, [Complete()]), Options());
        DraftValidationResult unknown = OrderDraftRules.Validate(new OrderDraft(999, Tomorrow, [Complete()]), Options());

        Assert.Equal(OrderDraftMessages.RestaurantRequired, Assert.Single(missing.Errors[OrderDraftFields.RestaurantId]));
        Assert.Equal(OrderDraftMessages.RestaurantUnavailable, Assert.Single(unknown.Errors[OrderDraftFields.RestaurantId]));
    }

    [Fact]
    public void Validate_EmptyDrinkEntry_IsReported()
    {
        DraftValidationResult result = OrderDraftRules.Validate(new OrderDraft(BranchA.Id, Tomorrow, [Complete(), null]), Options());

        Assert.Equal(OrderDraftMessages.DrinkMissing(2), Assert.Single(result.Errors[OrderDraftFields.Drink(1)]));
    }

    [Fact]
    public void Validate_InvalidPickup_IsReportedUnderPickupLocal_AlongsideOtherErrors()
    {
        DraftValidationResult result = OrderDraftRules.Validate(new OrderDraft(null, "2026-11-19T11:00", [Complete()]), Options());

        Assert.Null(result.Draft);
        Assert.Equal(PickupMessages.InThePast, Assert.Single(result.Errors[OrderDraftFields.PickupLocal]));
        Assert.Contains(OrderDraftFields.RestaurantId, result.Errors.Keys);
    }
}