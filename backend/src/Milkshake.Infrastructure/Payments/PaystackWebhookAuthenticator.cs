using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Milkshake.Infrastructure.Payments;

public enum WebhookSignature
{
    Valid,
    Invalid,

    /// <summary>No secret key is configured: nothing can be verified, so nothing is accepted.</summary>
    NotConfigured,
}

/// <summary>
/// Paystack signs each webhook with HMAC-SHA512 over the raw request body, keyed with the secret key, hex-encoded in
/// the x-paystack-signature header. The comparison is constant-time so timing reveals nothing about the expected value.
/// </summary>
public sealed class PaystackWebhookAuthenticator(IOptions<PaystackOptions> options)
{
    public const string SignatureHeader = "x-paystack-signature";

    public WebhookSignature Check(ReadOnlySpan<byte> rawBody, string? signatureHex)
    {
        if (options.Value.SecretKey is not { Length: > 0 } secret)
        {
            return WebhookSignature.NotConfigured;
        }

        if (string.IsNullOrWhiteSpace(signatureHex) || signatureHex.Length != 128)
        {
            return WebhookSignature.Invalid;
        }

        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(signatureHex);
        }
        catch (FormatException)
        {
            return WebhookSignature.Invalid;
        }

        byte[] expected = HMACSHA512.HashData(System.Text.Encoding.UTF8.GetBytes(secret), rawBody);
        return CryptographicOperations.FixedTimeEquals(expected, supplied) ? WebhookSignature.Valid : WebhookSignature.Invalid;
    }
}
