using Milkshake.Application.Abstractions;
using Milkshake.Domain.Auditing;

namespace Milkshake.UnitTests.Auditing;

/// <summary>M11 actor attribution: the allowed actor names and the ambient system scope.</summary>
public class AuditActorTests
{
    [Theory]
    [InlineData("user")]
    [InlineData("anonymous")]
    [InlineData("system:paystack-webhook")]
    [InlineData("system:payment-reconciliation")]
    [InlineData("system:email-dispatcher")]
    [InlineData("system:seeding")]
    [InlineData("system:manager-provisioning")]
    public void Known_actors_are_valid(string actor)
    {
        Assert.True(AuditActors.IsValid(actor));
    }

    [Theory]
    [InlineData("")]
    [InlineData("User")]
    [InlineData("admin")]
    [InlineData("system:")]
    [InlineData("system:Payment")]
    [InlineData("system:pay ment")]
    [InlineData("system:-x")]
    [InlineData("user\n")]
    [InlineData("system:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Other_values_are_not_actors(string actor)
    {
        Assert.False(AuditActors.IsValid(actor));
    }

    [Fact]
    public void Only_system_actors_are_system()
    {
        Assert.True(AuditActors.IsSystem(AuditActors.EmailDispatcher));
        Assert.False(AuditActors.IsSystem(AuditActors.User));
        Assert.False(AuditActors.IsSystem(AuditActors.Anonymous));
        Assert.False(AuditActors.IsSystem(null));
    }

    [Fact]
    public void A_scope_sets_the_actor_and_restores_the_previous_one()
    {
        Assert.Null(AuditActorScope.Current);
        using (AuditActorScope.Begin(AuditActors.PaymentReconciliation))
        {
            Assert.Equal(AuditActors.PaymentReconciliation, AuditActorScope.Current);
            using (AuditActorScope.Begin(AuditActors.EmailDispatcher))
            {
                Assert.Equal(AuditActors.EmailDispatcher, AuditActorScope.Current);
            }

            Assert.Equal(AuditActors.PaymentReconciliation, AuditActorScope.Current);
        }

        Assert.Null(AuditActorScope.Current);
    }

    [Fact]
    public void Disposing_twice_does_not_disturb_an_outer_scope()
    {
        using IDisposable outer = AuditActorScope.Begin(AuditActors.Seeding);
        IDisposable inner = AuditActorScope.Begin(AuditActors.EmailDispatcher);
        inner.Dispose();
        inner.Dispose();

        Assert.Equal(AuditActors.Seeding, AuditActorScope.Current);
    }

    [Fact]
    public async Task A_scope_flows_into_awaited_work_but_not_back_out_of_it()
    {
        static async Task<string?> InsideAsync()
        {
            await Task.Yield();
            return AuditActorScope.Current;
        }

        static async Task SetWithoutDisposingAsync()
        {
            await Task.Yield();
            AuditActorScope.Begin(AuditActors.EmailDispatcher);
        }

        using (AuditActorScope.Begin(AuditActors.PaystackWebhook))
        {
            Assert.Equal(AuditActors.PaystackWebhook, await InsideAsync());
        }

        await SetWithoutDisposingAsync();   // a scope set inside an awaited method stays inside it
        Assert.Null(AuditActorScope.Current);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("anonymous")]
    [InlineData("nonsense")]
    public void Only_system_actors_can_be_set_as_a_scope(string actor)
    {
        Assert.Throws<ArgumentException>(() => AuditActorScope.Begin(actor));
        Assert.Null(AuditActorScope.Current);
    }

    [Fact]
    public void An_audit_record_rejects_an_unknown_actor()
    {
        Assert.Throws<ArgumentException>(() => new AuditLog(DateTimeOffset.UtcNow, AuditAction.Insert, "orders", "1", "[]", null, null, null, "root"));
        Assert.Equal(AuditActors.User, new AuditLog(DateTimeOffset.UtcNow, AuditAction.Insert, "orders", "1", "[]", null, null, null, AuditActors.User).Actor);
        Assert.Null(new AuditLog(DateTimeOffset.UtcNow, AuditAction.Insert, "orders", "1", "[]", null, null, null, null).Actor);
    }
}
