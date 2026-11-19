namespace Milkshake.Domain.Pricing;

/// <summary>
/// Prices or settings that cannot produce a correct price (e.g. a fee in another currency).
/// Not shown to patrons: the API reports ordering as unavailable and logs the details.
/// </summary>
public sealed class PricingConfigurationException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public PricingConfigurationException()
    {
    }

    /// <summary>Creates the exception with a message for the server log.</summary>
    public PricingConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message for the server log and a cause.</summary>
    public PricingConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}