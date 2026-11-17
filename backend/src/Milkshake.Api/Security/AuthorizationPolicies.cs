namespace Milkshake.Api.Security;

/// <summary>Names of the authorization policies used on controllers.</summary>
public static class AuthorizationPolicies
{
    /// <summary>Only users in the Manager role.</summary>
    public const string ManagerOnly = "ManagerOnly";
}