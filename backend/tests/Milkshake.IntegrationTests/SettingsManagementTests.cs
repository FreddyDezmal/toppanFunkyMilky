using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api.Contracts.Management;
using Milkshake.Domain.Auditing;
using Milkshake.Domain.Settings;
using Milkshake.Infrastructure.Persistence;
using Milkshake.IntegrationTests.Infrastructure;

namespace Milkshake.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class SettingsManagementTests(PostgresApiFactory factory)
{
    private const string SettingsPath = "/api/manager/settings";

    [Fact]
    public async Task List_AsPatron_Returns403()
    {
        using HttpClient client = factory.CreateHttpsClient();
        using HttpResponseMessage register = await client.PostWithCsrfAsync("/api/auth/register", RegisterBody.Valid());
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        using HttpResponseMessage response = await client.GetAsync(new Uri(SettingsPath, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsSettingsInScreenOrder_WithCurrencyReadOnly()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        List<SettingResponse> settings = await ListAsync(manager);

        Assert.Equal(SettingKeys.MinDrinksPerOrder, settings[0].Key);
        Assert.Equal(SettingKeys.CurrencyCode, settings[^1].Key);
        Assert.False(settings.Single(s => s.Key == SettingKeys.CurrencyCode).IsEditable);
        Assert.Contains("Q-02", settings.Single(s => s.Key == SettingKeys.MaxDiscountAmount).Hint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_LeadTime_SavesAndIsAudited()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        SettingResponse leadTime = await GetAsync(manager, SettingKeys.MinPickupLeadTimeMinutes);
        string newValue = leadTime.Value == "20" ? "25" : "20";

        using HttpResponseMessage response = await PutAsync(manager, leadTime.Key, newValue, leadTime.Version);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<SettingResponse> updated = await response.ReadJsonAsync<List<SettingResponse>>();
        Assert.Equal(newValue, updated.Single(s => s.Key == leadTime.Key).Value);

        using IServiceScope scope = factory.Services.CreateScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(a =>
            a.TableName == "system_settings" && a.RecordId == leadTime.Key && a.Action == AuditAction.Update && a.UserEmail == manager.Email));
    }

    [Fact]
    public async Task Update_MinimumAboveMaximum_Returns400()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        SettingResponse min = await GetAsync(manager, SettingKeys.MinDrinksPerOrder);
        int max = int.Parse((await GetAsync(manager, SettingKeys.MaxDrinksPerOrder)).Value, CultureInfo.InvariantCulture);

        using HttpResponseMessage response = await PutAsync(manager, min.Key, (max + 1).ToString(CultureInfo.InvariantCulture), min.Version);

        await AssertValueErrorAsync(response);
    }

    [Fact]
    public async Task Update_MaximumAboveFifty_Returns400()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        SettingResponse max = await GetAsync(manager, SettingKeys.MaxDrinksPerOrder);

        using HttpResponseMessage response = await PutAsync(manager, max.Key, "51", max.Version);

        await AssertValueErrorAsync(response);
    }

    [Fact]
    public async Task Update_Currency_Returns400()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        SettingResponse currency = await GetAsync(manager, SettingKeys.CurrencyCode);

        using HttpResponseMessage response = await PutAsync(manager, currency.Key, "USD", currency.Version);

        await AssertValueErrorAsync(response);
        Assert.Equal("ZAR", (await GetAsync(manager, SettingKeys.CurrencyCode)).Value);
    }

    [Fact]
    public async Task Update_InvalidLocale_Returns400()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        SettingResponse locale = await GetAsync(manager, SettingKeys.DisplayLocale);

        using HttpResponseMessage response = await PutAsync(manager, locale.Key, "not a locale", locale.Version);

        await AssertValueErrorAsync(response);
    }

    [Fact]
    public async Task Update_WithStaleVersion_Returns409()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        SettingResponse vat = await GetAsync(manager, SettingKeys.VatRate);
        string changed = vat.Value == "0.16" ? "0.15" : "0.16";

        using HttpResponseMessage first = await PutAsync(manager, vat.Key, changed, vat.Version);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using HttpResponseMessage stale = await PutAsync(manager, vat.Key, "0.17", vat.Version);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        // Restore the URS value for other tests.
        SettingResponse current = await GetAsync(manager, SettingKeys.VatRate);
        using HttpResponseMessage restore = await PutAsync(manager, vat.Key, "0.15", current.Version);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
    }

    [Fact]
    public async Task Update_UnknownKey_Returns404()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        using HttpResponseMessage response = await PutAsync(manager, "Unknown.Setting", "1", 1u);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<List<SettingResponse>> ListAsync(ManagerSession manager) =>
        await manager.Client.GetFromJsonAsync<List<SettingResponse>>(new Uri(SettingsPath, UriKind.Relative))
        ?? throw new InvalidOperationException("No settings returned.");

    private static async Task<SettingResponse> GetAsync(ManagerSession manager, string key) =>
        (await ListAsync(manager)).Single(s => s.Key == key);

    private static Task<HttpResponseMessage> PutAsync(ManagerSession manager, string key, string value, uint version) =>
        manager.Client.SendWithCsrfAsync(HttpMethod.Put, $"{SettingsPath}/{key}", new { value, version });

    private static async Task AssertValueErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("value", (await response.ReadJsonAsync<ValidationErrorBody>()).Errors.Keys);
    }
}