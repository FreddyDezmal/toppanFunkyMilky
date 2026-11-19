using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Domain.Settings;
using Milkshake.Domain.Users;
using Milkshake.Infrastructure.Identity;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.IntegrationTests.Infrastructure;

/// <summary>A signed-in manager's HTTP client.</summary>
internal sealed class ManagerSession(HttpClient client, string email) : IDisposable
{
    public HttpClient Client { get; } = client;

    public string Email { get; } = email;

    public void Dispose() => Client.Dispose();
}

internal static class ManagementTestHelpers
{
    /// <summary>
    /// Creates a manager (Manager role only) directly through Identity, makes sure the reference settings and
    /// discount tiers exist (the Testing environment does not run the seeder), and signs in through the API.
    /// </summary>
    public static async Task<ManagerSession> CreateManagerSessionAsync(this PostgresApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        string email = RegisterBody.UniqueEmail();

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, FirstName = "Manager" };
            if (!(await users.CreateAsync(user, RegisterBody.ValidPassword)).Succeeded ||
                !(await users.AddToRoleAsync(user, Roles.Manager)).Succeeded)
            {
                throw new InvalidOperationException("Could not create the test manager.");
            }

            await EnsureReferenceDataAsync(scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>());
        }

        HttpClient client = factory.CreateHttpsClient();
        using HttpResponseMessage login = await client.PostWithCsrfAsync("/api/auth/login", new LoginBody(email, RegisterBody.ValidPassword));
        if (login.StatusCode != HttpStatusCode.OK)
        {
            client.Dispose();
            throw new InvalidOperationException($"Test manager sign-in failed: {login.StatusCode}.");
        }

        return new ManagerSession(client, email);
    }

    /// <summary>Makes sure the reference data exists, then registers a patron through the API (which signs them in).</summary>
    public static async Task<HttpClient> CreatePatronClientAsync(this PostgresApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            await EnsureReferenceDataAsync(scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>());
        }

        HttpClient client = factory.CreateHttpsClient();
        using HttpResponseMessage register = await client.PostWithCsrfAsync("/api/auth/register", RegisterBody.Valid());
        if (register.StatusCode != HttpStatusCode.Created)
        {
            client.Dispose();
            throw new InvalidOperationException($"Test patron registration failed: {register.StatusCode}.");
        }

        return client;
    }

    /// <summary>Sends JSON with any method and a fresh anti-forgery token.</summary>
    public static async Task<HttpResponseMessage> SendWithCsrfAsync<T>(this HttpClient client, HttpMethod method, string path, T body)
    {
        string token = await client.GetCsrfTokenAsync();

        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);

        return await client.SendAsync(request);
    }

    /// <summary>Reads a JSON body or fails clearly.</summary>
    public static async Task<T> ReadJsonAsync<T>(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidOperationException("The response had no JSON body.");

    /// <summary>URS reference settings, provisional pickup settings and the three tiers (inactive), added only where missing.</summary>
    private static async Task EnsureReferenceDataAsync(MilkshakeDbContext db)
    {
        HashSet<string> keys = [.. await db.SystemSettings.Select(s => s.Key).ToListAsync()];
        SystemSetting[] settings =
        [
            new(SettingKeys.MinDrinksPerOrder, "1", SettingValueType.WholeNumber, "Minimum drinks per order."),
            new(SettingKeys.MaxDrinksPerOrder, "10", SettingValueType.WholeNumber, "Maximum drinks per order."),
            new(SettingKeys.VatRate, "0.15", SettingValueType.DecimalNumber, "VAT as a fraction."),
            new(SettingKeys.MaxDiscountAmount, "50.00", SettingValueType.DecimalNumber, "Maximum discount per order."),
            new(SettingKeys.MinPickupLeadTimeMinutes, "15", SettingValueType.WholeNumber, "Minimum pickup lead time."),
            new(SettingKeys.PickupMaxAdvanceDays, "7", SettingValueType.WholeNumber, "Days ahead a pickup may be booked."),
            new(SettingKeys.PickupTimeZone, "Africa/Johannesburg", SettingValueType.Text, "Pickup time zone."),
            new(SettingKeys.CurrencyCode, "ZAR", SettingValueType.Text, "Pricing currency."),
            new(SettingKeys.DisplayLocale, "en-ZA", SettingValueType.Text, "Display locale."),
        ];
        db.SystemSettings.AddRange(settings.Where(s => !keys.Contains(s.Key)));

        HashSet<int> tierNumbers = [.. await db.DiscountTiers.Select(t => t.TierNumber).ToListAsync()];
        DiscountTier[] tiers =
        [
            new(1, 5, 2, 0.05m, isActive: false),
            new(2, 10, 3, 0.10m, isActive: false),
            new(3, 20, 4, 0.15m, isActive: false),
        ];
        db.DiscountTiers.AddRange(tiers.Where(t => !tierNumbers.Contains(t.TierNumber)));

        await db.SaveChangesAsync();
    }
}