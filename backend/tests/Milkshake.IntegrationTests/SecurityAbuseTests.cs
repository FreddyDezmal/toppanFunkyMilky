using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Milkshake.Api;
using Milkshake.Infrastructure.Identity;
using Milkshake.Infrastructure.Persistence;
using Milkshake.IntegrationTests.Infrastructure;

namespace Milkshake.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class SecurityAbuseTests(PostgresApiFactory factory)
{
    private const string MePath = "/api/auth/me";

    [Fact]
    public async Task TamperedSessionCookie_IsRejected()
    {
        using HttpClient client = factory.CreateHttpsClient();
        using HttpResponseMessage register = await client.PostWithCsrfAsync("/api/auth/register", RegisterBody.Valid());
        string genuine = register.GetSetCookie("milkshake.session").Split(';')[0];

        // Change one character in the middle of the encrypted, signed cookie value.
        int position = genuine.IndexOf('=', StringComparison.Ordinal) + 20;
        char replacement = genuine[position] == 'A' ? 'B' : 'A';
        string tampered = genuine[..position] + replacement + genuine[(position + 1)..];

        using HttpClient attacker = factory.CreateSecureClient(handleCookies: false);

        Assert.Equal(HttpStatusCode.OK, await GetMeStatusAsync(attacker, genuine));    // control: the real cookie works
        Assert.Equal(HttpStatusCode.Unauthorized, await GetMeStatusAsync(attacker, tampered));
    }

    [Fact]
    public async Task Passwords_NeverAppearInLogs()
    {
        var logs = new CapturingLoggerProvider();
        using WebApplicationFactory<ApiAssemblyMarker> app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging =>
            {
                logging.AddProvider(logs);
                logging.AddFilter<CapturingLoggerProvider>(null, LogLevel.Trace);   // capture everything, every category
            }));
        using HttpClient client = app.CreateSecureClient();

        string password = $"Zz9{Guid.NewGuid():N}";   // unique, so any occurrence in the logs is unmistakable
        RegisterBody body = RegisterBody.Valid() with { Password = password, ConfirmPassword = password };

        using HttpResponseMessage register = await client.PostWithCsrfAsync("/api/auth/register", body);
        using HttpResponseMessage wrongLogin = await client.PostWithCsrfAsync("/api/auth/login", new LoginBody(body.Email, password + "x"));
        using HttpResponseMessage rightLogin = await client.PostWithCsrfAsync("/api/auth/login", new LoginBody(body.Email, password));

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rightLogin.StatusCode);
        Assert.Contains(logs.Messages, line => line.Contains("Patron account", StringComparison.Ordinal)); // capture works
        Assert.DoesNotContain(logs.Messages, line => line.Contains(password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuditLog_RecordsPasswordHashOnlyAsRedacted()
    {
        using HttpClient client = factory.CreateHttpsClient();
        RegisterBody body = RegisterBody.Valid();
        using HttpResponseMessage register = await client.PostWithCsrfAsync("/api/auth/register", body);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        using IServiceScope scope = factory.Services.CreateScope();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();

        ApplicationUser user = await users.FindByEmailAsync(body.Email) ?? throw new InvalidOperationException("User not created.");
        string recordId = user.Id.ToString();
        List<string> changes = await db.AuditLogs
            .Where(a => a.TableName == "users" && a.RecordId == recordId)
            .Select(a => a.Changes)
            .ToListAsync();

        Assert.NotEmpty(changes);
        Assert.All(changes, entry => Assert.DoesNotContain(user.PasswordHash!, entry, StringComparison.Ordinal));
        Assert.Contains(changes, entry => IsRedacted(entry, "password_hash"));
    }

    private static async Task<HttpStatusCode> GetMeStatusAsync(HttpClient client, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(MePath, UriKind.Relative));
        request.Headers.Add("Cookie", cookie);
        using HttpResponseMessage response = await client.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>True if the audit entry records the field with the redaction marker as its new value.</summary>
    private static bool IsRedacted(string changesJson, string field)
    {
        using JsonDocument json = JsonDocument.Parse(changesJson);
        return json.RootElement.EnumerateArray().Any(change =>
            change.GetProperty("field").GetString() == field &&
            change.GetProperty("new").ValueKind == JsonValueKind.String &&
            change.GetProperty("new").GetString() == "***");
    }
}