using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api;
using Milkshake.Api.Contracts.Management;
using Milkshake.Api.Contracts.Ordering;
using Milkshake.IntegrationTests.Infrastructure;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.IntegrationTests;

/// <summary>
/// M12 management reports against disposable PostgreSQL 17: orders per date or range (AC-12.2) and volumes by weekday,
/// month and year (AC-12.3) for business-local days, reconciled with direct SQL on seeded orders (AC-12.6, T12.6);
/// managers only; strict parameters; no patron details; and answers within 3 seconds over 50,000 orders (decision R8).
/// Seeded orders are moved to 2041 and 2045 so they never mix with orders other tests place today.
/// </summary>
[Collection(PostgresTestGroup.Name)]
public sealed class ManagerReportsTests(PostgresApiFactory factory)
{
    private const string OrdersReport = "/api/manager/reports/orders";
    private const string TrendsReport = "/api/manager/reports/trends";
    private const string Zone = "Africa/Johannesburg";

    // ---- reconciliation -----------------------------------------------------------------------------------------

    [Fact]
    public async Task TheOrdersReport_ListsEachMilkshakeOfTheLocalDays_WithFiltersPagingAndPaymentStatus()
    {
        Seeded s = await SeedAsync();
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        string mine = $"&q={Uri.EscapeDataString(s.Menu.Flavour.Name)}";

        OrderReportResponse range = await GetAsync<OrderReportResponse>(manager.Client, $"{OrdersReport}?from=2041-03-04&to=2041-03-06{mine}");

        Assert.Equal(("2041-03-04", "2041-03-06", Zone, 4, 5, 1, 1), (range.From, range.To, range.TimeZone, range.TotalOrders, range.TotalDrinks, range.TotalPages, range.Page));
        Assert.Equal(
            new[]
            {
                (s.D, "2041-03-06", "12:00", 1, "Unpaid", (string?)null),
                (s.C, "2041-03-05", "00:30", 1, "Cancelled", "NotPaid"),   // 22:30 UTC on the 4th is already the 5th locally
                (s.B, "2041-03-04", "23:30", 1, "Unpaid", null),
                (s.A, "2041-03-04", "10:00", 1, "Paid", null),
                (s.A, "2041-03-04", "10:00", 2, "Paid", null),
            },
            range.Rows.Select(r => (r.Reference, r.Date, r.Time, r.DrinkNumber, r.PaymentStatus, r.RefundStatus)));
        Assert.All(range.Rows, r => Assert.Equal(
            (s.Menu.Flavour.Name, s.Menu.Topping.Name, s.Menu.Consistency.Name, s.Menu.Restaurant.Name),
            (r.Flavour, r.Topping, r.Consistency, r.Restaurant)));

        // A single date; the minute before local midnight belongs to the earlier day.
        OrderReportResponse fifth = await GetAsync<OrderReportResponse>(manager.Client, $"{OrdersReport}?from=2041-03-05{mine}");
        OrderReportResponse third = await GetAsync<OrderReportResponse>(manager.Client, $"{OrdersReport}?from=2041-03-03{mine}");
        Assert.Equal(new[] { s.C }, fifth.Rows.Select(r => r.Reference));
        Assert.Equal(new[] { (s.F, "2041-03-03", "23:59") }, third.Rows.Select(r => (r.Reference, r.Date, r.Time)));

        // Payment filters.
        Assert.Equal((1, 2), Totals(await GetAsync<OrderReportResponse>(manager.Client, $"{OrdersReport}?from=2041-03-04&to=2041-03-06&status=paid{mine}")));
        Assert.Equal((2, 2), Totals(await GetAsync<OrderReportResponse>(manager.Client, $"{OrdersReport}?from=2041-03-04&to=2041-03-06&status=unpaid{mine}")));
        Assert.Equal((1, 1), Totals(await GetAsync<OrderReportResponse>(manager.Client, $"{OrdersReport}?from=2041-03-04&to=2041-03-06&status=cancelled{mine}")));

        // Search by reference finds the order's milkshakes; numbered pages keep the totals.
        OrderReportResponse byReference = await GetAsync<OrderReportResponse>(manager.Client, $"{OrdersReport}?from=2041-03-04&q={s.A.ToLowerInvariant()}");
        Assert.Equal((1, 2), Totals(byReference));
        OrderReportResponse lastPage = await GetAsync<OrderReportResponse>(manager.Client, $"{OrdersReport}?from=2041-03-04&to=2041-03-06&pageSize=2&page=3{mine}");
        Assert.Equal((3, 4, 5), (lastPage.TotalPages, lastPage.TotalOrders, lastPage.TotalDrinks));
        Assert.Equal(new[] { (s.A, 2) }, lastPage.Rows.Select(r => (r.Reference, r.DrinkNumber)));
        OrderReportResponse beyond = await GetAsync<OrderReportResponse>(manager.Client, $"{OrdersReport}?from=2041-03-04&to=2041-03-06&pageSize=2&page=9{mine}");
        Assert.Empty(beyond.Rows);
        Assert.Equal(5, beyond.TotalDrinks);

        // Reconciliation (T12.6): the same totals straight from the database, grouped by local date in SQL.
        (int orders, int drinks) = await CountInDatabaseAsync(s.Menu.Flavour.Name, new DateOnly(2041, 3, 4), new DateOnly(2041, 3, 6));
        Assert.Equal((orders, drinks), (range.TotalOrders, range.TotalDrinks));
    }

