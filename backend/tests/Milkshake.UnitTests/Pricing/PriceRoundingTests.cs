using Milkshake.Domain.Pricing;

namespace Milkshake.UnitTests.Pricing;

/// <summary>Decision P3: cents, midpoints away from zero (not banker's rounding).</summary>
public sealed class PriceRoundingTests
{
    [Theory]
    [InlineData("0.045", "0.05")]      // midpoint up (banker's rounding would give 0.04)
    [InlineData("0.125", "0.13")]      // midpoint up (banker's would give 0.12)
    [InlineData("0.135", "0.14")]
    [InlineData("0.005", "0.01")]
    [InlineData("0.0049", "0.00")]
    [InlineData("6.915", "6.92")]
    [InlineData("4499.955", "4499.96")]
    [InlineData("6.9", "6.90")]
    [InlineData("0", "0.00")]
    public void ToCents_RoundsHalfAwayFromZero(string amount, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            PriceRounding.ToCents(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));
    }
}