namespace Milkshake.Domain.Common;

/// <summary>
/// Entities whose creation and last-update times are maintained automatically on save.
/// </summary>
public interface IHasTimestamps
{
    /// <summary>When the row was created (UTC).</summary>
    DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the row was last changed (UTC).</summary>
    DateTimeOffset UpdatedAt { get; set; }
}