using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Milkshake.Api;
using Milkshake.Api.Contracts.Management;
using Milkshake.Api.Contracts.Ordering;
using Milkshake.Application.Notifications;
using Milkshake.Application.Payments;
using Milkshake.Domain.Notifications;
using Milkshake.Domain.Payments;
using Milkshake.Domain.Settings;
using Milkshake.IntegrationTests.Infrastructure;
using Milkshake.Infrastructure.Payments;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M11, AC-11.5 against disposable PostgreSQL 17: the audited tables are enumerated from the database and the EF model,
/// and a broad run of the application's real write paths must produce exactly one audit record per row written – for
/// every table and every action. A write path that bypasses the audit (raw SQL, a bulk update, a table outside the
/// model) fails these tests.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class AuditCoverageTests(PostgresApiFactory factory) : IDisposable
{
    private const string Key = "sk_test_integration_tests_only";

    /// <summary>Tables that are not audited by the interceptor, each for a stated reason.</summary>
    private static readonly Dictionary<string, string> NotAudited = new(StringComparer.Ordinal)
    {
        ["audit_logs"] = "the audit trail itself (append-only)",
        ["security_events"] = "the security trail itself (append-only)",
        ["__ef_migrations_history"] = "schema history written by EF migrations, not by the application",
    };

    /// <summary>Audited tables the application has no write path to today (Identity features not used, seeding only).</summary>
    private static readonly Dictionary<string, string> NoApplicationWritePath = new(StringComparer.Ordinal)
    {
        ["roles"] = "created by the seeder only",
        ["role_claims"] = "Identity feature not used",
        ["user_claims"] = "Identity feature not used",
        ["user_logins"] = "external logins not used",
        ["user_tokens"] = "token features deferred (D5)",
    };

    private readonly FakePaymentGateway _gateway = new();
    private readonly FakeEmailSender _sender = new();
    private WebApplicationFactory<ApiAssemblyMarker>? _app;

    private WebApplicationFactory<ApiAssemblyMarker> App => _app ??= factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Payments:Paystack:ReturnBaseUrl", "https://localhost:5173/");
        builder.UseSetting("Payments:Paystack:SecretKey", Key);
        builder.UseSetting("Payments:Reconciliation:BatchSize", "10000");
        builder.UseSetting("Email:PublicBaseUrl", "https://shop.example.test/");
        builder.UseSetting("Email:Dispatch:BatchSize", "10000");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IPaymentGateway>(_gateway);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(_sender);
        });
    });

    public void Dispose() => _app?.Dispose();

    [Fact]
    public async Task EveryTableInTheSchema_IsAuditedOrExplicitlyExcluded()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = Db(scope);

        List<string> tables = await db.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value" FROM information_schema.tables
            WHERE table_schema = 'milkshake' AND table_type = 'BASE TABLE'
            """).ToListAsync();
        SortedSet<string> modelTables = ModelTables(db);

        // A table without an entity can only be written by raw SQL, which the interceptor never sees.
        List<string> outsideTheModel = tables.Where(t => !modelTables.Contains(t) && !NotAudited.ContainsKey(t)).Order(StringComparer.Ordinal).ToList();
        List<string> missingFromTheDatabase = modelTables.Where(t => !tables.Contains(t)).ToList();
        Assert.Empty(outsideTheModel);
        Assert.Empty(missingFromTheDatabase);
        Assert.All(NotAudited.Keys, t => Assert.Contains(t, tables));
    }

    [Fact]
    public async Task EveryWriteOnEveryAuditedTable_ProducesExactlyOneAuditRecord()
    {
        await using AsyncServiceScope setup = factory.Services.CreateAsyncScope();
        SortedSet<string> audited = AuditedTables(Db(setup));
        await InstallWriteCountersAsync(audited);
        try
        {
            await RunTheApplicationsWritePathsAsync();

            Dictionary<(string Table, string Action), long> writes = await CountsAsync("writes");
            Dictionary<(string Table, string Action), long> audits = await CountsAsync("audits");

            List<string> differences = writes.Keys.Union(audits.Keys)
                .Where(key => writes.GetValueOrDefault(key) != audits.GetValueOrDefault(key))
                .Select(key => $"{key.Table} {key.Action}: {writes.GetValueOrDefault(key)} written, {audits.GetValueOrDefault(key)} audited")
                .Order(StringComparer.Ordinal)
                .ToList();
            Assert.Empty(differences);

            // Breadth: the run reached every audited table that has an application write path.
            List<string> notExercised = audited
                .Where(t => !NoApplicationWritePath.ContainsKey(t) && !writes.Keys.Any(k => k.Table == t))
                .ToList();
            Assert.Empty(notExercised);
            Assert.All(new[] { "Insert", "Update", "Delete" }, action => Assert.Contains(writes.Keys, k => k.Action == action));
        }
        finally
        {
            await ExecuteAsync("DROP SCHEMA IF EXISTS audit_coverage CASCADE");
        }
    }

    // ---- the write paths -----------------------------------------------------------------------------------------

    private async Task RunTheApplicationsWritePathsAsync()
    {
        // Management (M10): catalogue, prices, restaurants, settings, discount tiers.
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);

        CatalogItemResponse renamed = await SendAsync<CatalogItemResponse>(manager.Client, HttpMethod.Put, $"/api/manager/catalog/flavours/{menu.Flavour.Id}",
            new { name = menu.Flavour.Name + " renamed", displayOrder = menu.Flavour.DisplayOrder, version = menu.Flavour.Version });
        Assert.EndsWith(" renamed", renamed.Name, StringComparison.Ordinal);

        CatalogItemResponse spare = await manager.CreateTestItemAsync("toppings", 3.00m);
        CatalogItemResponse hidden = await SendAsync<CatalogItemResponse>(manager.Client, HttpMethod.Post, $"/api/manager/catalog/toppings/{spare.Id}/deactivate", new { version = spare.Version });
        CatalogItemResponse shown = await SendAsync<CatalogItemResponse>(manager.Client, HttpMethod.Post, $"/api/manager/catalog/toppings/{spare.Id}/activate", new { version = hidden.Version });

        // Whole seconds, so the start time round-trips through PostgreSQL unchanged.
        DateTimeOffset later = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeSeconds());
        PriceHistoryResponse scheduled = await SendAsync<PriceHistoryResponse>(manager.Client, HttpMethod.Post, $"/api/manager/catalog/toppings/{spare.Id}/prices",
            new { amount = 4.00m, effectiveFrom = later, version = shown.Version });
        PricePeriodResponse future = scheduled.Prices.Single(p => p.CanCancel);
        await SendAsync<PriceHistoryResponse>(manager.Client, HttpMethod.Post, $"/api/manager/catalog/toppings/{spare.Id}/prices/{future.Id}/cancel",
            new { version = scheduled.Item.Version });

        RestaurantResponse branch = await manager.CreateTestRestaurantAsync();
        await SendAsync<RestaurantResponse>(manager.Client, HttpMethod.Put, $"/api/manager/restaurants/{branch.Id}",
            new { name = branch.Name + " renamed", address = "1 Audit Road", version = branch.Version });

        List<SettingResponse> settings = await manager.Client.GetFromJsonAsync<List<SettingResponse>>(new Uri("/api/manager/settings", UriKind.Relative))
            ?? throw new InvalidOperationException("No settings returned.");
        string maxDrinks = settings.Single(s => s.Key == SettingKeys.MaxDrinksPerOrder).Value;
        await manager.ChangeSettingAsync(SettingKeys.MaxDrinksPerOrder, maxDrinks == "9" ? "8" : "9");
        await manager.ChangeSettingAsync(SettingKeys.MaxDrinksPerOrder, maxDrinks);

        List<DiscountTierResponse> tiers = await manager.Client.GetFromJsonAsync<List<DiscountTierResponse>>(new Uri("/api/manager/discount-tiers", UriKind.Relative))
            ?? throw new InvalidOperationException("No tiers returned.");
        DiscountTierResponse tier = tiers.Single(t => t.TierNumber == 3);
        DiscountTierResponse changed = await SendAsync<DiscountTierResponse>(manager.Client, HttpMethod.Put, "/api/manager/discount-tiers/3",
            new { minPreviousOrders = tier.MinPreviousOrders + 1, minDrinksPerOrder = tier.MinDrinksPerOrder, discountRate = tier.DiscountRate, version = tier.Version });
        await SendAsync<DiscountTierResponse>(manager.Client, HttpMethod.Put, "/api/manager/discount-tiers/3",
            new { minPreviousOrders = tier.MinPreviousOrders, minDrinksPerOrder = tier.MinDrinksPerOrder, discountRate = tier.DiscountRate, version = changed.Version });

        // Patron (M4, M7, M8, M8b): registration, orders, payments by webhook and by refresh, cancellations, requests.
        using HttpClient patron = App.CreateSecureClient();
        using (HttpResponseMessage registered = await patron.PostWithCsrfAsync("/api/auth/register", RegisterBody.Valid()))
        {
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        }

        string first = await PlaceOrderAsync(patron, menu);
        string firstPayment = await StartPaymentAsync(patron, first);
        _gateway.OnVerify = reference => reference == firstPayment ? FakePaymentGateway.Paid(2990) : Open();
        using (HttpResponseMessage delivered = await DeliverWebhookAsync(firstPayment))
        {
            Assert.Equal(HttpStatusCode.OK, delivered.StatusCode);
        }

        await PostAsync(patron, $"/api/ordering/orders/{first}/cancel", new { expectedRefund = 27.30m });

        string second = await PlaceOrderAsync(patron, menu);
        await PayByRefreshAsync(patron, second);
        await PostAsync(manager.Client, $"/api/manager/orders/{second}/cancellation-requests", new { reason = "Out of milk" }, HttpStatusCode.Created);
        await PostAsync(patron, $"/api/ordering/orders/{second}/cancellation-request/accept", new { expectedRefund = 29.90m });

        string third = await PlaceOrderAsync(patron, menu);
        await PostAsync(manager.Client, $"/api/manager/orders/{third}/cancellation-requests", new { reason = "Closing early" }, HttpStatusCode.Created);
        await PostAsync(patron, $"/api/ordering/orders/{third}/cancellation-request/decline", new { });

        // Background work (M8b, M9): refund completion by reconciliation, email delivery, a manager resend.
        string firstRefund;
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            int firstOrderId = await db.Orders.Where(o => o.Reference == first).Select(o => o.Id).SingleAsync();
            firstRefund = await db.Refunds.Where(r => r.OrderId == firstOrderId).Select(r => r.Reference).SingleAsync();
        }

        _gateway.OnVerify = _ => Open();
        _gateway.OnGetRefund = reference => reference == firstRefund
            ? new GatewayRefund(true, VerifiedRefundOutcome.Processed, 2730, null, null)
            : new GatewayRefund(true, VerifiedRefundOutcome.Pending, 0, null, null);
        await using (AsyncServiceScope scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPaymentReconciler>().ReconcileAsync(CancellationToken.None);
        }

        await using (AsyncServiceScope scope = App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IEmailDispatcher>().DispatchAsync(CancellationToken.None);
        }

        int sentEmail;
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext db = Db(scope);
            int firstOrderId = await db.Orders.Where(o => o.Reference == first).Select(o => o.Id).SingleAsync();
            sentEmail = await db.EmailNotifications
                .Where(n => n.OrderId == firstOrderId && n.Status == NotificationStatus.Sent)
                .Select(n => n.Id).FirstAsync();
        }

        await PostAsync(manager.Client, $"/api/manager/emails/{sentEmail}/resend", new { });
    }

    private static GatewayVerification Open() => new(true, VerifiedOutcome.Open, 0, "ZAR", null, null);

    private async Task<string> PlaceOrderAsync(HttpClient patron, TestMenu menu)
    {
        OrderingOptionsResponse options = await patron.GetFromJsonAsync<OrderingOptionsResponse>(new Uri("/api/ordering/options", UriKind.Relative))
            ?? throw new InvalidOperationException("No options returned.");
        var draft = new
        {
            restaurantId = menu.Restaurant.Id,
            pickupLocal = options.Pickup.LatestLocal,
            drinks = new[] { new { flavourId = menu.Flavour.Id, consistencyId = menu.Consistency.Id, toppingId = menu.Topping.Id } },
            expectedTotal = 29.90m,
            expectedCurrencyCode = "ZAR",
        };
        using HttpResponseMessage placed = await PostRawAsync(patron, "/api/ordering/orders", JsonSerializer.Serialize(draft), idempotent: true);
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        return (await placed.ReadJsonAsync<OrderResponse>()).Reference;
    }

    private async Task<string> StartPaymentAsync(HttpClient patron, string order)
    {
        using HttpResponseMessage started = await PostRawAsync(patron, $"/api/ordering/orders/{order}/payments", "{}", idempotent: true);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        return (await started.ReadJsonAsync<PaymentCheckoutResponse>()).PaymentReference;
    }

    private async Task PayByRefreshAsync(HttpClient patron, string order)
    {
        string payment = await StartPaymentAsync(patron, order);
        _gateway.OnVerify = reference => reference == payment ? FakePaymentGateway.Paid(2990) : Open();
        await PostAsync(patron, $"/api/ordering/orders/{order}/payments/refresh", new { });
        _gateway.OnVerify = _ => Open();
    }

    private async Task<HttpResponseMessage> DeliverWebhookAsync(string paymentReference)
    {
        long transactionId = Random.Shared.NextInt64(1_000_000, long.MaxValue);
        string body = "{\"event\":\"charge.success\",\"data\":{\"id\":" + transactionId.ToString(CultureInfo.InvariantCulture) +
                      ",\"status\":\"success\",\"reference\":\"" + paymentReference + "\",\"amount\":2990,\"currency\":\"ZAR\"," +
                      "\"customer\":{\"email\":\"x@example.test\"}}}";
        using HttpClient provider = App.CreateSecureClient(handleCookies: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/payments/paystack/webhook", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(PaystackWebhookAuthenticator.SignatureHeader,
            PaystackWebhookAuthenticatorTests.Sign(Encoding.UTF8.GetBytes(body), Key));
        return await provider.SendAsync(request);
    }

    private static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string path, object body)
    {
        using HttpResponseMessage response = await client.SendWithCsrfAsync(method, path, body);
        Assert.True(response.IsSuccessStatusCode, $"{method} {path}: {(int)response.StatusCode}");
        return await response.ReadJsonAsync<T>();
    }

    private static async Task PostAsync(HttpClient client, string path, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using HttpResponseMessage response = await PostRawAsync(client, path, JsonSerializer.Serialize(body), idempotent: false);
        Assert.True(response.StatusCode == expected, $"POST {path}: {(int)response.StatusCode}");
    }

    private static async Task<HttpResponseMessage> PostRawAsync(HttpClient client, string path, string json, bool idempotent)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-XSRF-TOKEN", await client.GetCsrfTokenAsync());
        if (idempotent)
        {
            request.Headers.Add(OrderMessages.IdempotencyKeyHeader, Guid.NewGuid().ToString());
        }

        return await client.SendAsync(request);
    }

    // ---- counters ------------------------------------------------------------------------------------------------

    private static SortedSet<string> ModelTables(MilkshakeDbContext db) =>
        new(db.Model.GetEntityTypes()
            .Where(e => e.GetTableName() is not null && e.GetSchema() == MilkshakeDbContext.Schema)
            .Select(e => e.GetTableName()!), StringComparer.Ordinal);

    private static SortedSet<string> AuditedTables(MilkshakeDbContext db) =>
        new(ModelTables(db).Where(t => !NotAudited.ContainsKey(t)), StringComparer.Ordinal);

    /// <summary>Test database only: count every row written to an audited table, and every audit record written.</summary>
    private async Task InstallWriteCountersAsync(IEnumerable<string> audited)
    {
        await ExecuteAsync("DROP SCHEMA IF EXISTS audit_coverage CASCADE");
        await ExecuteAsync("""
            CREATE SCHEMA audit_coverage;
            CREATE TABLE audit_coverage.writes (table_name text NOT NULL, action text NOT NULL);
            CREATE TABLE audit_coverage.audits (table_name text NOT NULL, action text NOT NULL);
            CREATE FUNCTION audit_coverage.count_write() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF TG_TABLE_NAME = 'audit_logs' THEN
                    INSERT INTO audit_coverage.audits VALUES (NEW.table_name, NEW.action);
                ELSE
                    INSERT INTO audit_coverage.writes VALUES (TG_TABLE_NAME, initcap(lower(TG_OP)));
                END IF;
                RETURN NULL;
            END;
            $$;
            CREATE TRIGGER zz_audit_coverage AFTER INSERT ON milkshake.audit_logs
                FOR EACH ROW EXECUTE FUNCTION audit_coverage.count_write();
            """);
        foreach (string table in audited)
        {
            await ExecuteAsync($"CREATE TRIGGER zz_audit_coverage AFTER INSERT OR UPDATE OR DELETE ON milkshake.\"{table}\" " +
                               "FOR EACH ROW EXECUTE FUNCTION audit_coverage.count_write()");
        }
    }

    private async Task<Dictionary<(string Table, string Action), long>> CountsAsync(string counter)
    {
        // Fixed statements: the counter table is never interpolated into SQL.
        string sql = counter switch
        {
            "writes" => "SELECT table_name AS \"Table\", action AS \"Action\", count(*) AS \"Count\" FROM audit_coverage.writes GROUP BY 1, 2",
            "audits" => "SELECT table_name AS \"Table\", action AS \"Action\", count(*) AS \"Count\" FROM audit_coverage.audits GROUP BY 1, 2",
            _ => throw new ArgumentOutOfRangeException(nameof(counter)),
        };
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        List<CountRow> rows = await Db(scope).Database.SqlQueryRaw<CountRow>(sql).ToListAsync();
        return rows.ToDictionary(r => (r.Table, r.Action), r => r.Count);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await Db(scope).Database.ExecuteSqlRawAsync(sql);
    }

    private static MilkshakeDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();

    private sealed record CountRow(string Table, string Action, long Count);
}
