namespace Milkshake.Domain.Auditing;

/// <summary>Kinds of security-relevant events that change no data and so are not in the audit log.</summary>
public enum SecurityEventType
{
    /// <summary>Correct email and password.</summary>
    LoginSucceeded,

    /// <summary>Wrong password or unknown email.</summary>
    LoginFailed,

    /// <summary>Sign-in refused because the account is locked.</summary>
    LoginLockedOut,

    /// <summary>The user signed out.</summary>
    Logout,

    /// <summary>A manager account was created by the provisioning process (Step 6).</summary>
    ManagerProvisioned,

    /// <summary>A manager downloaded a report or audit export (M12); which one is in the server log under the same trace id.</summary>
    ReportExported,
}

/// <summary>
/// One security event (URS §3.3: full audit of transactions). Never contains passwords, cookies or tokens.
/// </summary>
public sealed class SecurityEvent
{
    /// <summary>Maximum stored email length.</summary>
    public const int EmailMaxLength = 256;

    /// <summary>Maximum stored IP address length (IPv6).</summary>
    public const int IpAddressMaxLength = 45;

    /// <summary>Maximum stored trace id length.</summary>
    public const int TraceIdMaxLength = 64;

    private SecurityEvent()
    {
    }

    /// <summary>Creates an event; over-long values are truncated.</summary>
    public SecurityEvent(
        DateTimeOffset occurredAt,
        SecurityEventType eventType,
        Guid? userId,
        string? email,
        string? ipAddress,
        string? traceId)
    {
        OccurredAt = occurredAt;
        EventType = eventType;
        UserId = userId;
        Email = Truncate(email, EmailMaxLength);
        IpAddress = Truncate(ipAddress, IpAddressMaxLength);
        TraceId = Truncate(traceId, TraceIdMaxLength);
    }

    /// <summary>Database identifier.</summary>
    public long Id { get; private set; }

    /// <summary>When the event happened (UTC).</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>What happened.</summary>
    public SecurityEventType EventType { get; private set; }

    /// <summary>The account involved, when it exists.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>The email address used (also recorded for unknown accounts, to spot guessing).</summary>
    public string? Email { get; private set; }

    /// <summary>Client IP address as seen by the API.</summary>
    public string? IpAddress { get; private set; }

    /// <summary>Request trace id, to correlate with API logs.</summary>
    public string? TraceId { get; private set; }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}