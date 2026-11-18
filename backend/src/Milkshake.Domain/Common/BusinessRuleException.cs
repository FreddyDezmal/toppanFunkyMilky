namespace Milkshake.Domain.Common;

/// <summary>
/// A request broke a business rule. The message is safe to show to the user.
/// Derives from InvalidOperationException so existing guards keep their meaning.
/// </summary>
public sealed class BusinessRuleException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public BusinessRuleException()
    {
    }

    /// <summary>Creates the exception with a user-facing message.</summary>
    public BusinessRuleException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a user-facing message and a cause.</summary>
    public BusinessRuleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}