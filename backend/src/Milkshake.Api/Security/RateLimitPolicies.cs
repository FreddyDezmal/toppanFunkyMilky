namespace Milkshake.Api.Security;

/// <summary>Names of the rate-limiting policies used on controllers.</summary>
public static class RateLimitPolicies
{
    /// <summary>Login and registration: limited per client IP to slow down password guessing.</summary>
    public const string Auth = "auth";

    /// <summary>Patron ordering endpoints: limited per signed-in user (M5 finding F1).</summary>
    public const string Ordering = "ordering";

    /// <summary>Payment provider notifications, per source address (M8). Generous so legitimate retries are never refused.</summary>
    public const string Webhook = "webhook";

    /// <summary>Management → Emails resend (M9), per signed-in manager: a resend reaches a patron's inbox.</summary>
    public const string EmailResend = "email-resend";
}