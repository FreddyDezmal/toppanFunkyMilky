using Milkshake.Application.Ordering;

namespace Milkshake.UnitTests.Orders;

/// <summary>Decision O5: a retry with the same content matches; any change to the content does not.</summary>
public sealed class OrderRequestFingerprintTests
{
    private static OrderDraft Draft(int restaurant = 1, string pickup = "2026-11-20T10:00", params DraftDrinkSelection?[] drinks) =>
        new(restaurant, pickup, drinks.Length == 0 ? [new DraftDrinkSelection(1, 2, 3)] : drinks);

    [Fact]
    public void TheSameContent_GivesTheSameLowercaseSha256()
    {
        string first = OrderRequestFingerprint.Compute(Draft());
        string second = OrderRequestFingerprint.Compute(Draft());

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.True(first.All(char.IsAsciiHexDigitLower));
    }

    [Fact]
    public void AnyChangeToTheContent_GivesADifferentFingerprint()
    {
        string original = OrderRequestFingerprint.Compute(Draft());
        string[] changed =
        [
            OrderRequestFingerprint.Compute(Draft(restaurant: 2)),
            OrderRequestFingerprint.Compute(Draft(pickup: "2026-11-20T10:01")),
            OrderRequestFingerprint.Compute(Draft(drinks: new DraftDrinkSelection(1, 2, 4))),
            OrderRequestFingerprint.Compute(Draft(drinks: [new DraftDrinkSelection(1, 2, 3), new DraftDrinkSelection(1, 2, 3)])),
            OrderRequestFingerprint.Compute(Draft(drinks: new DraftDrinkSelection(1, 23, null))),
            OrderRequestFingerprint.Compute(Draft(drinks: new DraftDrinkSelection(12, 3, null))),
        ];

        Assert.All(changed, fingerprint => Assert.NotEqual(original, fingerprint));
        Assert.Equal(changed.Length, changed.Distinct().Count());
    }

    [Fact]
    public void DrinkOrderMatters()
    {
        var a = new DraftDrinkSelection(1, 2, 3);
        var b = new DraftDrinkSelection(4, 5, 6);

        Assert.NotEqual(OrderRequestFingerprint.Compute(Draft(drinks: [a, b])), OrderRequestFingerprint.Compute(Draft(drinks: [b, a])));
    }
}
