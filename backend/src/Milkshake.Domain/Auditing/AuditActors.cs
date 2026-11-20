using System.Text.RegularExpressions;

namespace Milkshake.Domain.Auditing;

/// <summary>
/// Who an audit record is attributed to (M11, AC-11.1). A request with a signed-in user is <see cref="User"/>; a
/// request without one is <see cref="Anonymous"/>; work done by the system itself (webhooks, background jobs, seeding,
/// the command line) is named explicitly and never attributed to a browser user.
/// </summary>
public static partial class AuditActors
{
    public const int MaxLength = 50;

    /// <summary>
    /// The same rule as the database check on <c>audit_logs.actor</c> (<see cref="DatabasePattern"/>). <c>\z</c>, not
    /// <c>$</c>: in .NET <c>$</c> also matches before a trailing line break.
    /// </summary>
    public const string Pattern = @"^(user|anonymous|system:[a-z][a-z-]{0,40})\z";

    /// <summary>For the PostgreSQL check constraint, where <c>$</c> is the end of the text.</summary>
    public const string DatabasePattern = "^(user|anonymous|system:[a-z][a-z-]{0,40})$";

    public const string User = "user";
    public const string Anonymous = "anonymous";
    public const string SystemPrefix = "system:";
    public const string PaystackWebhook = "system:paystack-webhook";
    public const string PaymentReconciliation = "system:payment-reconciliation";
    public const string EmailDispatcher = "system:email-dispatcher";
    public const string Seeding = "system:seeding";
    public const string ManagerProvisioning = "system:manager-provisioning";

    public static bool IsValid(string? actor) => actor is not null && actor.Length <= MaxLength && ActorRegex().IsMatch(actor);

    public static bool IsSystem(string? actor) => IsValid(actor) && actor!.StartsWith(SystemPrefix, StringComparison.Ordinal);

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex ActorRegex();
}
