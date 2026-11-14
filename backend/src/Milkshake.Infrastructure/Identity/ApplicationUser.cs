using Microsoft.AspNetCore.Identity;
using Milkshake.Domain.Common;

namespace Milkshake.Infrastructure.Identity;

/// <summary>
/// A patron or manager account. Identity's PhoneNumber holds the mobile number (URS §3.1).
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>, IHasTimestamps
{
    /// <summary>Maximum length of first and last names.</summary>
    public const int NameMaxLength = 100;

    /// <summary>Creates a user with a time-ordered (version 7) id.</summary>
    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
    }

    /// <summary>First name (URS §3.1).</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Optional surname.</summary>
    public string? LastName { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>An application role (see Milkshake.Domain.Users.Roles).</summary>
public sealed class ApplicationRole : IdentityRole<Guid>
{
    /// <summary>For EF Core and Identity.</summary>
    public ApplicationRole()
    {
        Id = Guid.CreateVersion7();
    }

    /// <summary>Creates a named role.</summary>
    public ApplicationRole(string roleName)
        : base(roleName)
    {
        Id = Guid.CreateVersion7();
    }
}