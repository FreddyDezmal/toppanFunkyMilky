using System.Collections.Concurrent;
using Milkshake.Application.Notifications;

namespace Milkshake.IntegrationTests.Infrastructure;

/// <summary>An email sender for tests: records every attempt and answers as the test decides (success by default).</summary>
internal sealed class FakeEmailSender : IEmailSender
{
    public string Name => "Fake";

    public bool IsConfigured { get; set; } = true;

    public bool DeliversToRealInboxes { get; set; }

    /// <summary>The answer for each attempt; may throw to simulate an unexpected sender failure.</summary>
    public Func<EmailMessage, EmailSendResult> Respond { get; set; } = message => EmailSendResult.Success("fake-" + message.IdempotencyKey);

    public ConcurrentQueue<EmailMessage> Attempts { get; } = new();

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Attempts.Enqueue(message);
        return Task.FromResult(Respond(message));
    }

    public int AttemptsFor(string notificationKey) => Attempts.Count(m => m.NotificationKey == notificationKey);
}
