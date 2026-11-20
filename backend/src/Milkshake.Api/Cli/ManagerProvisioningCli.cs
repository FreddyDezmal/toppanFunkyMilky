using System.Text;
using Milkshake.Application.Accounts;
using Milkshake.Application.Abstractions;
using Milkshake.Domain.Auditing;

namespace Milkshake.Api.Cli;

/// <summary>
/// Administrative command that creates or promotes a manager account, then exits without starting the web server:
/// <code>dotnet run --project backend/src/Milkshake.Api --launch-profile https -- provision-manager --email a@b.test --first-name Ann</code>
/// There is deliberately no HTTP endpoint for this. Running it requires shell access and the database secrets.
/// </summary>
internal static class ManagerProvisioningCli
{
    /// <summary>First argument that selects this command.</summary>
    public const string CommandName = "provision-manager";

    /// <summary>Optional password source for unattended set-up; otherwise a hidden prompt is used.</summary>
    public const string PasswordEnvironmentVariable = "MILKSHAKE_MANAGER_PASSWORD";

    public static bool IsInvoked(string[] args) =>
        args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.Ordinal);

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        using IDisposable actor = AuditActorScope.Begin(AuditActors.ManagerProvisioning);   // M11 audit attribution
        string? email = ReadOption(args, "--email");
        string? firstName = ReadOption(args, "--first-name");

        if (string.IsNullOrWhiteSpace(email))
        {
            await Console.Error.WriteLineAsync($"Usage: {CommandName} --email <email> [--first-name <first name>]");
            return 2;
        }

        using IServiceScope scope = services.CreateScope();
        IAccountService accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();

        string? password = null;
        if (!await accounts.AccountExistsAsync(email, CancellationToken.None))
        {
            if (string.IsNullOrWhiteSpace(firstName))
            {
                await Console.Error.WriteLineAsync("A new account needs --first-name.");
                return 2;
            }

            password = ReadNewPassword();
            if (password is null)
            {
                await Console.Error.WriteLineAsync("The passwords were empty or did not match. Nothing was changed.");
                return 1;
            }
        }

        ProvisionManagerResult result = await accounts.ProvisionManagerAsync(
            new ProvisionManagerCommand(email, firstName ?? string.Empty, password),
            CancellationToken.None);

        if (!result.Succeeded)
        {
            await Console.Error.WriteLineAsync("Provisioning failed. Nothing was changed:");
            foreach (string error in result.Errors)
            {
                await Console.Error.WriteLineAsync($"  - {error}");
            }

            return 1;
        }

        Console.WriteLine(result.Outcome switch
        {
            ProvisionManagerOutcome.Created => $"Created manager account {email}.",
            ProvisionManagerOutcome.Promoted => $"Added the Manager role to {email}.",
            _ => $"{email} is already a manager. Nothing was changed.",
        });

        return 0;
    }

    private static string? ReadOption(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string? ReadNewPassword()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable(PasswordEnvironmentVariable);
        if (!string.IsNullOrEmpty(fromEnvironment))
        {
            return fromEnvironment;
        }

        string first = ReadHidden("New manager password: ");
        string second = ReadHidden("Confirm password: ");
        return first.Length > 0 && string.Equals(first, second, StringComparison.Ordinal) ? first : null;
    }

    /// <summary>Reads a line without echoing it to the screen.</summary>
    private static string ReadHidden(string prompt)
    {
        Console.Write(prompt);
        var buffer = new StringBuilder();

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
    }
}