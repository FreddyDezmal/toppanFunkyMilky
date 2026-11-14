using Milkshake.Application.Abstractions;

namespace Milkshake.Infrastructure.Persistence.Auditing;

/// <summary>Used when no request user exists (design-time tools, seeding); audit records show "system".</summary>
internal sealed class SystemCurrentUser : ICurrentUser
{
    public Guid? UserId => null;

    public string? Email => null;
}