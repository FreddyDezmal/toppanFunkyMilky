namespace Milkshake.Domain.Payments;

/// <summary>
/// Exact conversion between an amount and the provider's minor units (cents for ZAR). Only currencies whose scale is
/// known are accepted, so an unexpected currency can never be charged at the wrong magnitude. No floating point.
/// </summary>
public static class MinorUnits
{
    private static readonly Dictionary<string, int> Scale = new(StringComparer.Ordinal) { ["ZAR"] = 2 };

    public static bool IsSupported(string currencyCode) => Scale.ContainsKey(currencyCode);

    public static long ToMinor(decimal amount, string currencyCode)
    {
        int scale = ScaleOf(currencyCode);
        decimal minor = amount * Pow10(scale);
        if (minor != decimal.Truncate(minor) || minor <= 0)
        {
            throw new ArgumentException($"{amount} {currencyCode} is not a positive whole number of minor units.", nameof(amount));
        }

        return checked((long)minor);
    }

    public static decimal FromMinor(long minor, string currencyCode) => minor / Pow10(ScaleOf(currencyCode));

    private static int ScaleOf(string currencyCode) =>
        Scale.TryGetValue(currencyCode, out int scale)
            ? scale
            : throw new ArgumentException($"Currency {currencyCode} is not supported for payments.", nameof(currencyCode));

    private static decimal Pow10(int scale) => scale switch { 0 => 1m, 2 => 100m, 3 => 1000m, _ => throw new ArgumentOutOfRangeException(nameof(scale)) };
}
