using Milkshake.Domain.Catalog;
using Milkshake.Domain.Common;

namespace Milkshake.UnitTests.Catalog;

public sealed class PriceSchedulingTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NextWeek = Now.AddDays(7);
    private static readonly DateTimeOffset NextMonth = Now.AddMonths(1);

    [Fact]
    public void SchedulePrice_StartingInThePast_IsRejected()
    {
        Flavour flavour = PricedFlavour();

        BusinessRuleException exception = Assert.Throws<BusinessRuleException>(
            () => flavour.SchedulePrice(Zar(35m), Now.AddMinutes(-1), Now));

        Assert.Equal(PricingMessages.NoBackDating, exception.Message);
        Assert.Single(flavour.Prices);
    }

    [Fact]
    public void SchedulePrice_StartingNow_TakesEffectImmediately()
    {
        Flavour flavour = PricedFlavour();

        flavour.SchedulePrice(Zar(35m), Now, Now);

        Assert.Equal(30m, flavour.PriceAt(Now.AddTicks(-1))?.Amount);
        Assert.Equal(35m, flavour.PriceAt(Now)?.Amount);
    }

    [Fact]
    public void SchedulePrice_InTheFuture_KeepsCurrentPriceUntilItStarts()
    {
        Flavour flavour = PricedFlavour();

        flavour.SchedulePrice(Zar(40m), NextWeek, Now);

        Assert.Equal(30m, flavour.PriceAt(Now)?.Amount);
        Assert.Equal(40m, flavour.PriceAt(NextWeek)?.Amount);
    }

    [Fact]
    public void SchedulePrice_BeforeAnAlreadyScheduledPrice_IsRejected()
    {
        Flavour flavour = PricedFlavour();
        flavour.SchedulePrice(Zar(40m), NextMonth, Now);

        BusinessRuleException exception = Assert.Throws<BusinessRuleException>(
            () => flavour.SchedulePrice(Zar(35m), NextWeek, Now));

        Assert.Equal(PricingMessages.MustStartAfterLatest, exception.Message);
    }

    [Fact]
    public void CancelLatestScheduledPrice_RemovesItAndReopensThePreviousPrice()
    {
        Flavour flavour = PricedFlavour();
        FlavourPrice original = flavour.Prices.Single();
        flavour.SchedulePrice(Zar(40m), NextWeek, Now);

        PricePeriod cancelled = flavour.CancelLatestScheduledPrice(Now);

        Assert.Equal(40m, cancelled.Price.Amount);
        Assert.Single(flavour.Prices);
        Assert.Null(original.EffectiveTo);
        Assert.Equal(30m, flavour.PriceAt(NextMonth)?.Amount);
    }

    [Fact]
    public void CancelLatestScheduledPrice_WhenLatestHasTakenEffect_IsRejected()
    {
        Flavour flavour = PricedFlavour();

        BusinessRuleException exception = Assert.Throws<BusinessRuleException>(() => flavour.CancelLatestScheduledPrice(Now));

        Assert.Equal(PricingMessages.NothingScheduled, exception.Message);
    }

    [Fact]
    public void CancelLatestScheduledPrice_WhenItIsTheOnlyPrice_IsRejected()
    {
        var flavour = new Flavour("Vanilla", displayOrder: 1);
        flavour.SetPrice(Zar(30m), NextWeek);

        BusinessRuleException exception = Assert.Throws<BusinessRuleException>(() => flavour.CancelLatestScheduledPrice(Now));

        Assert.Equal(PricingMessages.CannotCancelOnlyPrice, exception.Message);
    }

    [Fact]
    public void CancelLatestScheduledPrice_WithTwoScheduled_CancelsOnlyTheLatest()
    {
        Flavour flavour = PricedFlavour();
        PricePeriod first = flavour.SchedulePrice(Zar(40m), NextWeek, Now);
        flavour.SchedulePrice(Zar(45m), NextMonth, Now);

        PricePeriod cancelled = flavour.CancelLatestScheduledPrice(Now);

        Assert.Equal(45m, cancelled.Price.Amount);
        Assert.Null(first.EffectiveTo);
        Assert.Equal(40m, flavour.PriceAt(NextMonth)?.Amount);
    }

    [Fact]
    public void PricePeriod_StoresTimesInUtc()
    {
        Flavour flavour = PricedFlavour();
        var southAfricanMidnight = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.FromHours(2));

        PricePeriod scheduled = flavour.SchedulePrice(Zar(40m), southAfricanMidnight, Now);

        Assert.Equal(TimeSpan.Zero, scheduled.EffectiveFrom.Offset);
        Assert.Equal(southAfricanMidnight, scheduled.EffectiveFrom);   // same instant
    }

    [Fact]
    public void GetPriceHistory_ReturnsNewestFirst()
    {
        Flavour flavour = PricedFlavour();
        flavour.SchedulePrice(Zar(40m), NextWeek, Now);

        IReadOnlyList<PricePeriod> history = flavour.GetPriceHistory();

        Assert.Equal([40m, 30m], history.Select(p => p.Price.Amount));
    }

    private static Flavour PricedFlavour()
    {
        var flavour = new Flavour("Vanilla", displayOrder: 1);
        flavour.SetPrice(Zar(30m), Created);
        return flavour;
    }

    private static Money Zar(decimal amount) => new(amount, "ZAR");
}
