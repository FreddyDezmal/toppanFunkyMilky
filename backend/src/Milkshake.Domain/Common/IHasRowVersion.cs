namespace Milkshake.Domain.Common;

/// <summary>
/// An entity protected by optimistic concurrency. <see cref="Version"/> changes every time the row is saved;
/// an update based on an older version is rejected instead of silently overwriting someone else's change.
/// </summary>
public interface IHasRowVersion
{
    /// <summary>Row version supplied by the database (PostgreSQL xmin). Never set by application code.</summary>
    uint Version { get; }
}