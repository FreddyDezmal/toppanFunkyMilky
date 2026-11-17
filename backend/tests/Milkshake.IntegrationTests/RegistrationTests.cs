using Milkshake.Application.Accounts;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Domain.Users;
using Milkshake.Infrastructure.Identity;
using Milkshake.IntegrationTests.Infrastructure;

namespace Milkshake.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class RegistrationTests(PostgresApiFactory factory)
{
    private const string RegisterPath = "/api/auth/register";

    [Fact]
    public async Task Register_ValidPatron_Returns201AndAssignsOnlyPatronRole()
    {
        using HttpClient client = factory.CreateHttpsClient();
        RegisterBody body = RegisterBody.Valid();

        using HttpResponseMessage response = await client.PostWithCsrfAsync(RegisterPath, body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        ApplicationUser user = await FindUserAsync(body.Email) ?? throw new InvalidOperationException("User not created.");
        Assert.Equal("+27821234567", user.PhoneNumber);
        Assert.NotEqual(body.Password, user.PasswordHash);
        Assert.Equal(new[] { Roles.Patron }, await GetRolesAsync(user));
    }

    [Fact]
    public async Task Register_ResponseContainsOnlySafeFields()
    {
        using HttpClient client = factory.CreateHttpsClient();

        using HttpResponseMessage response = await client.PostWithCsrfAsync(RegisterPath, RegisterBody.Valid());

        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string[] fields = json.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "email", "firstName", "id" }, fields);
    }

    [Fact]
    public async Task Register_DuplicateEmailInDifferentCase_Returns400OnEmail()
    {
        using HttpClient client = factory.CreateHttpsClient();
        string email = RegisterBody.UniqueEmail();

        using HttpResponseMessage first = await client.PostWithCsrfAsync(RegisterPath, RegisterBody.Valid(email.ToUpperInvariant()));
        using HttpResponseMessage second = await client.PostWithCsrfAsync(RegisterPath, RegisterBody.Valid(email));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        ValidationErrorBody errors = await ReadErrorsAsync(second);
        Assert.Contains(RegistrationMessages.DuplicateEmail, errors.Errors["email"]);
    }

    [Fact]
    public async Task Register_PasswordWithoutUppercase_Returns400OnPassword()
    {
        using HttpClient client = factory.CreateHttpsClient();
        RegisterBody body = RegisterBody.Valid() with { Password = "lowercase1", ConfirmPassword = "lowercase1" };

        using HttpResponseMessage response = await client.PostWithCsrfAsync(RegisterPath, body);

        ValidationErrorBody errors = await ReadErrorsAsync(response);
        Assert.True(errors.Errors.ContainsKey("password"));
        Assert.Null(await FindUserAsync(body.Email));
    }

    [Fact]
    public async Task Register_MissingAndInvalidFields_Returns400PerField()
    {
        using HttpClient client = factory.CreateHttpsClient();
        RegisterBody body = new(string.Empty, "not-an-email", "+27821234567", "Valid1Password", "Different1Password");

        using HttpResponseMessage response = await client.PostWithCsrfAsync(RegisterPath, body);

        ValidationErrorBody errors = await ReadErrorsAsync(response);
        Assert.True(errors.Errors.ContainsKey("firstName"));
        Assert.True(errors.Errors.ContainsKey("email"));
        Assert.True(errors.Errors.ContainsKey("confirmPassword"));
    }

    [Fact]
    public async Task Register_InvalidMobileNumber_Returns400OnMobileNumber()
    {
        using HttpClient client = factory.CreateHttpsClient();
        RegisterBody body = RegisterBody.Valid() with { MobileNumber = "082 123 4567" };

        using HttpResponseMessage response = await client.PostWithCsrfAsync(RegisterPath, body);

        ValidationErrorBody errors = await ReadErrorsAsync(response);
        Assert.True(errors.Errors.ContainsKey("mobileNumber"));
    }

    [Fact]
    public async Task Register_WithManagerRoleInBody_StillCreatesOnlyAPatron()
    {
        using HttpClient client = factory.CreateHttpsClient();
        string email = RegisterBody.UniqueEmail();
        var maliciousBody = new
        {
            firstName = "Mallory",
            email,
            mobileNumber = "+27821234567",
            password = RegisterBody.ValidPassword,
            confirmPassword = RegisterBody.ValidPassword,
            role = Roles.Manager,
            roles = new[] { Roles.Manager },
        };

        using HttpResponseMessage response = await client.PostWithCsrfAsync(RegisterPath, maliciousBody);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        ApplicationUser user = await FindUserAsync(email) ?? throw new InvalidOperationException("User not created.");
        Assert.Equal(new[] { Roles.Patron }, await GetRolesAsync(user));
    }

    [Fact]
    public async Task Register_WithoutCsrfToken_Returns400AndCreatesNoUser()
    {
        using HttpClient client = factory.CreateHttpsClient();
        RegisterBody body = RegisterBody.Valid();

        using HttpResponseMessage response = await client.PostAsJsonAsync(new Uri(RegisterPath, UriKind.Relative), body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await FindUserAsync(body.Email));
    }

    private static async Task<ValidationErrorBody> ReadErrorsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<ValidationErrorBody>()
            ?? throw new InvalidOperationException("Response had no validation body.");
    }

    private async Task<ApplicationUser?> FindUserAsync(string email)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.FindByEmailAsync(email);
    }

    private async Task<IList<string>> GetRolesAsync(ApplicationUser user)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.GetRolesAsync(user);
    }
}
