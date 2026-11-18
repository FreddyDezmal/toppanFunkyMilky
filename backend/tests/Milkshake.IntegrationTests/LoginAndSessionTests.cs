using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api;
using Milkshake.Application.Accounts;
using Milkshake.Domain.Auditing;
using Milkshake.Domain.Users;
using Milkshake.Infrastructure.Identity;
using Milkshake.Infrastructure.Persistence;
using Milkshake.IntegrationTests.Infrastructure;

namespace Milkshake.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class LoginAndSessionTests(PostgresApiFactory factory)
{
    private const string LoginPath = "/api/auth/login";
    private const string LogoutPath = "/api/auth/logout";
    private const string MePath = "/api/auth/me";
    private const string ManagerPingPath = "/api/manager/ping";
    private const string WrongPassword = "Wrong1Password";

    [Fact]
    public async Task Login_ValidCredentials_ReturnsProfileAndHardenedSessionCookie()
    {
        string email = await CreatePatronAsync();
        using HttpClient client = factory.CreateHttpsClient();

        using HttpResponseMessage response = await LoginAsync(client, email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CurrentUserBody? user = await response.Content.ReadFromJsonAsync<CurrentUserBody>();
        Assert.NotNull(user);
        Assert.Equal(email, user.Email);
        Assert.Equal(new[] { Roles.Patron }, user.Roles);

        string cookie = response.GetSetCookie("milkshake.session");
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        Assert.True(await HasSecurityEventAsync(email, SecurityEventType.LoginSucceeded));
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownEmail_GetIdenticalResponses()
    {
        string email = await CreatePatronAsync();
        using HttpClient client = factory.CreateHttpsClient();

        using HttpResponseMessage wrongPassword = await LoginAsync(client, email, WrongPassword);
        using HttpResponseMessage unknownEmail = await LoginAsync(client, RegisterBody.UniqueEmail());

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);

        ProblemBody? first = await wrongPassword.Content.ReadFromJsonAsync<ProblemBody>();
        ProblemBody? second = await unknownEmail.Content.ReadFromJsonAsync<ProblemBody>();
        Assert.Equal(LoginMessages.Failed, first?.Detail);
        Assert.Equal(first?.Title, second?.Title);
        Assert.Equal(first?.Detail, second?.Detail);
    }

    [Fact]
    public async Task Login_AfterFiveWrongPasswords_AccountIsLockedEvenForCorrectPassword()
    {
        string email = await CreatePatronAsync();
        using HttpClient client = factory.CreateHttpsClient();

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            using HttpResponseMessage failed = await LoginAsync(client, email, WrongPassword);
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        using HttpResponseMessage correctPassword = await LoginAsync(client, email);

        Assert.Equal(HttpStatusCode.Unauthorized, correctPassword.StatusCode);
        Assert.True(await HasSecurityEventAsync(email, SecurityEventType.LoginLockedOut));
    }

    [Fact]
    public async Task Login_BeyondRateLimit_Returns429()
    {
        using WebApplicationFactory<ApiAssemblyMarker> app =
            factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:AuthPermitLimit", "3"));
        using HttpClient client = app.CreateSecureClient();

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            using HttpResponseMessage allowed = await LoginAsync(client, RegisterBody.UniqueEmail(), WrongPassword);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using HttpResponseMessage limited = await LoginAsync(client, RegisterBody.UniqueEmail(), WrongPassword);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutSession_Returns401()
    {
        using HttpClient client = factory.CreateHttpsClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(MePath, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_ReturnsOnlySafeFields()
    {
        string email = await CreatePatronAsync();
        using HttpClient client = factory.CreateHttpsClient();
        using HttpResponseMessage login = await LoginAsync(client, email);

        using HttpResponseMessage response = await client.GetAsync(new Uri(MePath, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string[] fields = json.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "email", "firstName", "id", "mobileNumber", "roles" }, fields);
    }

    [Fact]
    public async Task Register_SignsTheNewPatronIn()
    {
        using HttpClient client = factory.CreateHttpsClient();

        using HttpResponseMessage register = await client.PostWithCsrfAsync("/api/auth/register", RegisterBody.Valid());
        using HttpResponseMessage me = await client.GetAsync(new Uri(MePath, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        string email = await CreatePatronAsync();
        using HttpClient client = factory.CreateHttpsClient();
        using HttpResponseMessage login = await LoginAsync(client, email);

        using HttpResponseMessage logout = await client.PostWithCsrfAsync(LogoutPath, new { });
        using HttpResponseMessage me = await client.GetAsync(new Uri(MePath, UriKind.Relative));

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.True(await HasSecurityEventAsync(email, SecurityEventType.Logout));
    }

    [Fact]
    public async Task Logout_WithoutCsrfToken_IsRejectedAndSessionSurvives()
    {
        string email = await CreatePatronAsync();
        using HttpClient client = factory.CreateHttpsClient();
        using HttpResponseMessage login = await LoginAsync(client, email);

        using HttpResponseMessage logout = await client.PostAsync(new Uri(LogoutPath, UriKind.Relative), null);
        using HttpResponseMessage me = await client.GetAsync(new Uri(MePath, UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Logout_RotatesSecurityStamp_SoACopiedCookieStopsWorking()
    {
        // Check the stamp on every request, so the effect is visible immediately (production: every 5 minutes).
        using WebApplicationFactory<ApiAssemblyMarker> app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero)));

        string email = await CreatePatronAsync();
        using HttpClient client = app.CreateSecureClient();
        using HttpResponseMessage login = await LoginAsync(client, email);
        string copiedCookie = login.GetSetCookie("milkshake.session").Split(';')[0];

        using HttpResponseMessage logout = await client.PostWithCsrfAsync(LogoutPath, new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using HttpClient attacker = app.CreateSecureClient(handleCookies: false);
        using var replay = new HttpRequestMessage(HttpMethod.Get, new Uri(MePath, UriKind.Relative));
        replay.Headers.Add("Cookie", copiedCookie);
        using HttpResponseMessage response = await attacker.SendAsync(replay);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ManagerEndpoint_AsPatron_Returns403()
    {
        string email = await CreatePatronAsync();
        using HttpClient client = factory.CreateHttpsClient();
        using HttpResponseMessage login = await LoginAsync(client, email);

        using HttpResponseMessage response = await client.GetAsync(new Uri(ManagerPingPath, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ManagerEndpoint_AsManager_Returns200()
    {
        string email = await CreateManagerAsync();
        using HttpClient client = factory.CreateHttpsClient();
        using HttpResponseMessage login = await LoginAsync(client, email);

        using HttpResponseMessage response = await client.GetAsync(new Uri(ManagerPingPath, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password = RegisterBody.ValidPassword) =>
        client.PostWithCsrfAsync(LoginPath, new LoginBody(email, password));

    /// <summary>Registers a patron through the public API (with a throwaway client) and returns the email.</summary>
    private async Task<string> CreatePatronAsync()
    {
        using HttpClient client = factory.CreateHttpsClient();
        RegisterBody body = RegisterBody.Valid();

        using HttpResponseMessage response = await client.PostWithCsrfAsync("/api/auth/register", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return body.Email;
    }

    /// <summary>Creates a manager directly through Identity: test set-up stands in for the provisioning process (Step 6).</summary>
    private async Task<string> CreateManagerAsync()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        string email = RegisterBody.UniqueEmail();
        var user = new ApplicationUser { UserName = email, Email = email, FirstName = "Manager" };

        Assert.True((await users.CreateAsync(user, RegisterBody.ValidPassword)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, Roles.Manager)).Succeeded);
        return email;
    }

    private async Task<bool> HasSecurityEventAsync(string email, SecurityEventType eventType)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        return await db.SecurityEvents.AnyAsync(e => e.Email == email && e.EventType == eventType);
    }
}