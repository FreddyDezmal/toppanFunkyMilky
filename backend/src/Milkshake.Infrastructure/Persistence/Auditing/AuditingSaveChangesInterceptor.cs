using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Milkshake.Application.Abstractions;
using Milkshake.Domain.Auditing;
using Milkshake.Domain.Common;

namespace Milkshake.Infrastructure.Persistence.Auditing;

/// <summary>
/// Runs before every SaveChanges: maintains created_at/updated_at and adds one AuditLog per changed row
/// to the same SaveChanges, so data and audit are committed (or rolled back) together.
/// Requires keys to be known before saving (HiLo sequence or client-generated GUIDs).
/// M11: records who acted (<see cref="AuditActors"/>) – a named system process when an <see cref="AuditActorScope"/> is
/// active (never the browser user then), otherwise the signed-in user or "anonymous" – and refuses any change to
/// audit records or security events (append-only, AC-11.3; the database refuses it too).
/// </summary>
internal sealed class AuditingSaveChangesInterceptor(TimeProvider timeProvider, ICurrentUser currentUser) : SaveChangesInterceptor
{
    private const string RedactedValue = "***";
    private const string CreatedAtColumn = "created_at";
    private const string UpdatedAtColumn = "updated_at";
    
    // PostgreSQL row version (optimistic concurrency): changes on every write and is not business data.
    private const string RowVersionColumn = "xmin";

    // Secrets that must never be written to the audit trail in clear text, and the email outbox's personal data and
    // content (M9: the delivery log itself is the record; the audit trail shows only that it changed).
    private static readonly HashSet<string> SensitiveColumns = new(StringComparer.Ordinal)
    {
        "password_hash",
        "security_stamp",
        "concurrency_stamp",
        "recipient_email",
        "recipient_name",
        "payload",
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        StampAndAudit(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        StampAndAudit(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void StampAndAudit(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        string? systemActor = AuditActorScope.Current;
        Guid? userId = systemActor is null ? currentUser.UserId : null;
        string? userEmail = systemActor is null ? currentUser.Email : null;
        string actor = systemActor ?? (userId is null ? AuditActors.Anonymous : AuditActors.User);
        string? traceId = Activity.Current?.TraceId.ToString();

        var auditLogs = new List<AuditLog>();

        foreach (EntityEntry entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is AuditLog or SecurityEvent)
            {
                if (entry.State is EntityState.Modified or EntityState.Deleted)
                {
                    throw new InvalidOperationException(
                        "Audit records and security events are append-only: they cannot be changed or deleted.");
                }

                continue;
            }

            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            ApplyTimestamps(entry, now);

            AuditLog? auditLog = CreateAuditLog(entry, now, userId, userEmail, traceId, actor);
            if (auditLog is not null)
            {
                auditLogs.Add(auditLog);
            }
        }

        if (auditLogs.Count > 0)
        {
            context.Set<AuditLog>().AddRange(auditLogs);
        }
    }

    private static void ApplyTimestamps(EntityEntry entry, DateTimeOffset now)
    {
        if (entry.Entity is not IHasTimestamps)
        {
            return;
        }

        if (entry.State == EntityState.Added)
        {
            entry.Property(nameof(IHasTimestamps.CreatedAt)).CurrentValue = now;
            entry.Property(nameof(IHasTimestamps.UpdatedAt)).CurrentValue = now;
        }
        else if (entry.State == EntityState.Modified)
        {
            entry.Property(nameof(IHasTimestamps.UpdatedAt)).CurrentValue = now;
            entry.Property(nameof(IHasTimestamps.CreatedAt)).IsModified = false;
        }
    }

    private static AuditLog? CreateAuditLog(
        EntityEntry entry, DateTimeOffset now, Guid? userId, string? userEmail, string? traceId, string actor)
    {
        string tableName = entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name;
        bool isTokenTable = tableName == "user_tokens";
        var changes = new List<AuditChange>();

        foreach (PropertyEntry property in AllProperties(entry))
        {
            string column = property.Metadata.GetColumnName();
            if (column is CreatedAtColumn or UpdatedAtColumn or RowVersionColumn)
            {
                continue;
            }

            object? oldValue = entry.State == EntityState.Added ? null : property.OriginalValue;
            object? newValue = entry.State == EntityState.Deleted ? null : property.CurrentValue;

            if (entry.State == EntityState.Modified && (!property.IsModified || Equals(oldValue, newValue)))
            {
                continue;
            }

            bool sensitive = SensitiveColumns.Contains(column) || (isTokenTable && column == "value");
            changes.Add(new AuditChange(
                column,
                sensitive && oldValue is not null ? RedactedValue : oldValue,
                sensitive && newValue is not null ? RedactedValue : newValue));
        }

        // M11 (AC-11.1): every UPDATE the application issues is audited. Some updates change no business field on
        // purpose – a "touch" that only advances updated_at so the row's version check guards a related change (a
        // catalogue item when its prices change, the partner of the minimum/maximum drinks setting). Record what
        // actually changed for those: the modified columns, normally just updated_at.
        if (changes.Count == 0 && entry.State == EntityState.Modified)
        {
            foreach (PropertyEntry property in AllProperties(entry).Where(p => p.IsModified))
            {
                string column = property.Metadata.GetColumnName();
                if (column == RowVersionColumn)
                {
                    continue;
                }

                bool sensitive = SensitiveColumns.Contains(column) || (isTokenTable && column == "value");
                changes.Add(new AuditChange(
                    column,
                    sensitive && property.OriginalValue is not null ? RedactedValue : property.OriginalValue,
                    sensitive && property.CurrentValue is not null ? RedactedValue : property.CurrentValue));
            }
        }

        if (changes.Count == 0)
        {
            return null;
        }

        AuditAction action = entry.State switch
        {
            EntityState.Added => AuditAction.Insert,
            EntityState.Deleted => AuditAction.Delete,
            _ => AuditAction.Update,
        };

        return new AuditLog(
            now,
            action,
            tableName,
            GetRecordId(entry),
            JsonSerializer.Serialize(changes, JsonOptions),
            userId,
            userEmail,
            traceId,
            actor);
    }

    // Regular properties plus the columns of complex properties such as Money (amount, currency_code).
    private static IEnumerable<PropertyEntry> AllProperties(EntityEntry entry) =>
        entry.Properties.Concat(entry.ComplexProperties.SelectMany(complex => complex.Properties));

    private static string GetRecordId(EntityEntry entry)
    {
        IKey? key = entry.Metadata.FindPrimaryKey();
        if (key is null)
        {
            return "(no key)";
        }

        var parts = new List<string>();
        foreach (IProperty keyProperty in key.Properties)
        {
            PropertyEntry keyEntry = entry.Property(keyProperty.Name);
            if (keyEntry.IsTemporary)
            {
                throw new InvalidOperationException(
                    $"Cannot audit '{entry.Metadata.ClrType.Name}': its key is generated by the database after insert. " +
                    "Use a HiLo sequence or client-generated GUID so the key is known before saving.");
            }

            parts.Add(Convert.ToString(keyEntry.CurrentValue, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return string.Join(",", parts);
    }

    private sealed record AuditChange(string Field, object? Old, object? New);
}