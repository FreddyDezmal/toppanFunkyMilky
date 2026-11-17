using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Milkshake.Application.Accounts;
using Milkshake.Domain.Users;
using Milkshake.Infrastructure.Persistence;
using Npgsql;

namespace Milkshake.Infrastructure.Identity;

/// <summary>Account use cases implemented with ASP.NET Core Identity.</summary>
internal sealed partial class AccountService(
    UserManager<ApplicationUser> userManager,
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
        return await strategy.ExecuteAsync(
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