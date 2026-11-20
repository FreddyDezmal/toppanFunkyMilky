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

    /// <summary>Management audit history (M11), per signed-in manager: bounded queries over a large table.</summary>
    public const string Audit = "audit";

    /// <summary>Management reports (M12), per signed-in manager: aggregate queries over all orders.</summary>
    public const string Reports = "reports";

    /// <summary>Report and audit exports (M12), per signed-in manager: each builds a file of up to 10,000 records.</summary>
    public const string ReportExports = "report-exports";
}