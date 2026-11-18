using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Application.Accounts;
using Milkshake.Domain.Auditing;
using Milkshake.Domain.Users;
using Milkshake.Infrastructure.Persistence;
using Milkshake.IntegrationTests.Infrastructure;

namespace Milkshake.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class ManagerProvisioningTests(PostgresApiFactory factory)
{
    [Fact]
    public async Task Provision_NewAccount_CreatesManagerWhoCanUseManagerEndpoints()
    {
        string email = RegisterBody.UniqueEmail();

        ProvisionManagerResult result = await ProvisionAsync(email, RegisterBody.ValidPassword);

        Assert.Equal(ProvisionManagerOutcome.Created, result.Outcome);

        using HttpClient client = factory.CreateHttpsClient();
        using HttpResponseMessage login = await client.PostWithCsrfAsync("/api/auth/login", new LoginBody(email, RegisterBody.ValidPassword));
        using HttpResponseMessage ping = await client.GetAsync(new Uri("/api/manager/ping", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ping.StatusCode);
        Assert.True(await HasProvisionedEventAsync(email));
    }

    [Fact]
    public async Task Provision_ExistingPatron_PromotesWithoutChangingPassword()
    {
        using HttpClient registrant = factory.CreateHttpsClient();
        RegisterBody body = RegisterBody.Valid();
        using HttpResponseMessage register = await registrant.PostWithCsrfAsync("/api/auth/register", body);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        ProvisionManagerResult result = await ProvisionAsync(body.Email, password: null);

        Assert.Equal(ProvisionManagerOutcome.Promoted, result.Outcome);

        using HttpClient client = factory.CreateHttpsClient();
        using HttpResponseMessage login = await client.PostWithCsrfAsync("/api/auth/login", new LoginBody(body.Email, RegisterBody.ValidPassword));
        CurrentUserBody? user = await login.Content.ReadFromJsonAsync<CurrentUserBody>();
        Assert.Equal(new[] { Roles.Manager, Roles.Patron }, user?.Roles);
    }

    [Fact]
    public async Task Provision_SameAccountTwice_SecondRunChangesNothing()
    {
        string email = RegisterBody.UniqueEmail();

        ProvisionManagerResult first = await ProvisionAsync(email, RegisterBody.ValidPassword);
        ProvisionManagerResult second = await ProvisionAsync(email, password: null);

        Assert.Equal(ProvisionManagerOutcome.Created, first.Outcome);
        Assert.Equal(ProvisionManagerOutcome.AlreadyManager, second.Outcome);
    }

    [Fact]
    public async Task Provision_NewAccountWithoutPassword_FailsAndCreatesNothing()
    {
        string email = RegisterBody.UniqueEmail();

        ProvisionManagerResult result = await ProvisionAsync(email, password: null);

        Assert.False(result.Succeeded);
        Assert.False(await AccountExistsAsync(email));
    }

    [Fact]
    public async Task Provision_WeakPassword_FailsAndCreatesNothing()
    {
        string email = RegisterBody.UniqueEmail();

        ProvisionManagerResult result = await ProvisionAsync(email, "weak");

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);
        Assert.False(await AccountExistsAsync(email));
    }

    private async Task<ProvisionManagerResult> ProvisionAsync(string email, string? password)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IAccountService accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        return await accounts.ProvisionManagerAsync(new ProvisionManagerCommand(email, "Manager", password), CancellationToken.None);
    }

    private async Task<bool> AccountExistsAsync(string email)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IAccountService accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        return await accounts.AccountExistsAsync(email, CancellationToken.None);
    }

    private async Task<bool> HasProvisionedEventAsync(string email)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        return await db.SecurityEvents.AnyAsync(e => e.Email == email && e.EventType == SecurityEventType.ManagerProvisioned);
    }
}