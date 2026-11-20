using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Milkshake.Domain.Orders;

/// <summary>
/// Public order reference such as <c>MS-7KQ2-X9TD</c> (M7 decision O4): 8 random Crockford base-32 characters
/// (no I, L, O or U), so references are not sequential and cannot be guessed from another order. Uniqueness is
/// enforced by a database index; the caller retries on the (very unlikely) collision.
/// </summary>
public static partial class OrderReference
{
    public const int Length = 12;

    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>The same pattern as the database check constraint <c>ck_orders_reference</c>.</summary>
    public const string Pattern = "^MS-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$";

    public static string New()
    {
        Span<char> chars = stackalloc char[8];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"MS-{chars[..4]}-{chars[4..]}";
    }

    public static bool IsValid(string? value) => value is not null && ReferenceRegex().IsMatch(value);

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceRegex();
}
