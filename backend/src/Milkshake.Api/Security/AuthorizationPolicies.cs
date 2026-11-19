namespace Milkshake.Api.Security;

/// <summary>Names of the authorization policies used on controllers.</summary>
public static class AuthorizationPolicies
{
    /// <summary>Only users in the Manager role.</summary>
    public const string ManagerOnly = "ManagerOnly";

    /// <summary>Only users in the Patron role (ordering). Manager-only accounts cannot order (Q-19).</summary>
    public const string PatronOnly = "PatronOnly";
}