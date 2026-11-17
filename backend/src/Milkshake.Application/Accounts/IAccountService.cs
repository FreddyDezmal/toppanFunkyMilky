namespace Milkshake.Application.Accounts;

/// <summary>Account use cases: registration now; login, logout and provisioning follow.</summary>
public interface IAccountService
{
    /// <summary>
    /// Creates a patron account. The Patron role is always assigned by the server; callers cannot choose a role.
    /// </summary>
    Task<RegistrationResult> RegisterPatronAsync(RegisterPatronCommand command, CancellationToken cancellationToken);
}

/// <summary>Data needed to register a patron (URS §3.1).</summary>
public sealed record RegisterPatronCommand(string FirstName, string Email, string MobileNumber, string Password);

/// <summary>Safe summary of a newly registered user.</summary>
public sealed record RegisteredUser(Guid Id, string FirstName, string Email);

/// <summary>Outcome of a registration attempt: the new user, or validation errors keyed by field name.</summary>
public sealed class RegistrationResult
{
    private RegistrationResult(RegisteredUser? user, IReadOnlyDictionary<string, string[]> errors)
    {
        User = user;
        Errors = errors;
    }

    /// <summary>The created user, when registration succeeded.</summary>
    public RegisteredUser? User { get; }

    /// <summary>Validation errors keyed by JSON field name (empty key = not field-specific).</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <summary>True when the account was created.</summary>
    public bool Succeeded => User is not null;

    /// <summary>Creates a successful result.</summary>
    public static RegistrationResult Success(RegisteredUser user) =>
        new(user, new Dictionary<string, string[]>());

    /// <summary>Creates a failed result.</summary>
    public static RegistrationResult Failure(IReadOnlyDictionary<string, string[]> errors) => new(null, errors);
}


/// <summary>User-facing registration messages shared by the service, API and tests.</summary>
public static class RegistrationMessages
{
    /// <summary>Shown when the email belongs to an existing account.</summary>
    public const string DuplicateEmail = "This email address is already registered.";
}