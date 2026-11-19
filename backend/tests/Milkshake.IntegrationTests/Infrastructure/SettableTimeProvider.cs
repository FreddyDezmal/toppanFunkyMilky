namespace Milkshake.IntegrationTests.Infrastructure;

/// <summary>A clock a test can set, to price at an exact instant (e.g. the moment a scheduled price starts).</summary>
internal sealed class SettableTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <summary>The current time this clock reports.</summary>
    public DateTimeOffset Now { get; set; } = now;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
}