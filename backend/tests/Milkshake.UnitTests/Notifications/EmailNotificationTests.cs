using Milkshake.Domain.Common;
using Milkshake.Domain.Notifications;

namespace Milkshake.UnitTests.Notifications;

/// <summary>M9 outbox rules (D2, D4, D6, D7): one key per event, safe recipients, claim and lease, backoff, final failure, resend.</summary>
public sealed class EmailNotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    private static EmailNotification Create(string email = "thandi@example.test") =>
        EmailNotification.Create(NotificationKind.OrderConfirmed, EmailNotification.KeyFor(NotificationKind.OrderConfirmed, 42), 42, null, null,
            email, "Thandi", 1, "{\"reference\":\"MS-7KQ2-X9TD\"}", Now);

    private static EmailNotification Claimed()
    {
        EmailNotification email = Create();
        email.Claim(Now, Lease);
        return email;
    }

    [Fact]
    public void Create_StartsPendingAndDueNow()
    {
        EmailNotification email = Create();

        Assert.Equal((NotificationStatus.Pending, 0, Now, "order-confirmed:42"), (email.Status, email.AttemptCount, email.NextAttemptAt, email.NotificationKey));
        Assert.True(email.CanBeClaimedAt(Now));
        Assert.Equal("order-confirmed:42", email.IdempotencyKey);
    }

    [Theory]
    [InlineData(NotificationKind.OrderConfirmed, "order-confirmed:7")]
    [InlineData(NotificationKind.CancellationRequested, "cancellation-requested:7")]
    [InlineData(NotificationKind.OrderCancelled, "order-cancelled:7")]
    [InlineData(NotificationKind.RefundProcessed, "refund-processed:7")]
    [InlineData(NotificationKind.RefundFailed, "refund-failed:7")]
    public void Keys_NameTheEventAndItsRecord(NotificationKind kind, string expected)
    {
        Assert.Equal(expected, EmailNotification.KeyFor(kind, 7));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("Thandi <thandi@example.test>")]
    [InlineData("thandi@example.test\r\nBcc: everyone@example.test")]
    [InlineData("thandi@example.test, other@example.test")]
    public void Create_RefusesAnythingButOnePlainAddress(string email)
    {
        Assert.False(EmailNotification.IsValidRecipient(email));
        Assert.Throws<BusinessRuleException>(() => Create(email));
    }

    [Fact]
    public void Create_RefusesANameWithLineBreaks()
    {
        Assert.Throws<BusinessRuleException>(() => EmailNotification.Create(NotificationKind.OrderConfirmed, "order-confirmed:1", 1, null, null,
            "thandi@example.test", "Thandi\r\nBcc: x@example.test", 1, "{}", Now));
    }

    [Fact]
    public void Claim_TakesItForOneAttempt_AndAnotherDispatcherCannotTakeItWhileTheLeaseLasts()
    {
        EmailNotification email = Claimed();

        Assert.Equal((NotificationStatus.Sending, 1, Now + Lease), (email.Status, email.AttemptCount, email.LockedUntil));
        Assert.False(email.CanBeClaimedAt(Now + Lease - TimeSpan.FromSeconds(1)));
        Assert.Throws<BusinessRuleException>(() => email.Claim(Now, Lease));
    }

    [Fact]
    public void AnExpiredLease_LetsTheEmailBeClaimedAgain_AfterACrash()
    {
        EmailNotification email = Claimed();

        email.Claim(Now + Lease, Lease);

        Assert.Equal((NotificationStatus.Sending, 2), (email.Status, email.AttemptCount));
    }

    [Fact]
    public void Sent_IsFinal_AndRecordsTheProvidersId()
    {
        EmailNotification email = Claimed();

        email.MarkSent("Mailpit", "abc-123", Now);

        Assert.Equal((NotificationStatus.Sent, "Mailpit", "abc-123", Now), (email.Status, email.Provider, email.ProviderMessageId, email.SentAt));
        Assert.Null(email.LockedUntil);
        Assert.False(email.CanBeClaimedAt(Now.AddDays(1)));
        Assert.Throws<BusinessRuleException>(() => email.MarkSent("Mailpit", null, Now));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(6, 32)]
    [InlineData(7, 60)]
    [InlineData(20, 60)]
    public void Backoff_DoublesFromOneMinute_UpToAnHour(int attempts, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), EmailNotification.Backoff(attempts));
    }

    [Fact]
    public void ATemporaryFailure_WaitsAndIsTriedAgain()
    {
        EmailNotification email = Claimed();

        email.MarkAttemptFailed(DeliveryError.ProviderUnavailable, permanent: false, Now);

        Assert.Equal((NotificationStatus.Pending, Now.AddMinutes(1), DeliveryError.ProviderUnavailable), (email.Status, email.NextAttemptAt, email.LastError));
        Assert.False(email.CanBeClaimedAt(Now));
        Assert.True(email.CanBeClaimedAt(Now.AddMinutes(1)));
    }

    [Fact]
    public void ARateLimit_WaitsAsLongAsTheProviderAsks_WithinTheMaximum()
    {
        EmailNotification first = Claimed();
        first.MarkAttemptFailed(DeliveryError.RateLimited, permanent: false, Now, TimeSpan.FromMinutes(10));
        EmailNotification second = Claimed();
        second.MarkAttemptFailed(DeliveryError.RateLimited, permanent: false, Now, TimeSpan.FromHours(5));

        Assert.Equal((Now.AddMinutes(10), Now.AddMinutes(60)), (first.NextAttemptAt, second.NextAttemptAt));
    }

    [Fact]
    public void APermanentFailure_EndsAtOnce()
    {
        EmailNotification email = Claimed();

        email.MarkAttemptFailed(DeliveryError.InvalidRecipient, permanent: true, Now);

        Assert.Equal((NotificationStatus.Failed, DeliveryError.InvalidRecipient), (email.Status, email.LastError));
        Assert.False(email.CanBeClaimedAt(Now.AddDays(1)));
    }

    [Fact]
    public void AfterTheLastAllowedAttempt_ATemporaryFailureBecomesFinal()
    {
        EmailNotification email = Create();
        DateTimeOffset at = Now;
        for (int attempt = 1; attempt < EmailNotification.MaxAttempts; attempt++)
        {
            email.Claim(at, Lease);
            email.MarkAttemptFailed(DeliveryError.Timeout, permanent: false, at);
            at = email.NextAttemptAt;
        }

        email.Claim(at, Lease);
        email.MarkAttemptFailed(DeliveryError.Timeout, permanent: false, at);

        Assert.Equal((NotificationStatus.Failed, EmailNotification.MaxAttempts), (email.Status, email.AttemptCount));
    }

    [Fact]
    public void ARecipientNotOnTheAllowList_IsSuppressed_AndNeverSent()
    {
        EmailNotification email = Claimed();

        email.MarkSuppressed();

        Assert.Equal(NotificationStatus.Suppressed, email.Status);
        Assert.False(email.CanBeClaimedAt(Now.AddDays(1)));
        Assert.Throws<BusinessRuleException>(() => email.Resend(Now));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AResend_StartsFreshAttempts_WithANewIdempotencyKey(bool wasSent)
    {
        EmailNotification email = Claimed();
        if (wasSent) { email.MarkSent("Resend", "re_1", Now); } else { email.MarkAttemptFailed(DeliveryError.Rejected, true, Now); }

        email.Resend(Now.AddHours(1));
        email.Claim(Now.AddHours(1), Lease);
        email.MarkSent("Resend", "re_2", Now.AddHours(1));
        email.Resend(Now.AddHours(2));

        Assert.Equal((NotificationStatus.Pending, 0, 2, Now.AddHours(2)), (email.Status, email.AttemptCount, email.ResendCount, email.NextAttemptAt));
        Assert.Equal("order-confirmed:42#resend-2", email.IdempotencyKey);
        Assert.Null(email.SentAt);
        Assert.Null(email.LastError);
    }

    [Fact]
    public void PendingOrSendingEmails_CannotBeResent()
    {
        Assert.Throws<BusinessRuleException>(() => Create().Resend(Now));
        Assert.Throws<BusinessRuleException>(() => Claimed().Resend(Now));
    }
}
