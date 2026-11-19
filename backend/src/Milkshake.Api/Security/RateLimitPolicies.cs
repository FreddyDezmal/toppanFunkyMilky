namespace Milkshake.Api.Security;

/// <summary>Names of the rate-limiting policies used on controllers.</summary>
public static class RateLimitPolicies
{
    /// <summary>Login and registration: limited per client IP to slow down password guessing.</summary>
    public const string Auth = "auth";

    /// <summary>Patron ordering endpoints: limited per signed-in user (M5 finding F1).</summary>
    public const string Ordering = "ordering";
}