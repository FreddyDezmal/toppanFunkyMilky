using System.Text;
using Microsoft.EntityFrameworkCore;
using Milkshake.Application.Management;
using Milkshake.Domain.Auditing;
using Milkshake.Domain.Users;
using Milkshake.Infrastructure.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace Milkshake.Infrastructure.Management;

/// <summary>
/// Management → audit history (M11, AC-11.4): read-only, newest first, keyset paging on (occurred_at, id) using the
/// descending indexes, every filter value passed as a parameter. Statements are assembled only from the fixed
/// fragments below. Personal data is masked on output (<see cref="AuditMasking"/>); free text never searches inside
/// account changes, so masked values cannot be found by guessing them.
/// </summary>
internal sealed class AuditQueryService(MilkshakeDbContext db) : IAuditQueryService
{
    public async Task<AuditPage<AuditEntryView>?> ChangesAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Entity is { } entity && !AuditedTables().Contains(entity))
        {
            return null;
        }

        // Result columns in snake_case: the model's naming convention also maps these row types.
        var sql = new StringBuilder("""
            SELECT a.id, a.occurred_at, a.action, a.table_name AS entity, a.record_id, a.actor, a.user_id, a.user_email,
                   a.changes::text AS changes, a.trace_id
            FROM milkshake.audit_logs AS a
            WHERE TRUE
            """);
        var parameters = new List<NpgsqlParameter>();
        AddRange(sql, parameters, "a", query.From, query.To);
        AddUser(sql, parameters, "a", "user_email", query.User);
        if (query.Actor is { } actor)
        {
            sql.Append(" AND a.actor = @actor");
            parameters.Add(new NpgsqlParameter("actor", actor));
        }

        if (query.Entity is { } table)
        {
            sql.Append(" AND a.table_name = @entity");
            parameters.Add(new NpgsqlParameter("entity", table));
        }

        if (query.Action is { } action)
        {
            sql.Append(" AND a.action = @action");
            parameters.Add(new NpgsqlParameter("action", action.ToString()));
        }

        if (query.Text is { } text)
        {
            sql.Append("""
                 AND (a.table_name ILIKE @text ESCAPE '\' OR a.record_id ILIKE @text ESCAPE '\'
                      OR (a.table_name <> ALL (@personal) AND a.changes::text ILIKE @text ESCAPE '\'))
                """);
            parameters.Add(new NpgsqlParameter("text", "%" + EscapeLike(text) + "%"));
            parameters.Add(new NpgsqlParameter("personal", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = AuditMasking.PersonalTables.ToArray() });
        }

        AddPage(sql, parameters, "a", query.After, query.Limit);

        List<ChangeRow> rows = await db.Database.SqlQueryRaw<ChangeRow>(sql.ToString(), [.. parameters]).ToListAsync(cancellationToken);
        bool more = rows.Count > query.Limit;
        if (more)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        HashSet<Guid> managers = await ManagersAsync(rows.Select(r => r.UserId), cancellationToken);
        List<AuditEntryView> items = rows.Select(r => new AuditEntryView(
                r.Id,
                r.OccurredAt,
                Enum.Parse<AuditAction>(r.Action),
                r.Entity,
                r.RecordId,
                r.Actor,
                r.UserId,
                DisplayUser(r.UserId, r.UserEmail, managers),
                AuditMasking.Changes(r.Entity, r.Changes),
                r.TraceId))
            .ToList();
        return new AuditPage<AuditEntryView>(items, more ? new AuditCursor(items[^1].OccurredAt, items[^1].Id).Encode() : null);
    }

    public async Task<AuditPage<SecurityEventView>> SecurityEventsAsync(SecurityEventQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var sql = new StringBuilder("""
            SELECT s.id, s.occurred_at, s.event_type, s.user_id, s.email, s.ip_address
            FROM milkshake.security_events AS s
            WHERE TRUE
            """);
        var parameters = new List<NpgsqlParameter>();
        AddRange(sql, parameters, "s", query.From, query.To);
        AddUser(sql, parameters, "s", "email", query.User);
        if (query.EventType is { } type)
        {
            sql.Append(" AND s.event_type = @eventType");
            parameters.Add(new NpgsqlParameter("eventType", type.ToString()));
        }

        AddPage(sql, parameters, "s", query.After, query.Limit);

        List<SecurityRow> rows = await db.Database.SqlQueryRaw<SecurityRow>(sql.ToString(), [.. parameters]).ToListAsync(cancellationToken);
        bool more = rows.Count > query.Limit;
        if (more)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        HashSet<Guid> managers = await ManagersAsync(rows.Select(r => r.UserId), cancellationToken);
        List<SecurityEventView> items = rows.Select(r => new SecurityEventView(
                r.Id,
                r.OccurredAt,
                Enum.Parse<SecurityEventType>(r.EventType),
                r.UserId,
                DisplayUser(r.UserId, r.Email, managers),
                r.IpAddress is null ? null : AuditMasking.MaskIp(r.IpAddress)))
            .ToList();
        return new AuditPage<SecurityEventView>(items, more ? new AuditCursor(items[^1].OccurredAt, items[^1].Id).Encode() : null);
    }

    private HashSet<string> AuditedTables() =>
        new(db.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(t => t is not null && t is not ("audit_logs" or "security_events"))
            .Select(t => t!), StringComparer.Ordinal);

    private static void AddRange(StringBuilder sql, List<NpgsqlParameter> parameters, string alias, DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from is { } start)
        {
            sql.Append(" AND ").Append(alias).Append(".occurred_at >= @from");
            parameters.Add(new NpgsqlParameter("from", start.ToUniversalTime()));
        }

        if (to is { } end)
        {
            sql.Append(" AND ").Append(alias).Append(".occurred_at < @to");
            parameters.Add(new NpgsqlParameter("to", end.ToUniversalTime()));
        }
    }

    /// <summary>A user id, or an email address (exact, ignoring letter case).</summary>
    private static void AddUser(StringBuilder sql, List<NpgsqlParameter> parameters, string alias, string emailColumn, string? user)
    {
        if (user is null)
        {
            return;
        }

        if (Guid.TryParse(user, out Guid id))
        {
            sql.Append(" AND ").Append(alias).Append(".user_id = @userId");
            parameters.Add(new NpgsqlParameter("userId", id));
        }
        else
        {
            sql.Append(" AND lower(").Append(alias).Append('.').Append(emailColumn).Append(") = lower(@userEmail)");
            parameters.Add(new NpgsqlParameter("userEmail", user));
        }
    }

    private static void AddPage(StringBuilder sql, List<NpgsqlParameter> parameters, string alias, AuditCursor? after, int limit)
    {
        if (after is { } cursor)
        {
            sql.Append(" AND (").Append(alias).Append(".occurred_at, ").Append(alias).Append(".id) < (@afterAt, @afterId)");
            parameters.Add(new NpgsqlParameter("afterAt", cursor.OccurredAt.ToUniversalTime()));
            parameters.Add(new NpgsqlParameter("afterId", cursor.Id));
        }

        sql.Append(" ORDER BY ").Append(alias).Append(".occurred_at DESC, ").Append(alias).Append(".id DESC LIMIT @take");
        parameters.Add(new NpgsqlParameter("take", limit + 1));
    }

    private static string EscapeLike(string text) =>
        text.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);

    /// <summary>Managers acting in the history are shown in full (who changed what); everyone else is masked.</summary>
    private async Task<HashSet<Guid>> ManagersAsync(IEnumerable<Guid?> userIds, CancellationToken cancellationToken)
    {
        List<Guid> ids = userIds.OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        string manager = Roles.Manager.ToUpperInvariant();
        List<Guid> managers = await (
                from userRole in db.UserRoles
                join role in db.Roles on userRole.RoleId equals role.Id
                where ids.Contains(userRole.UserId) && role.NormalizedName == manager
                select userRole.UserId)
            .ToListAsync(cancellationToken);
        return [.. managers];
    }

    private static string? DisplayUser(Guid? userId, string? email, HashSet<Guid> managers) =>
        email is null ? null : userId is { } id && managers.Contains(id) ? email : AuditMasking.MaskEmail(email);

    private sealed record ChangeRow(
        long Id, DateTimeOffset OccurredAt, string Action, string Entity, string RecordId, string? Actor, Guid? UserId, string? UserEmail,
        string Changes, string? TraceId);

    private sealed record SecurityRow(long Id, DateTimeOffset OccurredAt, string EventType, Guid? UserId, string? Email, string? IpAddress);
}
