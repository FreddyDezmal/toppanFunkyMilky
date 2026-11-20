using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Milkshake.Application.Payments;
using Milkshake.Domain.Payments;
using Milkshake.Infrastructure.Payments;

namespace Milkshake.IntegrationTests;

/// <summary>
/// The Paystack client against a stubbed HTTP handler (no network, no Docker): request shape (Bearer key, cents,
/// currency, our reference, return URL), status mapping, not-found handling and failures that never echo the key.
/// </summary>
public sealed class PaystackGatewayTests
{
    private const string Key = "sk_test_not_a_real_key_0123456789";

    [Fact]
    public async Task Initialize_SendsCentsCurrencyReferenceAndReturnUrl_WithTheSecretKey_AndReturnsTheCheckout()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"status":true,"message":"Authorization URL created","data":{"authorization_url":"https://checkout.paystack.com/abc123","access_code":"abc123","reference":"MSP-0123456789AB"}}"""));
        PaystackGateway gateway = Gateway(handler);

        Uri checkout = await gateway.CreateCheckoutAsync(
            new CheckoutRequest("MSP-0123456789AB", 5980, "ZAR", "thandi@example.test", new Uri("https://localhost:5173/orders/MS-7KQ2-X9TD?payment=return")),
            CancellationToken.None);

        Assert.Equal("https://checkout.paystack.com/abc123", checkout.AbsoluteUri);
        HttpRequestMessage sent = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Post, "https://api.paystack.co/transaction/initialize"), (sent.Method, sent.RequestUri!.AbsoluteUri));
        Assert.Equal(("Bearer", Key), (sent.Headers.Authorization!.Scheme, sent.Headers.Authorization.Parameter));
        using JsonDocument body = JsonDocument.Parse(handler.Bodies.Single());
        Assert.Equal(5980, body.RootElement.GetProperty("amount").GetInt64());
        Assert.Equal("ZAR", body.RootElement.GetProperty("currency").GetString());
        Assert.Equal("MSP-0123456789AB", body.RootElement.GetProperty("reference").GetString());
        Assert.Equal("https://localhost:5173/orders/MS-7KQ2-X9TD?payment=return", body.RootElement.GetProperty("callback_url").GetString());
        Assert.Equal("thandi@example.test", body.RootElement.GetProperty("email").GetString());
    }

    [Theory]
    [InlineData("success", VerifiedOutcome.Paid)]
    [InlineData("failed", VerifiedOutcome.Failed)]
    [InlineData("reversed", VerifiedOutcome.Failed)]
    [InlineData("abandoned", VerifiedOutcome.Abandoned)]
    [InlineData("ongoing", VerifiedOutcome.Open)]
    [InlineData("pending", VerifiedOutcome.Open)]
    public async Task Verify_MapsPaystackStatuses_AndReportsAmountCurrencyAndId(string status, VerifiedOutcome expected)
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"status":true,"message":"Verification successful","data":{"id":4242,"status":"STATUS","reference":"MSP-0123456789AB","amount":5980,"currency":"ZAR","gateway_response":"Approved","customer":{"email":"x@example.test"},"authorization":{"last4":"4081","bin":"408408"}}}""".Replace("STATUS", status, StringComparison.Ordinal)));

        GatewayVerification result = await Gateway(handler).VerifyAsync("MSP-0123456789AB", CancellationToken.None);

        Assert.Equal((true, expected, 5980L, "ZAR", (long?)4242), (result.Found, result.Outcome, result.AmountMinor, result.CurrencyCode, result.TransactionId));
        Assert.Equal("https://api.paystack.co/transaction/verify/MSP-0123456789AB", Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Verify_AnUnknownReference_IsNotFound(HttpStatusCode code)
    {
        var handler = new StubHandler(_ => Json(code, """{"status":false,"message":"Transaction reference not found"}"""));

        GatewayVerification result = await Gateway(handler).VerifyAsync("MSP-0123456789AB", CancellationToken.None);

        Assert.False(result.Found);
    }

    [Fact]
    public async Task Verify_ADifferentTransactionInTheResponse_IsRefused()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"status":true,"data":{"id":1,"status":"success","reference":"MSP-ZZZZZZZZZZZZ","amount":5980,"currency":"ZAR"}}"""));

        await Assert.ThrowsAsync<PaymentGatewayException>(() => Gateway(handler).VerifyAsync("MSP-0123456789AB", CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Failures_BecomeGatewayErrors_ThatNeverContainTheKey(HttpStatusCode code)
    {
        var handler = new StubHandler(_ => Json(code, """{"status":false,"message":"Invalid key"}"""));

        PaymentGatewayException failure = await Assert.ThrowsAsync<PaymentGatewayException>(() =>
            Gateway(handler).CreateCheckoutAsync(Request(), CancellationToken.None));

        Assert.DoesNotContain(Key, failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NetworkErrors_BecomeGatewayErrors()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<PaymentGatewayException>(() => Gateway(handler).VerifyAsync("MSP-0123456789AB", CancellationToken.None));
    }

    [Fact]
    public async Task WithoutASecretKey_NothingIsSent()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));

        await Assert.ThrowsAsync<PaymentGatewayException>(() => Gateway(handler, key: null).CreateCheckoutAsync(Request(), CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    private static CheckoutRequest Request() =>
        new("MSP-0123456789AB", 5980, "ZAR", "thandi@example.test", new Uri("https://localhost:5173/orders/MS-7KQ2-X9TD?payment=return"));

    private static PaystackGateway Gateway(StubHandler handler, string? key = Key) =>
        new(new HttpClient(handler), Options.Create(new PaystackOptions { SecretKey = key }), NullLogger<PaystackGateway>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }
}
