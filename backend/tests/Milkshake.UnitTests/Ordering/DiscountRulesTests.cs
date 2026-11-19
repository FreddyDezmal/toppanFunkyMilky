using Milkshake.Application.Abstractions;
using Milkshake.Application.Management;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Common;
using Milkshake.Domain.Pricing;

namespace Milkshake.UnitTests.Ordering;

/// <summary>The discount gate (decision P5) and the pure discount decision, with FICTIONAL rates.</summary>
public sealed class DiscountRulesTests
{
    private static readonly DiscountConfiguration Configuration = new(
        [
            new DiscountTierRule(1, 5, 2, 0.05m, IsActive: true),
            new DiscountTierRule(2, 10, 3, 0.10m, IsActive: true),
        ],
        new Money(50.00m, "ZAR"));

    private static readonly DiscountHistory TenOrdersOfThree = new(Enumerable.Repeat(3, 10).ToArray());

    [Fact]
    public void WhileTheRulesAreUnconfirmed_ThereIsNeverADiscount()
    {
        Assert.Null(DiscountRules.Resolve(rulesConfirmed: false, Configuration, TenOrdersOfThree));
    }

    [Fact]
    public void OnceConfirmed_TheReachedTierAndTheCapAreApplied()
    {
        DiscountTerms terms = Assert.IsType<DiscountTerms>(DiscountRules.Resolve(rulesConfirmed: true, Configuration, TenOrdersOfThree));

        Assert.Equal((2, 0.10m, 50.00m), (terms.TierNumber, terms.Rate, terms.Cap.Amount));
    }

    [Fact]
    public void OnceConfirmed_NoHistoryMeansNoDiscount()
    {
        Assert.Null(DiscountRules.Resolve(rulesConfirmed: true, Configuration, DiscountHistory.Empty));
    }

    [Fact]
    public void OnceConfirmed_InactiveTiersGiveNoDiscount()
    {
        DiscountConfiguration inactive = Configuration with { Tiers = [.. Configuration.Tiers.Select(t => t with { IsActive = false })] };

        Assert.Null(DiscountRules.Resolve(rulesConfirmed: true, inactive, TenOrdersOfThree));
    }

    [Fact]
    public void TheActivationGate_IsStillShut()
    {
        // Opening it is a deliberate code change once Q-01 to Q-04 are answered and order history exists (M7/M8).
        Assert.False(DiscountPolicy.RulesConfirmed);
    }

    [Fact]
    public async Task TheLiveResolver_ReadsNoConfigurationOrHistory_WhileTheGateIsShut()
    {
        var resolver = new DiscountResolver(new SignedInPatron(), new FailingConfiguration(), new FailingHistory());

        Assert.Null(await resolver.ResolveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UntilOrdersExist_EveryPatronHasNoHistory()
    {
        DiscountHistory history = await new NoOrderHistoryYet().GetHistoryAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(history.PreviousOrderDrinkCounts);
    }

    private sealed class SignedInPatron : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();

        public string? Email => "patron@example.test";
    }

    private sealed class FailingConfiguration : IDiscountConfigurationSource
    {
        public Task<DiscountConfiguration> GetAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The discount configuration must not be read while the gate is shut.");
    }

    private sealed class FailingHistory : IDiscountEligibilitySource
    {
        public Task<DiscountHistory> GetHistoryAsync(Guid patronId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Order history must not be read while the gate is shut.");
    }
}