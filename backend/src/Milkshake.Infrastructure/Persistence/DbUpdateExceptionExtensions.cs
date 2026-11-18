using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Milkshake.Infrastructure.Persistence;

/// <summary>Recognises PostgreSQL errors behind EF Core save failures.</summary>
internal static class DbUpdateExceptionExtensions
{
    /// <summary>True if a unique index rejected the save.</summary>
    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}