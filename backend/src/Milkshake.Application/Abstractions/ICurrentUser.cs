namespace Milkshake.Application.Abstractions;

/// <summary>
/// The user performing the current operation. Null values mean a system action
/// (seeding, background jobs, design-time tools).
/// </summary>
public interface ICurrentUser
{
    /// <summary>Signed-in user's id, or null.</summary>
    Guid? UserId { get; }

    /// <summary>Signed-in user's email, or null.</summary>
    string? Email { get; }
}