    [Fact]
    public async Task Trends_CountOrdersByLocalWeekdayMonthAndYear_AndReconcileWithTheDatabase()
    {
        // Its own year, so the orders other tests seed in 2041 are not counted.
        await SeedAsync(year: 2042);
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        OrderTrendsResponse trends = await GetAsync<OrderTrendsResponse>(manager.Client, $"{TrendsReport}?from=2042-03-01&to=2042-04-30");

        // Seeded: Mon 3 Mar (unpaid), Tue 4 Mar (paid, unpaid), Wed 5 Mar (cancelled; 22:30 UTC on Tuesday), Thu 6 Mar (failed),
        // Thu 10 Apr (paid).
        Assert.Equal(("2042-03-01", "2042-04-30", Zone, 6, 2), (trends.From, trends.To, trends.TimeZone, trends.TotalOrders, trends.TotalPaidOrders));
        Assert.Equal(
            new[] { ("Monday", 1, 0), ("Tuesday", 2, 1), ("Wednesday", 1, 0), ("Thursday", 2, 1), ("Friday", 0, 0), ("Saturday", 0, 0), ("Sunday", 0, 0) },
            trends.Weekdays.Select(p => (p.Label, p.Orders, p.PaidOrders)));
        Assert.Equal(new[] { ("2042-03", 5, 1), ("2042-04", 1, 1) }, trends.Months.Select(p => (p.Label, p.Orders, p.PaidOrders)));
        Assert.Equal(new[] { ("2042", 6, 2) }, trends.Years.Select(p => (p.Label, p.Orders, p.PaidOrders)));

        // "None": every order, still adding up with the database.
        OrderTrendsResponse all = await GetAsync<OrderTrendsResponse>(manager.Client, TrendsReport);
        (int total, int paid) = await CountAllInDatabaseAsync();
        Assert.Null(all.From);
        Assert.Null(all.To);
        Assert.Equal((total, paid), (all.TotalOrders, all.TotalPaidOrders));
        Assert.Equal(total, all.Weekdays.Sum(p => p.Orders));
        Assert.Equal(total, all.Months.Sum(p => p.Orders));
        Assert.Equal(paid, all.Years.Sum(p => p.PaidOrders));
        Assert.Contains(all.Months, p => p.Label == "2042-03" && p.Orders >= 5);
    }

    // ---- access, parameters and privacy -------------------------------------------------------------------------

    [Fact]
    public async Task OnlyManagers_CanReadReports_AndNothingIsCached()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        using HttpClient patron = await factory.CreatePatronClientAsync();
        using HttpClient anonymous = factory.CreateHttpsClient();

