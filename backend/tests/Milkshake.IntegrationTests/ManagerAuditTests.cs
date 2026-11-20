using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api;
using Milkshake.Api.Contracts.Management;
using Milkshake.IntegrationTests.Infrastructure;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M11 audit query (AC-11.4) against disposable PostgreSQL 17: managers only, strict filters, keyset paging, personal
/// data masked (and never searchable), sign-in events, rate limiting, and answers within 3 seconds over 100,000 records.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class ManagerAuditTests(PostgresApiFactory factory)
{
    private const string Changes = "/api/manager/audit/changes";
    private const string Security = "/api/manager/audit/security";

    [Fact]
    public async Task AManagersChange_IsListedWithItsFieldsActionActorAndManager()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        RestaurantResponse restaurant = await manager.CreateTestRestaurantAsync();

        AuditPageResponse<AuditChangeResponse> page = await GetAsync<AuditChangeResponse>(manager.Client,
            $"{Changes}?entity=restaurants&action=Insert&user={Uri.EscapeDataString(manager.Email)}&limit=5");

        AuditChangeResponse entry = Assert.Single(page.Items, i => i.RecordId == restaurant.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(("Insert", "restaurants", "user", manager.Email), (entry.Action, entry.Entity, entry.Actor, entry.User));
        AuditFieldChangeResponse name = Assert.Single(entry.Changes, c => c.Field == "name");
        Assert.Null(name.Old);
        Assert.Equal(restaurant.Name, name.New!.Value.GetString());
        Assert.DoesNotContain(entry.Changes, c => c.Field is "xmin" or "created_at" or "updated_at");
    }

    [Fact]
    public async Task APatronsAccountDetails_AreMasked_AndCannotBeFoundBySearchingForThem()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        string email = RegisterBody.UniqueEmail();
        using HttpClient patron = factory.CreateHttpsClient();
        using (HttpResponseMessage registered = await patron.PostWithCsrfAsync("/api/auth/register", RegisterBody.Valid(email)))
        {
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        }

        using HttpResponseMessage accounts = await manager.Client.GetAsync(new Uri($"{Changes}?entity=users&action=Insert&limit=100", UriKind.Relative));
        string body = await accounts.Content.ReadAsStringAsync();
        AuditPageResponse<AuditChangeResponse> searched = await GetAsync<AuditChangeResponse>(manager.Client, $"{Changes}?q={Uri.EscapeDataString(email)}");

        Assert.Equal(HttpStatusCode.OK, accounts.StatusCode);
        Assert.DoesNotContain(email, body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"***\"", body, StringComparison.Ordinal);
        Assert.Empty(searched.Items);
    }

    [Fact]
    public async Task SignInEvents_AreListed_WithManagersInFull_AndPatronsAndAddressesMasked()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        string email = RegisterBody.UniqueEmail();
        using HttpClient patron = factory.CreateHttpsClient();
        using (HttpResponseMessage registered = await patron.PostWithCsrfAsync("/api/auth/register", RegisterBody.Valid(email)))
        {
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        }

        using (HttpResponseMessage login = await patron.PostWithCsrfAsync("/api/auth/login", new LoginBody(email, RegisterBody.ValidPassword)))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        AuditPageResponse<SecurityEventResponse> managerEvents = await GetAsync<SecurityEventResponse>(manager.Client,
            $"{Security}?user={Uri.EscapeDataString(manager.Email)}&eventType=LoginSucceeded");
        AuditPageResponse<SecurityEventResponse> patronEvents = await GetAsync<SecurityEventResponse>(manager.Client,
            $"{Security}?user={Uri.EscapeDataString(email)}");

        Assert.Equal(manager.Email, Assert.Single(managerEvents.Items).User);
        Assert.NotEmpty(patronEvents.Items);
        Assert.All(patronEvents.Items, e => Assert.Equal(email[0] + "***@example.test", e.User));
        SecurityEventResponse patronLogin = patronEvents.Items.First(e => e.EventType == "LoginSucceeded");
        Assert.True(patronLogin.IpAddress is null or "***" || patronLogin.IpAddress.Contains("x.x", StringComparison.Ordinal) ||
                    patronLogin.IpAddress.EndsWith('…'));
    }

    [Fact]
    public async Task Filters_AreCombined()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        await manager.CreateTestRestaurantAsync();

        AuditPageResponse<AuditChangeResponse> inserts = await GetAsync<AuditChangeResponse>(manager.Client,
            $"{Changes}?entity=restaurants&action=Insert&actor=user&limit=100");
        AuditPageResponse<AuditChangeResponse> future = await GetAsync<AuditChangeResponse>(manager.Client,
            $"{Changes}?from=2090-01-01T00:00:00Z");   // beyond any simulated test clock (some tests run days ahead)
        AuditPageResponse<AuditChangeResponse> today = await GetAsync<AuditChangeResponse>(manager.Client,
            $"{Changes}?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"))}&entity=restaurants");

        Assert.NotEmpty(inserts.Items);
        Assert.All(inserts.Items, i => Assert.Equal(("restaurants", "Insert", "user"), (i.Entity, i.Action, i.Actor)));
        Assert.Empty(future.Items);
        Assert.NotEmpty(today.Items);
        Assert.All(today.Items, i => Assert.InRange(i.OccurredAt, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddMinutes(5)));
    }

    [Fact]
    public async Task Pages_AreStable_NewestFirst_WithoutOverlap()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        for (int i = 0; i < 3; i++)
        {
            await manager.CreateTestRestaurantAsync();
        }

        string filter = $"entity=restaurants&action=Insert&user={Uri.EscapeDataString(manager.Email)}";
        AuditPageResponse<AuditChangeResponse> first = await GetAsync<AuditChangeResponse>(manager.Client, $"{Changes}?{filter}&limit=2");
        AuditPageResponse<AuditChangeResponse> second = await GetAsync<AuditChangeResponse>(manager.Client,
            $"{Changes}?{filter}&limit=2&cursor={first.NextCursor}");

        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        AuditChangeResponse last = Assert.Single(second.Items);
        Assert.Null(second.NextCursor);
        Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));
        Assert.True(first.Items[0].OccurredAt >= first.Items[1].OccurredAt && first.Items[1].OccurredAt >= last.OccurredAt);
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("from=yesterday")]
    [InlineData("from=2026-11-20T00:00:00Z&to=2026-11-19T00:00:00Z")]
    [InlineData("from=2024-01-01T00:00:00Z&to=2026-01-01T00:00:00Z")]
    [InlineData("entity=no_such_table")]
    [InlineData("entity=orders%3B%20drop%20table%20orders")]
    [InlineData("action=Truncate")]
    [InlineData("actor=root")]
    [InlineData("cursor=bm90LWEtY3Vyc29y")]
    [InlineData("password=x")]
    [InlineData("entity=orders&entity=payments")]
    public async Task InvalidFilters_AreRefused_WithFieldErrors(string query)
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        using HttpResponseMessage response = await manager.Client.GetAsync(new Uri($"{Changes}?{query}", UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"errors\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FreeTextWithWildcards_IsTakenLiterally()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        AuditPageResponse<AuditChangeResponse> page = await GetAsync<AuditChangeResponse>(manager.Client,
            $"{Changes}?q={Uri.EscapeDataString("%_no-such-text-" + Guid.NewGuid().ToString("N"))}");

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task PatronsAndAnonymousVisitors_CannotReadTheAuditHistory()
    {
        using HttpClient patron = await factory.CreatePatronClientAsync();
        using HttpClient anonymous = factory.CreateHttpsClient();

        using HttpResponseMessage patronChanges = await patron.GetAsync(new Uri(Changes, UriKind.Relative));
        using HttpResponseMessage patronSecurity = await patron.GetAsync(new Uri(Security, UriKind.Relative));
        using HttpResponseMessage anonymousChanges = await anonymous.GetAsync(new Uri(Changes, UriKind.Relative));

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized),
            (patronChanges.StatusCode, patronSecurity.StatusCode, anonymousChanges.StatusCode));
    }

    [Fact]
    public async Task TheAuditHistory_CannotBeChangedThroughTheApi()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        foreach (HttpMethod method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(method, Changes, new { });
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
    }

    [Fact]
    public async Task AuditQueries_AreRateLimitedPerManager()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        using WebApplicationFactory<ApiAssemblyMarker> app = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:AuditPermitLimit", "2"));
        using HttpClient client = app.CreateSecureClient();
        using (HttpResponseMessage login = await client.PostWithCsrfAsync("/api/auth/login", new LoginBody(manager.Email, RegisterBody.ValidPassword)))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        var statuses = new List<HttpStatusCode>();
        for (int i = 0; i < 3; i++)
        {
            using HttpResponseMessage response = await client.GetAsync(new Uri($"{Changes}?limit=1", UriKind.Relative));
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses.ToArray());
    }

    [Fact]
    public async Task OverOneHundredThousandRecords_EveryFilterAnswersWithinThreeSeconds()
    {
        // AC-11.4 [Rec target]. Synthetic history spread over a year, with negative ids so it never meets real ones.
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext seed = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
            seed.Database.SetCommandTimeout(TimeSpan.FromMinutes(3));   // setup only; the timed queries below keep the 3-second target
            await seed.Database.ExecuteSqlRawAsync("""
                INSERT INTO milkshake.audit_logs (id, action, changes, occurred_at, record_id, table_name, actor, user_email)
                SELECT -1000000 - g,
                       (ARRAY['Insert', 'Update', 'Delete'])[1 + g % 3],
                       jsonb_build_array(jsonb_build_object('field', 'status', 'old', 'Pending', 'new', 'Seed' || g)),
                       now() - make_interval(secs => g * 300),
                       (g % 5000)::text,
                       (ARRAY['orders', 'payments', 'refunds', 'flavours'])[1 + g % 4],
                       (ARRAY['user', 'anonymous', 'system:payment-reconciliation'])[1 + g % 3],
                       'perf' || (g % 50) || '@example.test'
                FROM generate_series(1, 100000) AS g
                WHERE NOT EXISTS (SELECT 1 FROM milkshake.audit_logs WHERE id = -1000001)
                """);
        }

        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        string month = $"from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-60).ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-30).ToString("O"))}";
        AuditPageResponse<AuditChangeResponse> firstPage = await GetAsync<AuditChangeResponse>(manager.Client, $"{Changes}?limit=100");
        string[] queries =
        [
            "limit=100",
            "entity=orders&action=Update&limit=100",
            "user=perf7%40example.test&limit=100",
            "q=Seed99999",
            month + "&limit=100",
            "actor=system%3Apayment-reconciliation&entity=refunds&q=status&limit=100",
            $"cursor={firstPage.NextCursor}&limit=100",
            month + "&entity=payments&action=Delete&q=Pending&limit=100",
        ];

        foreach (string query in queries)
        {
            var timer = Stopwatch.StartNew();
            using HttpResponseMessage response = await manager.Client.GetAsync(new Uri($"{Changes}?{query}", UriKind.Relative));
            timer.Stop();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(3), $"{query}: {timer.ElapsedMilliseconds} ms");
        }

        AuditPageResponse<AuditChangeResponse> found = await GetAsync<AuditChangeResponse>(manager.Client, $"{Changes}?q=Seed99999");
        Assert.Equal("-1099999", Assert.Single(found.Items).Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    private static async Task<AuditPageResponse<T>> GetAsync<T>(HttpClient client, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<AuditPageResponse<T>>() ?? throw new InvalidOperationException("No body.");
    }
}
