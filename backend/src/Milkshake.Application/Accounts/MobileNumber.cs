using System.Text.RegularExpressions;

namespace Milkshake.Application.Accounts;

/// <summary>
/// Mobile numbers are stored in international E.164 format, e.g. +27821234567.
/// Spaces, hyphens and brackets are accepted on input and removed.
/// </summary>
public static partial class MobileNumber
{
    /// <summary>Message shown when a number cannot be accepted.</summary>
    public const string InvalidMessage = "Enter your mobile number in international format, e.g. +27 82 123 4567.";

    /// <summary>Returns the number in E.164 format, or null if it is not a valid international number.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string compact = new(value.Where(c => !char.IsWhiteSpace(c) && c is not '-' and not '(' and not ')').ToArray());
        return E164().IsMatch(compact) ? compact : null;
    }

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164();
}