        foreach (string path in new[] { $"{OrdersReport}?from=2041-03-04", TrendsReport })
        {
            using HttpResponseMessage asManager = await manager.Client.GetAsync(new Uri(path, UriKind.Relative));
            using HttpResponseMessage asPatron = await patron.GetAsync(new Uri(path, UriKind.Relative));
            using HttpResponseMessage asAnonymous = await anonymous.GetAsync(new Uri(path, UriKind.Relative));

            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized),
                (asManager.StatusCode, asPatron.StatusCode, asAnonymous.StatusCode));
            Assert.Contains("no-store", asManager.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ReportRows_CarryNoPatronDetails()
    {
        Seeded s = await SeedAsync();
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        using HttpResponseMessage response = await manager.Client.GetAsync(
            new Uri($"{OrdersReport}?from=2041-03-04&q={Uri.EscapeDataString(s.Menu.Flavour.Name)}", UriKind.Relative));
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        JsonElement row = body.RootElement.GetProperty("rows")[0];
        Assert.Equal(
            new[] { "reference", "date", "time", "placedAtUtc", "pickupLocal", "restaurant", "drinkNumber", "flavour", "topping", "consistency", "paymentStatus", "refundStatus" },
            row.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("@", body.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OrdersReport, "", "from")]
    [InlineData(OrdersReport, "?from=2041-3-4", "from")]
    [InlineData(OrdersReport, "?from=2041-03-06&to=2041-03-04", "to")]
    [InlineData(OrdersReport, "?from=2040-01-01&to=2041-01-01", "to")]
    [InlineData(OrdersReport, "?from=2041-03-04&status=refunded", "status")]
    [InlineData(OrdersReport, "?from=2041-03-04&pageSize=51", "pageSize")]
    [InlineData(OrdersReport, "?from=2041-03-04&patronId=1", "patronId")]
    [InlineData(OrdersReport, "?from=2041-03-04&from=2041-03-05", "from")]
    [InlineData(TrendsReport, "?to=2041-03-04", "from")]
    [InlineData(TrendsReport, "?from=2041-03-04&status=paid", "status")]
    public async Task InvalidParameters_AreRejectedOnTheirField(string path, string query, string field)
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        using HttpResponseMessage response = await manager.Client.GetAsync(new Uri(path + query, UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(field, out _), problem.RootElement.GetRawText());
    }

    [Fact]
    public async Task SearchText_IsMatchedLiterally()
    {
        Seeded s = await SeedAsync();
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        // "%" and "_" are not wildcards: they match nothing in the seeded names.
        OrderReportResponse percent = await GetAsync<OrderReportResponse>(manager.Client, $"{OrdersReport}?from=2041-03-04&to=2041-03-06&q=%25");
        OrderReportResponse underscore = await GetAsync<OrderReportResponse>(manager.Client, $"{OrdersReport}?from=2041-03-04&to=2041-03-06&q=_");

        Assert.Equal((0, 0), Totals(percent));
        Assert.Equal((0, 0), Totals(underscore));
        Assert.Equal((1, 2), Totals(await GetAsync<OrderReportResponse>(manager.Client, $"{OrdersReport}?from=2041-03-04&q={s.A}")));
    }

    [Fact]
    public async Task Reports_AreRateLimitedPerManager()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        using WebApplicationFactory<ApiAssemblyMarker> app = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:ReportsPermitLimit", "2"));
        using HttpClient client = app.CreateSecureClient();
        using (HttpResponseMessage login = await client.PostWithCsrfAsync("/api/auth/login", new LoginBody(manager.Email, RegisterBody.ValidPassword)))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        var statuses = new List<HttpStatusCode>();
        foreach (string path in new[] { TrendsReport, $"{OrdersReport}?from=2041-03-04", TrendsReport })
        {
            using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests }, statuses);
    }

    // ---- exports (AC-12.6) --------------------------------------------------------------------------------------

    [Fact]
    public async Task TheOrdersExport_HasEveryRowOfTheFilter_AsCsvForSpreadsheets_AndIsRecorded()
    {
        Seeded s = await SeedAsync();
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        using HttpResponseMessage response = await manager.Client.GetAsync(new Uri(
            $"{OrdersReport}/export?from=2041-03-04&to=2041-03-06&pageSize=1&q={Uri.EscapeDataString(s.Menu.Flavour.Name)}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("milkshake-orders_2041-03-04_2041-03-06.csv", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(System.Text.Encoding.UTF8.GetPreamble(), bytes[..3]);
        string[] lines = System.Text.Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Date,Time,Order,Milkshake,Flavour,Topping,Consistency,Restaurant,Pickup,Payment status,Refund", lines[0]);
        Assert.Equal(6, lines.Length);   // all five milkshakes, although the screen's page size was 1
        Assert.StartsWith($"2041-03-05,00:30,{s.C},1,{s.Menu.Flavour.Name},", lines[2], StringComparison.Ordinal);
        Assert.EndsWith(",Cancelled,Not paid", lines[2], StringComparison.Ordinal);
        Assert.EndsWith(",Paid,", lines[4], StringComparison.Ordinal);
        Assert.DoesNotContain("@", string.Join('\n', lines), StringComparison.Ordinal);

        AuditPageResponse<SecurityEventResponse> recorded = await GetAsync<AuditPageResponse<SecurityEventResponse>>(manager.Client,
            $"/api/manager/audit/security?eventType=ReportExported&user={Uri.EscapeDataString(manager.Email)}");
        Assert.NotEmpty(recorded.Items);
    }

    [Fact]
    public async Task TheTrendsExport_ListsEachWeekdayMonthAndYear()
    {
        await SeedAsync(year: 2043);
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        string csv = await ExportTextAsync(manager.Client, $"{TrendsReport}/export?from=2043-03-01&to=2043-04-30");

        string[] lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Series,Period,Orders,Paid orders", lines[0]);
        Assert.Equal(7 + 2 + 1, lines.Length - 1);
        Assert.Contains("Month,2043-03,5,1", lines);
        Assert.Contains("Year,2043,6,2", lines);
    }

    [Fact]
    public async Task TheAuditExports_HaveOneLinePerChangedField_AndMaskPersonalData()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        using HttpClient patron = await factory.CreatePatronClientAsync();

        string changes = await ExportTextAsync(manager.Client, $"/api/manager/reports/audit/changes/export?entity=flavours&q={Uri.EscapeDataString(menu.Flavour.Name)}");
        string[] lines = changes.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Occurred at (UTC),Action,Table,Record,Made by,User,Field,From,To", lines[0]);
        Assert.Contains(lines, l => l.Contains($",Insert,flavours,{menu.Flavour.Id},user,{manager.Email},name,,{menu.Flavour.Name}", StringComparison.Ordinal));

        string users = await ExportTextAsync(manager.Client, "/api/manager/reports/audit/changes/export?entity=users&action=Insert");
        Assert.Contains(",email,,***", users, StringComparison.Ordinal);

        string signIns = await ExportTextAsync(manager.Client, $"/api/manager/reports/audit/security/export?user={Uri.EscapeDataString(manager.Email)}&eventType=LoginSucceeded");
        Assert.StartsWith("Occurred at (UTC),Event,User,Address\r\n", signIns, StringComparison.Ordinal);
        Assert.Contains($",LoginSucceeded,{manager.Email},", signIns, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/manager/reports/orders/export", "?to=2041-03-04", HttpStatusCode.BadRequest)]
    [InlineData("/api/manager/reports/trends/export", "?status=paid", HttpStatusCode.BadRequest)]
    [InlineData("/api/manager/reports/audit/changes/export", "?entity=no_such_table", HttpStatusCode.BadRequest)]
    [InlineData("/api/manager/reports/audit/security/export", "?eventType=Hacked", HttpStatusCode.BadRequest)]
    public async Task InvalidExportFilters_AreRejected(string path, string query, HttpStatusCode expected)
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        using HttpResponseMessage response = await manager.Client.GetAsync(new Uri(path + query, UriKind.Relative));

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OnlyManagers_CanExport()
    {
        using HttpClient patron = await factory.CreatePatronClientAsync();
        using HttpClient anonymous = factory.CreateHttpsClient();

        foreach (string path in new[] { $"{OrdersReport}/export?from=2041-03-04", $"{TrendsReport}/export", "/api/manager/reports/audit/changes/export", "/api/manager/reports/audit/security/export" })
        {
            using HttpResponseMessage asPatron = await patron.GetAsync(new Uri(path, UriKind.Relative));
            using HttpResponseMessage asAnonymous = await anonymous.GetAsync(new Uri(path, UriKind.Relative));
            Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized), (asPatron.StatusCode, asAnonymous.StatusCode));
        }
    }

    [Fact]
    public async Task Exports_HaveTheirOwnStricterRateLimit()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        using WebApplicationFactory<ApiAssemblyMarker> app = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:ExportPermitLimit", "1"));
        using HttpClient client = app.CreateSecureClient();
        using (HttpResponseMessage login = await client.PostWithCsrfAsync("/api/auth/login", new LoginBody(manager.Email, RegisterBody.ValidPassword)))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        var statuses = new List<HttpStatusCode>();
        foreach (string path in new[] { $"{TrendsReport}/export?from=2041-03-04", $"{TrendsReport}/export?from=2041-03-04", $"{TrendsReport}?from=2041-03-04" })
        {
            using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
            statuses.Add(response.StatusCode);
        }

        // The second export is refused; the report itself still answers (its own limit).
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.OK }, statuses);
    }

    // ---- performance (decision R8) ------------------------------------------------------------------------------

    [Fact]
    public async Task OverFiftyThousandOrders_EveryReportAnswersWithinThreeSeconds()
    {
        // 50,000 orders of two milkshakes each, every 10 minutes through 2045, copied from one real order. Ids from
        // 900,000,001 never meet the ids the application allocates; references MS-ZZxx-xxxx are reserved for this seed.
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        using HttpClient patron = await factory.CreatePatronClientAsync();
        string template = await PlaceAsync(patron, menu, drinks: 1);

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MilkshakeDbContext seed = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
            seed.Database.SetCommandTimeout(TimeSpan.FromMinutes(3));   // setup only; the timed requests below keep the 3-second target
            await seed.Database.ExecuteSqlAsync($"""
                INSERT INTO milkshake.orders (id, created_at, currency_code, discount_amount, discount_cap_applied, discount_rate,
                    discount_tier_number, idempotency_key, patron_id, pickup_at_utc, pickup_time_zone, priced_at_utc, reference,
                    request_fingerprint, restaurant_address, restaurant_id, restaurant_name, status, subtotal, taxable, total,
                    updated_at, vat, vat_rate)
                SELECT 900000000 + g,
                       timestamptz '2045-01-01 00:00:00+00' + make_interval(secs => g * 600),
                       t.currency_code, t.discount_amount, t.discount_cap_applied, t.discount_rate, t.discount_tier_number,
                       gen_random_uuid(), t.patron_id, t.pickup_at_utc, t.pickup_time_zone, t.priced_at_utc,
                       'MS-ZZ' || substr(x.h, 1, 2) || '-' || substr(x.h, 3, 4),
                       md5(g::text) || md5((g + 1)::text),
                       t.restaurant_address, t.restaurant_id, t.restaurant_name,
                       (ARRAY['Confirmed', 'PendingPayment', 'Cancelled', 'PaymentFailed'])[1 + g % 4],
                       t.subtotal, t.taxable, t.total, t.updated_at, t.vat, t.vat_rate
                FROM milkshake.orders AS t
                CROSS JOIN generate_series(1, 50000) AS g
                CROSS JOIN LATERAL (SELECT upper(lpad(to_hex(g), 6, '0')) AS h) AS x
                WHERE t.reference = {template}
                  AND NOT EXISTS (SELECT 1 FROM milkshake.orders WHERE id = 900000001)
                """);
            await seed.Database.ExecuteSqlAsync($"""
                INSERT INTO milkshake.order_items (id, consistency_fee, consistency_id, consistency_name, flavour_fee, flavour_id,
                    flavour_name, number, order_id, price, topping_fee, topping_id, topping_name)
                SELECT 900000000 + g * 2 + n, i.consistency_fee, i.consistency_id, i.consistency_name, i.flavour_fee, i.flavour_id,
                       i.flavour_name, n + 1, 900000000 + g, i.price, i.topping_fee, i.topping_id, i.topping_name
                FROM milkshake.order_items AS i
                JOIN milkshake.orders AS t ON t.id = i.order_id
                CROSS JOIN generate_series(1, 50000) AS g
                CROSS JOIN generate_series(0, 1) AS n
                WHERE t.reference = {template}
                  AND NOT EXISTS (SELECT 1 FROM milkshake.order_items WHERE id = 900000002)
                """);
            await seed.Database.ExecuteSqlRawAsync("ANALYZE milkshake.orders; ANALYZE milkshake.order_items;");
        }

        string[] paths =
        [
            $"{OrdersReport}?from=2045-01-01&to=2045-12-31",
            $"{OrdersReport}?from=2045-01-01&to=2045-12-31&page=2000&pageSize=50",
            $"{OrdersReport}?from=2045-01-01&to=2045-12-31&status=cancelled",
            $"{OrdersReport}?from=2045-01-01&to=2045-12-31&q=ZZ00-C350",
            $"{OrdersReport}?from=2045-06-15",
            $"{TrendsReport}?from=2045-01-01&to=2045-12-31",
            TrendsReport,
        ];

        foreach (string path in paths)
        {
            var timer = Stopwatch.StartNew();
            using HttpResponseMessage response = await manager.Client.GetAsync(new Uri(path, UriKind.Relative));
            timer.Stop();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(3), $"{path}: {timer.ElapsedMilliseconds} ms");
        }

        OrderReportResponse year = await GetAsync<OrderReportResponse>(manager.Client, $"{OrdersReport}?from=2045-01-01&to=2045-12-31&status=cancelled");
        Assert.Equal((12_500, 25_000), (year.TotalOrders, year.TotalDrinks));
        OrderReportResponse found = await GetAsync<OrderReportResponse>(manager.Client, $"{OrdersReport}?from=2045-01-01&to=2045-12-31&q=ZZ00-C350");
        Assert.Equal(new[] { "MS-ZZ00-C350", "MS-ZZ00-C350" }, found.Rows.Select(r => r.Reference));

        // Exports stop at 10,000 records: a year of 100,000 milkshakes is refused; one week (about 2,000) is not.
        using HttpResponseMessage tooLarge = await manager.Client.GetAsync(new Uri($"{OrdersReport}/export?from=2045-01-01&to=2045-12-31", UriKind.Relative));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLarge.StatusCode);
        Assert.Contains("10,000", await tooLarge.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var exportTimer = Stopwatch.StartNew();
        using HttpResponseMessage week = await manager.Client.GetAsync(new Uri($"{OrdersReport}/export?from=2045-03-02&to=2045-03-08", UriKind.Relative));
        exportTimer.Stop();
        Assert.Equal(HttpStatusCode.OK, week.StatusCode);
        Assert.True(exportTimer.Elapsed < TimeSpan.FromSeconds(3), $"week export: {exportTimer.ElapsedMilliseconds} ms");
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Six orders of one test menu, moved to fixed UTC times in <paramref name="year"/> (Johannesburg is UTC+2 all year):
    /// F 3 Mar 21:59Z (23:59 local, unpaid); A 4 Mar 08:00Z (10:00, paid, two milkshakes); B 4 Mar 21:30Z (23:30, unpaid);
    /// C 4 Mar 22:30Z (00:30 on the 5th, cancelled unpaid); D 6 Mar 10:00Z (12:00, payment failed); E 10 Apr 07:00Z (paid).
    /// </summary>
    private async Task<Seeded> SeedAsync(int year = 2041)
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        TestMenu menu = await manager.CreateTestMenuAsync(12.00m, 6.00m, 8.00m);
        using HttpClient patron = await factory.CreatePatronClientAsync();

        string a = await PlaceAsync(patron, menu, drinks: 2);
        string b = await PlaceAsync(patron, menu, drinks: 1);
        string c = await PlaceAsync(patron, menu, drinks: 1);
        string d = await PlaceAsync(patron, menu, drinks: 1);
        string e = await PlaceAsync(patron, menu, drinks: 1);
        string f = await PlaceAsync(patron, menu, drinks: 1);

        string y = year.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        foreach ((string reference, string at, string status) in new[]
        {
            (a, $"{y}-03-04T08:00:00Z", "Confirmed"),
            (b, $"{y}-03-04T21:30:00Z", "PendingPayment"),
            (c, $"{y}-03-04T22:30:00Z", "Cancelled"),
            (d, $"{y}-03-06T10:00:00Z", "PaymentFailed"),
            (e, $"{y}-04-10T07:00:00Z", "Confirmed"),
            (f, $"{y}-03-03T21:59:00Z", "PendingPayment"),
        })
        {
            DateTimeOffset placed = DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture);
            await db.Database.ExecuteSqlAsync($"UPDATE milkshake.orders SET created_at = {placed}, status = {status} WHERE reference = {reference}");
        }

        return new Seeded(menu, a, b, c, d, e, f);
    }

    private async Task<(int Orders, int Drinks)> CountInDatabaseAsync(string flavour, DateOnly from, DateOnly to)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        CountRow row = await db.Database.SqlQuery<CountRow>($"""
            SELECT count(DISTINCT o.id)::int AS orders, count(*)::int AS drinks
            FROM milkshake.orders AS o
            JOIN milkshake.order_items AS i ON i.order_id = o.id
            WHERE (o.created_at AT TIME ZONE {Zone})::date BETWEEN {from} AND {to}
              AND i.flavour_name = {flavour}
            """).SingleAsync();
        return (row.Orders, row.Drinks);
    }

    private async Task<(int Total, int Paid)> CountAllInDatabaseAsync()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        TotalRow row = await db.Database.SqlQuery<TotalRow>($"""
            SELECT count(*)::int AS total, (count(*) FILTER (WHERE status = 'Confirmed'))::int AS paid
            FROM milkshake.orders
            """).SingleAsync();
        return (row.Total, row.Paid);
    }

    /// <summary>Places an order of <paramref name="drinks"/> milkshakes (29.90 each; no discount below three).</summary>
    private static async Task<string> PlaceAsync(HttpClient patron, TestMenu menu, int drinks)
    {
        OrderingOptionsResponse options = await patron.GetFromJsonAsync<OrderingOptionsResponse>(new Uri("/api/ordering/options", UriKind.Relative))
            ?? throw new InvalidOperationException("No options returned.");
        var body = new
        {
            restaurantId = menu.Restaurant.Id,
            pickupLocal = options.Pickup.LatestLocal,
            drinks = Enumerable.Repeat(new { flavourId = menu.Flavour.Id, consistencyId = menu.Consistency.Id, toppingId = menu.Topping.Id }, drinks).ToArray(),
            expectedTotal = 29.90m * drinks,
            expectedCurrencyCode = "ZAR",
        };

        string token = await patron.GetCsrfTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/ordering/orders", UriKind.Relative)) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-XSRF-TOKEN", token);
        request.Headers.Add(OrderMessages.IdempotencyKeyHeader, Guid.NewGuid().ToString());
        using HttpResponseMessage response = await patron.SendAsync(request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Placing failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.ReadJsonAsync<OrderResponse>()).Reference;
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidOperationException("No body.");
    }

    private static (int Orders, int Drinks) Totals(OrderReportResponse report) => (report.TotalOrders, report.TotalDrinks);

    private static async Task<string> ExportTextAsync(HttpClient client, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
        return System.Text.Encoding.UTF8.GetString(bytes[3..]);
    }

    private sealed record Seeded(TestMenu Menu, string A, string B, string C, string D, string E, string F);

    private sealed record CountRow(int Orders, int Drinks);

    private sealed record TotalRow(int Total, int Paid);
}
