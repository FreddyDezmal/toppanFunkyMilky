namespace Milkshake.Application.Management;

/// <summary>How a management command ended.</summary>
public enum ManagementOutcome
{
    /// <summary>Saved (or nothing needed saving).</summary>
    Success,

    /// <summary>The record does not exist.</summary>
    NotFound,

    /// <summary>Validation failed; see the errors.</summary>
    Invalid,

    /// <summary>Someone else saved the record since it was read (stale version).</summary>
    Conflict,
}

/// <summary>Outcome of a management command: the saved record, or why nothing was saved.</summary>
public sealed class ManagementResult<T>
    where T : class
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    private ManagementResult(ManagementOutcome outcome, T? value, IReadOnlyDictionary<string, string[]> errors)
    {
        Outcome = outcome;
        Value = value;
        Errors = errors;
    }

    /// <summary>How the command ended.</summary>
    public ManagementOutcome Outcome { get; }

    /// <summary>The saved record, when successful.</summary>
    public T? Value { get; }

    /// <summary>Validation errors keyed by JSON field name.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <summary>Creates a successful result.</summary>
    public static ManagementResult<T> Success(T value) => new(ManagementOutcome.Success, value, NoErrors);

    /// <summary>The record does not exist.</summary>
    public static ManagementResult<T> NotFound() => new(ManagementOutcome.NotFound, null, NoErrors);

    /// <summary>The record changed since it was read.</summary>
    public static ManagementResult<T> Conflict() => new(ManagementOutcome.Conflict, null, NoErrors);

    /// <summary>Validation failed.</summary>
    public static ManagementResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors) => new(ManagementOutcome.Invalid, null, errors);

    /// <summary>Validation failed on one field.</summary>
    public static ManagementResult<T> Invalid(string field, string message) =>
        Invalid(new Dictionary<string, string[]> { [field] = [message] });
}