using Microsoft.EntityFrameworkCore;

namespace Milkshake.Infrastructure.Persistence;

/// <summary>
/// EF Core unit of work for the Milkshake database (Supabase PostgreSQL).
/// </summary>
/// <remarks>
/// Entity configurations live in separate IEntityTypeConfiguration classes in this assembly
/// and are discovered automatically. When ASP.NET Core Identity is added, this class will
/// inherit from IdentityDbContext instead of DbContext.
/// </remarks>
public sealed class MilkshakeDbContext(DbContextOptions<MilkshakeDbContext> options) : DbContext(options)
{
    /// <summary>
    /// PostgreSQL schema that holds all application tables, kept separate from Supabase-managed schemas.
    /// </summary>
    public const string Schema = "milkshake";

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MilkshakeDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}