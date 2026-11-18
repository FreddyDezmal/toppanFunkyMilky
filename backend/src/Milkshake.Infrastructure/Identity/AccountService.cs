using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Milkshake.Application.Accounts;
using Milkshake.Domain.Auditing;
using Milkshake.Domain.Users;
using Milkshake.Infrastructure.Persistence;
using Npgsql;

namespace Milkshake.Infrastructure.Identity;

/// <summary>Account use cases implemented with ASP.NET Core Identity.</summary>
internal sealed partial class AccountService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    SecurityEventRecorder securityEvents,
    MilkshakeDbContext dbContext,
    ILogger<AccountService> logger) : IAccountService
{
    public async Task<RegistrationResult> RegisterPatronAsync(RegisterPatronCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        string? mobileNumber = MobileNumber.Normalize(command.MobileNumber);
        if (mobileNumber is null)
        {
            return RegistrationResult.Failure(new Dictionary<string, string[]>
            {
                ["mobileNumber"] = [MobileNumber.InvalidMessage],
            });
        }

        string email = command.Email.Trim();
        string firstName = command.FirstName.Trim();

        // User + role are saved in one transaction. The execution strategy is required because
        // connection retries are enabled; each attempt starts from a clean change tracker.
        IExecutionStrategy strategy = dbContext.Database.CreateExecutionStrategy();
        RegistrationResult result = await strategy.ExecuteAsync(
            async ct =>
            {
                dbContext.ChangeTracker.Clear();
                await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(ct);

                var user = new ApplicationUser
                {
                    UserName = email,          // the email is the login name
                    Email = email,
                    FirstName = firstName,
                    PhoneNumber = mobileNumber,
                };

                IdentityResult created;
                try
                {
                    created = await userManager.CreateAsync(user, command.Password);
                }
                catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    // Two simultaneous registrations with the same email: the unique index decides.
                    return DuplicateEmailFailure();
                }

                if (!created.Succeeded)
                {
                    return RegistrationResult.Failure(MapErrors(created.Errors));
                }

                IdentityResult roleAdded = await userManager.AddToRoleAsync(user, Roles.Patron);
                if (!roleAdded.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Could not assign the {Roles.Patron} role: {string.Join("; ", roleAdded.Errors.Select(e => e.Code))}");
                }

                await transaction.CommitAsync(ct);
                LogPatronRegistered(logger, user.Id);

                return RegistrationResult.Success(new RegisteredUser(user.Id, user.FirstName, user.Email));
            },
            cancellationToken);

        if (result.User is { } registered)
        {
            // Sign in only after the account is committed, so a sign-in problem cannot undo the registration.
            ApplicationUser user = await userManager.FindByIdAsync(registered.Id.ToString())
                ?? throw new InvalidOperationException("The registered user could not be reloaded.");
            await signInManager.SignInAsync(user, isPersistent: false);
        }

        return result;
    }

    public async Task<UserProfile?> LoginAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        string email = command.Email.Trim();
        ApplicationUser? user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            // Spend about as long as a real password check, so response time doesn't reveal unknown emails.
            _ = userManager.PasswordHasher.HashPassword(new ApplicationUser(), command.Password);
            await securityEvents.RecordAsync(SecurityEventType.LoginFailed, null, email, cancellationToken);
            return null;
        }

        SignInResult result = await signInManager.PasswordSignInAsync(
            user, command.Password, isPersistent: false, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            await securityEvents.RecordAsync(SecurityEventType.LoginSucceeded, user.Id, user.Email, cancellationToken);
            return await ToProfileAsync(user);
        }

        SecurityEventType failure = result.IsLockedOut ? SecurityEventType.LoginLockedOut : SecurityEventType.LoginFailed;
        await securityEvents.RecordAsync(failure, user.Id, user.Email, cancellationToken);
        return null;
    }

    public async Task LogoutAsync(Guid userId, CancellationToken cancellationToken)
    {
        ApplicationUser? user = await userManager.FindByIdAsync(userId.ToString());

        await signInManager.SignOutAsync();

        if (user is not null)
        {
            // Invalidates every other cookie issued to this user at the next stamp check.
            await userManager.UpdateSecurityStampAsync(user);
        }

        await securityEvents.RecordAsync(SecurityEventType.Logout, userId, user?.Email, cancellationToken);
    }

    public async Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        ApplicationUser? user = await userManager.FindByIdAsync(userId.ToString());
        return user is null ? null : await ToProfileAsync(user);
    }

    private async Task<UserProfile> ToProfileAsync(ApplicationUser user)
    {
        IList<string> roles = await userManager.GetRolesAsync(user);
        return new UserProfile(
            user.Id,
            user.FirstName,
            user.Email ?? string.Empty,
            user.PhoneNumber,
            roles.Order(StringComparer.Ordinal).ToArray());
    }

    private static RegistrationResult DuplicateEmailFailure() =>
        RegistrationResult.Failure(new Dictionary<string, string[]> { ["email"] = [RegistrationMessages.DuplicateEmail] });

    /// <summary>Translates Identity errors into field-keyed messages without exposing internal detail.</summary>
    private static Dictionary<string, string[]> MapErrors(IEnumerable<IdentityError> errors)
    {
        var byField = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (IdentityError error in errors)
        {
            (string field, string message) = error.Code switch
            {
                nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)
                    => ("email", RegistrationMessages.DuplicateEmail),
                nameof(IdentityErrorDescriber.InvalidEmail) or nameof(IdentityErrorDescriber.InvalidUserName)
                    => ("email", "This email address cannot be used."),
                _ when error.Code.StartsWith("Password", StringComparison.Ordinal)
                    => ("password", error.Description),
                _ => (string.Empty, "Registration could not be completed."),
            };

            if (!byField.TryGetValue(field, out List<string>? messages))
            {
                messages = [];
                byField[field] = messages;
            }

            if (!messages.Contains(message))
            {
                messages.Add(message);
            }
        }

        return byField.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }

    // Only the user id is logged: never the password, and not the email (personal data).
    [LoggerMessage(Level = LogLevel.Information, Message = "Patron account {UserId} registered.")]
    private static partial void LogPatronRegistered(ILogger logger, Guid userId);
}