using Milkshake.Application.Abstractions;
using Milkshake.Application.Management;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Common;
using Milkshake.Domain.Pricing;

namespace Milkshake.UnitTests.Ordering;

/// <summary>The discount gate (decision P5, opened 22 Nov 2026) and the pure discount decision.</summary>
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
    public void TheActivationGate_IsOpen()
    {
        // Opened on 22 Nov 2026 once Q-01 to Q-04 were decided for the demonstration; tiers still need a manager to activate them.
        Assert.True(DiscountPolicy.RulesConfirmed);
    }

    [Fact]
    public async Task TheLiveResolver_AppliesTheReachedTier_FromConfigurationAndHistory()
    {
        var resolver = new DiscountResolver(new SignedInPatron(), new FixedConfiguration(Configuration), new FixedHistory(TenOrdersOfThree));

        DiscountTerms terms = Assert.IsType<DiscountTerms>(await resolver.ResolveAsync(CancellationToken.None));
        Assert.Equal((2, 0.10m, 50.00m), (terms.TierNumber, terms.Rate, terms.Cap.Amount));
    }

    [Fact]
    public async Task TheLiveResolver_ReadsNothing_WhenNobodyIsSignedIn()
    {
        var resolver = new DiscountResolver(new Anonymous(), new FailingConfiguration(), new FailingHistory());

        Assert.Null(await resolver.ResolveAsync(CancellationToken.None));
    }

    private sealed class SignedInPatron : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();

        public string? Email => "patron@example.test";
    }

    private sealed class Anonymous : ICurrentUser
    {
        public Guid? UserId => null;

        public string? Email => null;
    }

    private sealed class FixedConfiguration(DiscountConfiguration configuration) : IDiscountConfigurationSource
    {
        public Task<DiscountConfiguration> GetAsync(CancellationToken cancellationToken) => Task.FromResult(configuration);
    }

    private sealed class FixedHistory(DiscountHistory history) : IDiscountEligibilitySource
    {
        public Task<DiscountHistory> GetHistoryAsync(Guid patronId, CancellationToken cancellationToken) => Task.FromResult(history);
    }

    private sealed class FailingConfiguration : IDiscountConfigurationSource
    {
        public Task<DiscountConfiguration> GetAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The discount configuration must not be read without a signed-in patron.");
    }

    private sealed class FailingHistory : IDiscountEligibilitySource
    {
        public Task<DiscountHistory> GetHistoryAsync(Guid patronId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Order history must not be read without a signed-in patron.");
    }
}