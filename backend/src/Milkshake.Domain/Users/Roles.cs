namespace Milkshake.Domain.Users;

/// <summary>Application roles (URS §4: two or more user profiles).</summary>
public static class Roles
{
    /// <summary>Management user: lookups, fees, reports, audit.</summary>
    public const string Manager = "Manager";

    /// <summary>Customer who places orders.</summary>
    public const string Patron = "Patron";
}