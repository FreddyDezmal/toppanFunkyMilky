using System.Globalization;

namespace Milkshake.Domain.Common;

/// <summary>
/// An amount of money in a specific ISO 4217 currency (e.g. ZAR). Money is never stored as float/double.
/// Display formatting (symbol, separators) is a presentation concern driven by the configured locale.
/// </summary>
public sealed record Money
{
    /// <summary>Length of an ISO 4217 currency code.</summary>
    public const int CurrencyCodeLength = 3;

    /// <summary>Maximum number of decimal places stored for an amount.</summary>
    public const int AmountScale = 2;

    /// <summary>
    /// Creates a non-negative amount with at most two decimal places.
    /// </summary>
    public Money(decimal amount, string currencyCode)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);

        if (decimal.Round(amount, AmountScale) != amount)
        {
            throw new ArgumentException("Amounts may have at most two decimal places; round before creating Money.", nameof(amount));
        }

        string code = currencyCode.Trim().ToUpperInvariant();
        if (code.Length != CurrencyCodeLength || !code.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException("Currency code must be a three-letter ISO 4217 code such as ZAR.", nameof(currencyCode));
        }

        Amount = amount;
        CurrencyCode = code;
    }

    /// <summary>The amount, e.g. 35.00.</summary>
    public decimal Amount { get; }

    /// <summary>ISO 4217 currency code, e.g. ZAR.</summary>
    public string CurrencyCode { get; }

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Amount:0.00} {CurrencyCode}");
}