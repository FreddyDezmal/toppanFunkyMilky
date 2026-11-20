using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Milkshake.Application.Notifications;
using Milkshake.Domain.Notifications;

namespace Milkshake.Infrastructure.Notifications;

/// <summary>
/// Email settings (section "Email"). Secrets (the Resend key) come from user secrets or the environment, never from
/// appsettings or the repository. <see cref="AllowedRecipients"/> is the demonstration-store safety net (decision D4):
/// only these addresses are ever sent to; every other email is marked suppressed.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";
    public const string MailpitProvider = "Mailpit";
    public const string ResendProvider = "Resend";

    /// <summary>"Mailpit", "Resend" or "None" (default: emails stay queued).</summary>
    public string Provider { get; set; } = "None";

    public string? FromAddress { get; set; }

    public string FromName { get; set; } = "Milky Shaky Custom Drinks";

    /// <summary>The only recipients email is sent to. Empty means none.</summary>
    public IList<string> AllowedRecipients { get; } = [];

    public MailpitSettings Mailpit { get; set; } = new();

    public ResendSettings Resend { get; set; } = new();

    /// <summary>
    /// The site address used for the one link in every email (its order page), e.g. https://shop.example/. Required to
    /// send: without it emails stay queued.
    /// </summary>
    public Uri? PublicBaseUrl { get; set; }

    public DispatchSettings Dispatch { get; set; } = new();

    /// <summary>Exact address match, ignoring letter case.</summary>
    public bool IsAllowed(string recipient) =>
        AllowedRecipients.Any(allowed => string.Equals(allowed.Trim(), recipient, StringComparison.OrdinalIgnoreCase));

    public sealed class MailpitSettings
    {
        public string Host { get; set; } = "localhost";

        public int Port { get; set; } = 1025;

        public int TimeoutSeconds { get; set; } = 15;
    }

    /// <summary>The background dispatcher (section "Email:Dispatch").</summary>
    public sealed class DispatchSettings
    {
        /// <summary>Switched off in automated tests, which call the dispatcher directly.</summary>
        public bool Enabled { get; set; } = true;

        public int IntervalSeconds { get; set; } = 30;

        /// <summary>Emails claimed per run. With the sender timeout this must fit inside the lease.</summary>
        public int BatchSize { get; set; } = 10;

        /// <summary>How long a claimed email is held before another run may take it back.</summary>
        public int LeaseMinutes { get; set; } = 5;
    }

    public sealed class ResendSettings
    {
        /// <summary>re_… – user secrets only.</summary>
        public string? ApiKey { get; set; }

        public Uri BaseUrl { get; set; } = new("https://api.resend.com/");

        public int TimeoutSeconds { get; set; } = 15;
    }
}

/// <summary>Header naming the notification (no personal data) so a captured or delivered email can be traced to its row.</summary>
internal static class EmailHeaders
{
    public const string NotificationKey = "X-Milkshake-Notification";

    /// <summary>"Name &lt;address&gt;" with anything that could break the header removed from the name.</summary>
    public static string Sender(string name, string address)
    {
        string safe = new string(name.Where(c => !char.IsControl(c) && c is not ('<' or '>' or '"' or ',')).ToArray()).Trim();
        return safe.Length == 0 ? address : $"{safe} <{address}>";
    }

    /// <summary>Subjects are ours, but never let a line break reach a header.</summary>
    public static string SafeSubject(string subject) =>
        new(subject.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
}

/// <summary>No provider configured: nothing is sent and emails stay queued.</summary>
internal sealed class DisabledEmailSender : IEmailSender
{
    public string Name => "None";

    public bool IsConfigured => false;

    public bool DeliversToRealInboxes => false;

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
        Task.FromResult(EmailSendResult.Temporary(DeliveryError.ProviderUnavailable));
}

/// <summary>
/// Local capture through Mailpit's SMTP port (decision D3): nothing reaches a real inbox; captured mail is viewed at
/// http://localhost:8025. Plain SMTP without credentials, for development and automated tests only.
/// </summary>
public sealed partial class MailpitEmailSender(IOptions<EmailOptions> options, ILogger<MailpitEmailSender> logger) : IEmailSender
{
    public string Name => EmailOptions.MailpitProvider;

    public bool IsConfigured => options.Value.FromAddress is { Length: > 0 };

    public bool DeliversToRealInboxes => false;

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        EmailOptions settings = options.Value;

