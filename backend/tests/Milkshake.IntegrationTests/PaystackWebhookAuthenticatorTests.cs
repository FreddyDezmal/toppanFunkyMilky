using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Milkshake.Infrastructure.Payments;

namespace Milkshake.IntegrationTests;

/// <summary>Decision P5: HMAC-SHA512 of the exact raw body with the secret key; anything else is refused.</summary>
public sealed class PaystackWebhookAuthenticatorTests
{
    private const string Key = "sk_test_signing_key_for_tests_only";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"event":"charge.success","data":{"id":1,"reference":"MSP-0123456789AB"}}""");

    private static PaystackWebhookAuthenticator Authenticator(string? key = Key) => new(Options.Create(new PaystackOptions { SecretKey = key }));

    public static string Sign(byte[] body, string key) => Convert.ToHexStringLower(HMACSHA512.HashData(Encoding.UTF8.GetBytes(key), body));

    [Fact]
    public void TheCorrectSignature_IsValid_InEitherHexCase()
    {
        Assert.Equal(WebhookSignature.Valid, Authenticator().Check(Body, Sign(Body, Key)));
        Assert.Equal(WebhookSignature.Valid, Authenticator().Check(Body, Sign(Body, Key).ToUpperInvariant()));
    }

    [Fact]
    public void AChangedBody_AnotherKey_OrAMalformedSignature_IsInvalid()
    {
        byte[] tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Body).Replace("\"id\":1", "\"id\":2", StringComparison.Ordinal));

        Assert.Equal(WebhookSignature.Invalid, Authenticator().Check(tampered, Sign(Body, Key)));
        Assert.Equal(WebhookSignature.Invalid, Authenticator().Check(Body, Sign(Body, "sk_test_someone_else")));
        Assert.Equal(WebhookSignature.Invalid, Authenticator().Check(Body, null));
        Assert.Equal(WebhookSignature.Invalid, Authenticator().Check(Body, ""));
        Assert.Equal(WebhookSignature.Invalid, Authenticator().Check(Body, new string('z', 128)));
        Assert.Equal(WebhookSignature.Invalid, Authenticator().Check(Body, Sign(Body, Key)[..64]));
    }

    [Fact]
    public void WithoutAKey_NothingIsAccepted()
    {
        Assert.Equal(WebhookSignature.NotConfigured, Authenticator(key: null).Check(Body, Sign(Body, Key)));
    }
}
