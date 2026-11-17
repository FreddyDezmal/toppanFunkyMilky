using Milkshake.Application.Accounts;

namespace Milkshake.UnitTests.Accounts;

public sealed class MobileNumberTests
{
    [Theory]
    [InlineData("+27 82 123 4567", "+27821234567")]
    [InlineData("+27-82-123-4567", "+27821234567")]
    [InlineData("+44 (20) 7946 0958", "+442079460958")]
    public void Normalize_AcceptsInternationalFormats(string input, string expected) =>
        Assert.Equal(expected, MobileNumber.Normalize(input));

    [Theory]
    [InlineData("0821234567")]
    [InlineData("+0123456789")]
    [InlineData("not a number")]
    [InlineData("")]
    public void Normalize_RejectsNonInternationalNumbers(string input) =>
        Assert.Null(MobileNumber.Normalize(input));
}