        using var mail = new MailMessage
        {
            From = new MailAddress(settings.FromAddress!, settings.FromName),
            Subject = EmailHeaders.SafeSubject(message.Subject),
            SubjectEncoding = Encoding.UTF8,
            Body = message.TextBody,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false,
        };
        mail.To.Add(new MailAddress(message.To, message.ToName));
        mail.Headers.Add(EmailHeaders.NotificationKey, message.NotificationKey);
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, Encoding.UTF8, "text/html"));

        using var client = new SmtpClient(settings.Mailpit.Host, settings.Mailpit.Port)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            EnableSsl = false,
            Timeout = settings.Mailpit.TimeoutSeconds * 1000,
        };

        try
        {
            await client.SendMailAsync(mail, cancellationToken);
            return EmailSendResult.Success(null);
        }
        catch (SmtpFailedRecipientException)
        {
            return EmailSendResult.Permanent(DeliveryError.InvalidRecipient);
        }
        catch (Exception exception) when (exception is SmtpException or IOException or SocketException && !cancellationToken.IsCancellationRequested)
        {
            LogUnavailable(logger, message.NotificationKey, exception.GetType().Name);
            return EmailSendResult.Temporary(DeliveryError.ProviderUnavailable);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {NotificationKey} could not be handed to Mailpit ({ErrorType}); it will be retried.")]
    private static partial void LogUnavailable(ILogger logger, string notificationKey, string errorType);
}

/// <summary>
/// Resend's Email API (decision D3), for manual checks in real mail clients. Our idempotency key is sent so a retry
/// after an uncertain outcome is not sent twice (Resend remembers keys for 24 hours). Logs never contain the key,
/// addresses or response bodies.
/// </summary>
public sealed partial class ResendEmailSender(HttpClient http, IOptions<EmailOptions> options, ILogger<ResendEmailSender> logger) : IEmailSender
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => EmailOptions.ResendProvider;

    public bool IsConfigured => options.Value.Resend.ApiKey is { Length: > 0 } && options.Value.FromAddress is { Length: > 0 };

    public bool DeliversToRealInboxes => true;

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        EmailOptions settings = options.Value;
        if (!IsConfigured)
        {
            return EmailSendResult.Temporary(DeliveryError.ProviderUnavailable);
        }

        var body = new SendBody(
            EmailHeaders.Sender(settings.FromName, settings.FromAddress!),
            [message.To],
            EmailHeaders.SafeSubject(message.Subject),
            message.HtmlBody,
            message.TextBody,
            new Dictionary<string, string> { [EmailHeaders.NotificationKey] = message.NotificationKey });

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.Resend.BaseUrl, "emails"))
        {
            Content = JsonContent.Create(body, options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Resend.ApiKey);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", message.IdempotencyKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The email may or may not have been accepted: retried later with the same idempotency key.
            LogTrouble(logger, message.NotificationKey, "timeout");
            return EmailSendResult.Temporary(DeliveryError.Timeout);
        }
        catch (HttpRequestException)
        {
            LogTrouble(logger, message.NotificationKey, "unreachable");
            return EmailSendResult.Temporary(DeliveryError.ProviderUnavailable);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                string? id = null;
                try
                {
                    id = (await response.Content.ReadFromJsonAsync<SendResponse>(Json, cancellationToken))?.Id;
                }
                catch (JsonException)
                {
                    // Accepted; only the id is unreadable.
                }

                return EmailSendResult.Success(id);
            }

            LogRejected(logger, message.NotificationKey, (int)response.StatusCode);
            return response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => EmailSendResult.Temporary(DeliveryError.RateLimited, RetryAfter(response)),

                // 409: the same key is still being processed (or was used with different content); try again later.
                HttpStatusCode.Conflict => EmailSendResult.Temporary(DeliveryError.ProviderUnavailable),
                >= HttpStatusCode.InternalServerError => EmailSendResult.Temporary(DeliveryError.ProviderUnavailable),

                // 400/422 invalid request, 401 bad key, 403 sender domain not verified or recipient not allowed.
                _ => EmailSendResult.Permanent(DeliveryError.Rejected),
            };
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        return response.Headers.TryGetValues("retry-after", out IEnumerable<string>? values) &&
               int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {NotificationKey} could not be sent through Resend ({Problem}); it will be retried.")]
    private static partial void LogTrouble(ILogger logger, string notificationKey, string problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Resend refused email {NotificationKey} with HTTP {StatusCode}.")]
    private static partial void LogRejected(ILogger logger, string notificationKey, int statusCode);

    private sealed record SendBody(string From, string[] To, string Subject, string Html, string Text, Dictionary<string, string> Headers);

    private sealed record SendResponse(string? Id);
}
