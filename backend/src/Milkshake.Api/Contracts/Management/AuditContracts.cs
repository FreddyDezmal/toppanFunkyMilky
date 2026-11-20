using System.Text.Json;
using Milkshake.Application.Management;

namespace Milkshake.Api.Contracts.Management;

/// <summary>One changed field: values as stored (JSON), personal data masked.</summary>
public sealed record AuditFieldChangeResponse(string Field, JsonElement? Old, JsonElement? New);

/// <summary>One audit record (AC-11.1): who, what, old, new, action and when.</summary>
public sealed record AuditChangeResponse(
    long Id,
    DateTimeOffset OccurredAt,
    string Action,
    string Entity,
    string RecordId,
    string? Actor,
    Guid? UserId,
    string? User,
    IReadOnlyList<AuditFieldChangeResponse> Changes,
    string? TraceId)
{
    public static AuditChangeResponse From(AuditEntryView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new AuditChangeResponse(view.Id, view.OccurredAt, view.Action.ToString(), view.Entity, view.RecordId, view.Actor,
            view.UserId, view.User, view.Changes.Select(c => new AuditFieldChangeResponse(c.Field, c.Old, c.New)).ToList(), view.TraceId);
    }
}

/// <summary>One sign-in, sign-out or lockout event (T11.3), with the address and IP masked except for managers' addresses.</summary>
public sealed record SecurityEventResponse(long Id, DateTimeOffset OccurredAt, string EventType, Guid? UserId, string? User, string? IpAddress)
{
    public static SecurityEventResponse From(SecurityEventView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new SecurityEventResponse(view.Id, view.OccurredAt, view.EventType.ToString(), view.UserId, view.User, view.IpAddress);
    }
}

/// <summary>A page, newest first. Pass <see cref="NextCursor"/> as <c>cursor</c> for the next page; null at the end.</summary>
public sealed record AuditPageResponse<T>(IReadOnlyList<T> Items, string? NextCursor);

public static class AuditMessages
{
    public const string UnknownEntity = "No audited table has this name.";
}
