using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Milkshake.Application.Payments;
using Milkshake.Domain.Payments;

namespace Milkshake.Infrastructure.Payments;

/// <summary>
/// Paystack settings (section "Payments:Paystack"). The secret key comes from user secrets in development and from
/// environment variables / a secrets store elsewhere; it is never in appsettings, the repository or the frontend.
/// </summary>
public sealed class PaystackOptions
{
    public const string SectionName = "Payments:Paystack";

    /// <summary>sk_test_… in development. Missing → payments are unavailable (503), the rest of the app still works.</summary>
    public string? SecretKey { get; set; }

    public Uri BaseUrl { get; set; } = new("https://api.paystack.co/");

    /// <summary>The only checkout host patrons are sent to (decision P8).</summary>
    public string CheckoutHost { get; set; } = "checkout.paystack.com";

    /// <summary>The frontend origin Paystack returns the patron to, e.g. http://localhost:5173 in development.</summary>
    public Uri? ReturnBaseUrl { get; set; }

    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>
/// Paystack Transaction API: initialize (hosted checkout) and verify. Amounts are whole minor units (cents).
/// Nothing from a response is trusted without checks: the checkout URL is validated by the domain, and verification
/// results are compared with the stored payment by the caller. Logs never contain the key, e-mail or response bodies.
/// </summary>
public sealed partial class PaystackGateway(HttpClient http, IOptions<PaystackOptions> options, ILogger<PaystackGateway> logger) : IPaymentGateway
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Name => Payment.PaystackProvider;

    public string CheckoutHost => options.Value.CheckoutHost;

    public async Task<Uri> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = new InitializeBody(request.CustomerEmail, request.AmountMinor, request.CurrencyCode, request.Reference, request.CallbackUrl.AbsoluteUri);
        using HttpRequestMessage message = Authorized(HttpMethod.Post, "transaction/initialize");
        message.Content = JsonContent.Create(body, options: Json);

        Envelope<InitializeData> envelope = await SendAsync<InitializeData>(message, "initialize", cancellationToken);
        if (envelope.Data?.AuthorizationUrl is not { } url || !Uri.TryCreate(url, UriKind.Absolute, out Uri? checkout))
        {
            throw new PaymentGatewayException("Paystack did not return a checkout address.");
        }

        return checkout;
    }

    public async Task<GatewayVerification> VerifyAsync(string reference, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        using HttpRequestMessage message = Authorized(HttpMethod.Get, $"transaction/verify/{Uri.EscapeDataString(reference)}");
        Envelope<VerifyData> envelope;
        try
        {
            envelope = await SendAsync<VerifyData>(message, "verify", cancellationToken);
        }
        catch (TransactionNotFoundException)
        {
            return GatewayVerification.NotFound;
        }

        VerifyData data = envelope.Data ?? throw new PaymentGatewayException("Paystack returned no transaction data.");
        if (!string.Equals(data.Reference, reference, StringComparison.Ordinal))
        {
            throw new PaymentGatewayException("Paystack returned a different transaction.");
        }

        VerifiedOutcome outcome = data.Status switch
        {
            "success" => VerifiedOutcome.Paid,
            "failed" or "reversed" => VerifiedOutcome.Failed,
            "abandoned" => VerifiedOutcome.Abandoned,
            _ => VerifiedOutcome.Open, // ongoing, pending, processing, queued, …
        };

        return new GatewayVerification(true, outcome, data.Amount, data.Currency ?? string.Empty, data.Id, data.GatewayResponse);
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        string key = options.Value.SecretKey is { Length: > 0 } configured
            ? configured
            : throw new PaymentGatewayException("Payments are not configured (Payments:Paystack:SecretKey is missing).");

        var message = new HttpRequestMessage(method, new Uri(options.Value.BaseUrl, path));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return message;
    }

    private async Task<Envelope<T>> SendAsync<T>(HttpRequestMessage message, string operation, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogUnreachable(logger, operation, exception.GetType().Name);
            throw new PaymentGatewayException("Paystack could not be reached.", exception);
        }

        using (response)
        {
            if (operation == "verify" && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                throw new TransactionNotFoundException();
            }

            if (!response.IsSuccessStatusCode)
            {
                LogRejected(logger, operation, (int)response.StatusCode);
                throw new PaymentGatewayException($"Paystack rejected the {operation} request (HTTP {(int)response.StatusCode}).");
            }

            Envelope<T>? envelope;
            try
            {
                envelope = await response.Content.ReadFromJsonAsync<Envelope<T>>(Json, cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new PaymentGatewayException("Paystack returned an unreadable response.", exception);
            }

            return envelope is { Status: true }
                ? envelope
                : throw new PaymentGatewayException($"Paystack reported a failed {operation} request.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Paystack {Operation} request could not be sent ({ErrorType}).")]
    private static partial void LogUnreachable(ILogger logger, string operation, string errorType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Paystack rejected a {Operation} request with HTTP {StatusCode}.")]
    private static partial void LogRejected(ILogger logger, string operation, int statusCode);

    private sealed class TransactionNotFoundException : Exception;

    private sealed record InitializeBody(string Email, long Amount, string Currency, string Reference, string CallbackUrl);

    private sealed record Envelope<T>(bool Status, string? Message, T? Data);

    private sealed record InitializeData(string? AuthorizationUrl, string? AccessCode, string? Reference);

    private sealed record VerifyData(long? Id, string? Status, string? Reference, long Amount, string? Currency, string? GatewayResponse);
}
