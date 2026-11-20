using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Milkshake.Application.Notifications;
using Milkshake.Domain.Notifications;
using Milkshake.Infrastructure.Notifications;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M9 email providers (decision D3): Resend against a stubbed HTTP handler (request shape, idempotency key, outcome
/// mapping, nothing secret in errors) and the allow-list rule (D4). No network, no real email.
/// </summary>
public sealed class ResendEmailSenderTests
{
    private const string Key = "re_not_a_real_key_0123456789";

    private static EmailMessage Message(string key = "order-confirmed:42") =>
        new("thandi@example.test", "Thandi", "Order MS-7KQ2-X9TD confirmed", "<p>Hello</p>", "Hello", key, "order-confirmed:42");

    [Fact]
    public async Task Send_PostsTheEmailWithTheKeyAndIdempotencyKey_AndReturnsResendsId()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"id":"49a3999c-0ce1-4ea6-ab68-afcd6dc2e794"}"""));

        EmailSendResult result = await Sender(handler).SendAsync(Message("order-confirmed:42#resend-1"), CancellationToken.None);

        Assert.Equal((true, "49a3999c-0ce1-4ea6-ab68-afcd6dc2e794"), (result.Sent, result.ProviderMessageId));
        HttpRequestMessage sent = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Post, "https://api.resend.com/emails"), (sent.Method, sent.RequestUri!.AbsoluteUri));
        Assert.Equal(("Bearer", Key), (sent.Headers.Authorization!.Scheme, sent.Headers.Authorization.Parameter));
        Assert.Equal("order-confirmed:42#resend-1", Assert.Single(sent.Headers.GetValues("Idempotency-Key")));
        using JsonDocument body = JsonDocument.Parse(handler.Bodies.Single());
        Assert.Equal("Milky Shaky Custom Drinks <orders@example.test>", body.RootElement.GetProperty("from").GetString());
        Assert.Equal("thandi@example.test", body.RootElement.GetProperty("to")[0].GetString());
        Assert.Equal(1, body.RootElement.GetProperty("to").GetArrayLength());
        Assert.Equal("Order MS-7KQ2-X9TD confirmed", body.RootElement.GetProperty("subject").GetString());
        Assert.Equal(("<p>Hello</p>", "Hello"), (body.RootElement.GetProperty("html").GetString(), body.RootElement.GetProperty("text").GetString()));
        Assert.Equal("order-confirmed:42", body.RootElement.GetProperty("headers").GetProperty("X-Milkshake-Notification").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, DeliveryError.Rejected, true)]
    [InlineData(HttpStatusCode.Unauthorized, DeliveryError.Rejected, true)]
    [InlineData(HttpStatusCode.Forbidden, DeliveryError.Rejected, true)]
    [InlineData(HttpStatusCode.UnprocessableEntity, DeliveryError.Rejected, true)]
    [InlineData(HttpStatusCode.Conflict, DeliveryError.ProviderUnavailable, false)]
    [InlineData(HttpStatusCode.InternalServerError, DeliveryError.ProviderUnavailable, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, DeliveryError.ProviderUnavailable, false)]
    public async Task Refusals_AreClassified_AndNeverCarryResendsText(HttpStatusCode code, DeliveryError error, bool permanent)
    {
        var handler = new StubHandler(_ => Json(code, $$"""{"statusCode":{{(int)code}},"message":"secret detail {{Key}}","name":"x"}"""));

        EmailSendResult result = await Sender(handler).SendAsync(Message(), CancellationToken.None);

        Assert.Equal((false, error, permanent), (result.Sent, result.Error, result.IsPermanent));
        Assert.DoesNotContain(Key, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARateLimit_IsTemporary_AndRespectsRetryAfter()
    {
        var handler = new StubHandler(_ =>
        {
            HttpResponseMessage response = Json(HttpStatusCode.TooManyRequests, """{"name":"rate_limit_exceeded"}""");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        });

        EmailSendResult result = await Sender(handler).SendAsync(Message(), CancellationToken.None);

        Assert.Equal((DeliveryError.RateLimited, false, TimeSpan.FromSeconds(30)), (result.Error, result.IsPermanent, result.RetryAfter));
    }

    [Fact]
    public async Task ATimeout_IsTemporary_SoItIsRetriedWithTheSameKey()
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException("timed out"));

        EmailSendResult result = await Sender(handler).SendAsync(Message(), CancellationToken.None);

        Assert.Equal((DeliveryError.Timeout, false), (result.Error, result.IsPermanent));
    }

    [Fact]
    public async Task AnUnreachableProvider_IsTemporary()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("no route"));

        EmailSendResult result = await Sender(handler).SendAsync(Message(), CancellationToken.None);

        Assert.Equal((DeliveryError.ProviderUnavailable, false), (result.Error, result.IsPermanent));
    }

    [Fact]
    public async Task WithoutAKey_NothingIsSent()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"id":"x"}"""));
        ResendEmailSender sender = Sender(handler, key: null);

        EmailSendResult result = await sender.SendAsync(Message(), CancellationToken.None);

        Assert.False(sender.IsConfigured);
        Assert.True(sender.DeliversToRealInboxes);
        Assert.False(result.Sent);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task LineBreaksNeverReachTheSubject()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"id":"x"}"""));

        await Sender(handler).SendAsync(Message() with { Subject = "Order\r\nBcc: everyone@example.test" }, CancellationToken.None);

        using JsonDocument body = JsonDocument.Parse(handler.Bodies.Single());
        Assert.DoesNotContain('\n', body.RootElement.GetProperty("subject").GetString()!);
    }

    [Theory]
    [InlineData("thandi@example.test", true)]
    [InlineData("THANDI@Example.Test", true)]
    [InlineData("someone.else@example.test", false)]
    [InlineData("thandi@example.test.evil.test", false)]
    public void TheAllowList_MatchesWholeAddresses_IgnoringCase(string recipient, bool allowed)
    {
        var options = new EmailOptions();
        options.AllowedRecipients.Add("thandi@example.test");

        Assert.Equal(allowed, options.IsAllowed(recipient));
        Assert.False(new EmailOptions().IsAllowed("thandi@example.test"));
    }

    private static ResendEmailSender Sender(StubHandler handler, string? key = Key)
    {
        var options = new EmailOptions { Provider = EmailOptions.ResendProvider, FromAddress = "orders@example.test" };
        options.Resend.ApiKey = key;
        return new ResendEmailSender(new HttpClient(handler), Options.Create(options), NullLogger<ResendEmailSender>.Instance);
    }

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

/// <summary>
/// The Mailpit adapter against a real, throwaway Mailpit container (Docker, like the PostgreSQL tests): the email is
/// captured with its subject, recipient, HTML and text parts and the notification header, and an unreachable server
/// is a temporary failure.
/// </summary>
public sealed class MailpitEmailSenderTests : IAsyncLifetime
{
    private const ushort SmtpPort = 1025;
    private const ushort WebPort = 8025;

    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:latest")
        .WithPortBinding(SmtpPort, true)
        .WithPortBinding(WebPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(WebPort).ForPath("/api/v1/info")))
        .Build();

    public Task InitializeAsync() => _mailpit.StartAsync();

    public Task DisposeAsync() => _mailpit.DisposeAsync().AsTask();

    [Fact]
    public async Task AnEmail_IsCapturedByMailpit_WithBothPartsAndTheNotificationHeader()
    {
        MailpitEmailSender sender = Sender(_mailpit.GetMappedPublicPort(SmtpPort));

        EmailSendResult result = await sender.SendAsync(
            new EmailMessage("thandi@example.test", "Thandi", "Order MS-7KQ2-X9TD confirmed", "<p>Receipt <b>R29,90</b></p>", "Receipt R29,90",
                "order-confirmed:42", "order-confirmed:42"),
            CancellationToken.None);

        Assert.True(result.Sent);
        using var web = new HttpClient { BaseAddress = new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(WebPort)}/") };
        using JsonDocument list = JsonDocument.Parse(await web.GetStringAsync(new Uri("api/v1/messages", UriKind.Relative)));
        JsonElement captured = Assert.Single(list.RootElement.GetProperty("messages").EnumerateArray());
        Assert.Equal("Order MS-7KQ2-X9TD confirmed", captured.GetProperty("Subject").GetString());
        Assert.Equal("thandi@example.test", captured.GetProperty("To")[0].GetProperty("Address").GetString());

        string id = captured.GetProperty("ID").GetString()!;
        using JsonDocument message = JsonDocument.Parse(await web.GetStringAsync(new Uri($"api/v1/message/{id}", UriKind.Relative)));
        Assert.Contains("<b>R29,90</b>", message.RootElement.GetProperty("HTML").GetString(), StringComparison.Ordinal);
        Assert.Contains("Receipt R29,90", message.RootElement.GetProperty("Text").GetString(), StringComparison.Ordinal);
        using JsonDocument headers = JsonDocument.Parse(await web.GetStringAsync(new Uri($"api/v1/message/{id}/headers", UriKind.Relative)));
        Assert.Equal("order-confirmed:42", headers.RootElement.GetProperty("X-Milkshake-Notification")[0].GetString());
    }

    [Fact]
    public async Task AnUnreachableServer_IsATemporaryFailure()
    {
        MailpitEmailSender sender = Sender(port: 1);

        EmailSendResult result = await sender.SendAsync(
            new EmailMessage("thandi@example.test", "Thandi", "Subject", "<p>x</p>", "x", "k", "k"), CancellationToken.None);

        Assert.Equal((false, DeliveryError.ProviderUnavailable, false), (result.Sent, result.Error, result.IsPermanent));
    }

    private MailpitEmailSender Sender(int port)
    {
        var options = new EmailOptions { Provider = EmailOptions.MailpitProvider, FromAddress = "orders@example.test" };
        options.Mailpit.Host = _mailpit.Hostname;
        options.Mailpit.Port = port;
        options.Mailpit.TimeoutSeconds = 5;
        return new MailpitEmailSender(Options.Create(options), NullLogger<MailpitEmailSender>.Instance);
    }
}
