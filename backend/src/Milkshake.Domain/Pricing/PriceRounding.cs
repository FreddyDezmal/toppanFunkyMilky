namespace Milkshake.Domain.Pricing;

/// <summary>
/// The one rounding rule for money (M6 decision P3): two decimal places, midpoints away from zero (0.005 → 0.01).
/// Fees and drink prices are already whole cents, so only VAT and the discount are ever rounded, once per order.
/// </summary>
public static class PriceRounding
{
    /// <summary>Rounds an amount to cents, half away from zero.</summary>
    public static decimal ToCents(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}