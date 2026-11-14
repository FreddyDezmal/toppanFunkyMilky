namespace Milkshake.Domain.Auditing;

/// <summary>Kind of change recorded in the audit log.</summary>
public enum AuditAction
{
    /// <summary>A row was created.</summary>
    Insert,

    /// <summary>A row was changed.</summary>
    Update,

    /// <summary>A row was removed.</summary>
    Delete,
}

/// <summary>
/// One immutable audit record (URS §3.3, §4): who changed which record, from what to what, and when.
/// Written automatically on save; never updated or deleted by the application.
/// </summary>
public sealed class AuditLog
{
    private AuditLog()
    {
    }

    /// <summary>Creates an audit record.</summary>
    public AuditLog(
        DateTimeOffset occurredAt,
        AuditAction action,
        string tableName,
        string recordId,
        string changes,
        Guid? userId,
        string? userEmail,
        string? traceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);
        ArgumentException.ThrowIfNullOrWhiteSpace(changes);

        OccurredAt = occurredAt;
        Action = action;
        TableName = tableName;
        RecordId = recordId;
        Changes = changes;
        UserId = userId;
        UserEmail = userEmail;
        TraceId = traceId;
    }

    /// <summary>Database identifier.</summary>
    public long Id { get; private set; }

    /// <summary>When the change was saved (UTC).</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Insert, Update or Delete.</summary>
    public AuditAction Action { get; private set; }

    /// <summary>Database table that changed, e.g. "flavour_prices".</summary>
    public string TableName { get; private set; } = string.Empty;

    /// <summary>Primary key of the changed row, as text.</summary>
    public string RecordId { get; private set; } = string.Empty;

    /// <summary>JSON array of {"field","old","new"} entries.</summary>
    public string Changes { get; private set; } = "[]";

    /// <summary>Signed-in user who made the change; null for system changes such as seeding.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>Email at the time of the change, kept even if the user is later renamed.</summary>
    public string? UserEmail { get; private set; }

    /// <summary>Request trace id, to correlate with API logs.</summary>
    public string? TraceId { get; private set; }
}