using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Milkshake.Api.Security;
using Milkshake.Application.Payments;
using Milkshake.Infrastructure.Payments;

namespace Milkshake.Api.Controllers;

/// <summary>
/// Paystack server-to-server notifications (M8, decision P5). Anonymous and CSRF-exempt by design – Paystack has no
/// session – so the HMAC-SHA512 signature over the raw body is the authentication; anything unsigned is refused before
/// it is parsed. The body is never trusted beyond identifiers: the transaction is re-verified with Paystack.
/// Responses carry no body. 200 tells Paystack to stop retrying; 401/503 make it retry (signature or availability).
/// </summary>
[ApiController]
[Route("api/payments/paystack")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[EnableRateLimiting(RateLimitPolicies.Webhook)]
public sealed partial class PaystackWebhookController(
    PaystackWebhookAuthenticator authenticator,
    IPaymentNotificationService notifications,
    ILogger<PaystackWebhookController> logger) : ControllerBase
{
    /// <summary>Paystack's notifications are small; anything larger is not one of them.</summary>
    public const int MaxBodyBytes = 64 * 1024;

    [HttpPost("webhook")]
    [RequestBodyLimit(MaxBodyBytes)]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, cancellationToken);
        byte[] body = buffer.ToArray();

        string? signature = Request.Headers[PaystackWebhookAuthenticator.SignatureHeader].ToString();
        switch (authenticator.Check(body, signature))
        {
            case WebhookSignature.NotConfigured:
                LogNotConfigured(logger);
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            case WebhookSignature.Invalid:
                LogInvalidSignature(logger);
                return Unauthorized();
        }

        NotificationOutcome outcome;
        if (ParseRefund(body) is { } refund)
        {
            outcome = await notifications.HandleRefundAsync(refund, cancellationToken);
        }
        else if (Parse(body) is { } notification)
        {
            outcome = await notifications.HandleAsync(notification, cancellationToken);
        }
        else
        {
            return BadRequest();
        }

        return outcome == NotificationOutcome.RetryLater ? StatusCode(StatusCodes.Status503ServiceUnavailable) : Ok();
    }

    /// <summary>Reads only the event type, transaction id and reference; everything else in the payload is ignored.</summary>
    private static PaymentNotification? Parse(byte[] body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("event", out JsonElement type) && type.ValueKind == JsonValueKind.String &&
                root.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("id", out JsonElement id) && id.TryGetInt64(out long transactionId) &&
                data.TryGetProperty("reference", out JsonElement reference) && reference.ValueKind == JsonValueKind.String &&
                type.GetString() is { Length: > 0 and <= 50 } eventType && reference.GetString() is { Length: > 0 } referenceValue)
            {
                return new PaymentNotification(eventType, transactionId, referenceValue);
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <summary>
    /// A refund.* event (M8b): only our refund reference and the transaction reference are read; Paystack's sample
    /// payloads carry no refund id. Null when the body is not a refund event with at least one reference.
    /// </summary>
    private static RefundNotification? ParseRefund(byte[] body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("event", out JsonElement type) && type.ValueKind == JsonValueKind.String &&
                type.GetString() is { Length: > 0 and <= 50 } eventType && eventType.StartsWith("refund.", StringComparison.Ordinal) &&
                root.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Object)
            {
                string? refundReference = Text(data, "refund_reference");
                string? transactionReference = Text(data, "transaction_reference");
                return refundReference is null && transactionReference is null
                    ? null
                    : new RefundNotification(eventType, refundReference, transactionReference);
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string? Text(JsonElement data, string name) =>
        data.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String &&
        value.GetString() is { Length: > 0 and <= 100 } text
            ? text
            : null;

    [LoggerMessage(Level = LogLevel.Error, Message = "A Paystack notification arrived but Payments:Paystack:SecretKey is not configured; it was refused so Paystack retries.")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a payment notification with a missing or invalid signature.")]
    private static partial void LogInvalidSignature(ILogger logger);
}
