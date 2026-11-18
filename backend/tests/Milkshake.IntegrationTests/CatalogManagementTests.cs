using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Milkshake.Api.Contracts.Management;
using Milkshake.Domain.Auditing;
using Milkshake.Domain.Catalog;
using Milkshake.Infrastructure.Persistence;
using Milkshake.IntegrationTests.Infrastructure;
using Npgsql;

namespace Milkshake.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class CatalogManagementTests(PostgresApiFactory factory)
{
    private const string FlavoursPath = "/api/manager/catalog/flavours";

    [Fact]
    public async Task List_WhenAnonymous_Returns401()
    {
        using HttpClient client = factory.CreateHttpsClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(FlavoursPath, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_AsPatron_Returns403()
    {
        using HttpClient client = factory.CreateHttpsClient();
        using HttpResponseMessage register = await client.PostWithCsrfAsync("/api/auth/register", RegisterBody.Valid());
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        using HttpResponseMessage response = await client.GetAsync(new Uri(FlavoursPath, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnknownCatalogueKind_Returns404()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        using HttpResponseMessage response = await manager.Client.GetAsync(new Uri("/api/manager/catalog/sauces", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutAntiForgeryToken_Returns400()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        using HttpResponseMessage response = await manager.Client.PostAsJsonAsync(
            new Uri(FlavoursPath, UriKind.Relative),
            new { name = UniqueName(), displayOrder = 1, initialPrice = 10m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithInitialPrice_ReturnsCurrentPriceAndIsAudited()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        string name = UniqueName();

        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, FlavoursPath, new { name, displayOrder = 50, initialPrice = 12.50m });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        CatalogItemResponse item = await response.ReadJsonAsync<CatalogItemResponse>();
        Assert.Equal(name, item.Name);
        Assert.True(item.IsActive);
        Assert.Equal(12.50m, item.CurrentPrice);
        Assert.Equal("ZAR", item.CurrencyCode);
        Assert.NotEqual(0u, item.Version);

        using IServiceScope scope = factory.Services.CreateScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        string recordId = item.Id.ToString(CultureInfo.InvariantCulture);
        AuditLog insert = await db.AuditLogs.SingleAsync(a => a.TableName == "flavours" && a.RecordId == recordId && a.Action == AuditAction.Insert);
        Assert.Equal(manager.Email, insert.UserEmail);
        Assert.DoesNotContain("xmin", insert.Changes, StringComparison.Ordinal);
        Assert.True(await db.FlavourPrices.AnyAsync(p => p.FlavourId == item.Id && p.EffectiveTo == null));
    }

    [Fact]
    public async Task Create_WithNameDifferingOnlyByCase_Returns400()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        string name = UniqueName();
        await CreateFlavourAsync(manager, name);

        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, FlavoursPath, new { name = name.ToUpperInvariant(), displayOrder = 1, initialPrice = 5m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ValidationErrorBody body = await response.ReadJsonAsync<ValidationErrorBody>();
        Assert.Contains("name", body.Errors.Keys);
    }

    [Theory]
    [InlineData("10.555")]
    [InlineData("-1")]
    public async Task Create_WithInvalidPrice_Returns400ForInitialPrice(string price)
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        decimal amount = decimal.Parse(price, CultureInfo.InvariantCulture);

        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, FlavoursPath, new { name = UniqueName(), displayOrder = 1, initialPrice = amount });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ValidationErrorBody body = await response.ReadJsonAsync<ValidationErrorBody>();
        Assert.Contains("initialPrice", body.Errors.Keys);
    }

    [Fact]
    public async Task Create_WithoutInitialPrice_Returns400ForInitialPrice()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, FlavoursPath, new { name = UniqueName(), displayOrder = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ValidationErrorBody body = await response.ReadJsonAsync<ValidationErrorBody>();
        Assert.Contains("initialPrice", body.Errors.Keys);
    }

    [Fact]
    public async Task Update_WithStaleVersion_Returns409AndKeepsFirstChange()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        string name = UniqueName();
        CatalogItemResponse created = await CreateFlavourAsync(manager, name);
        string path = $"{FlavoursPath}/{created.Id}";
        string renamed = name + " A";

        using HttpResponseMessage first = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Put, path, new { name = renamed, displayOrder = 1, version = created.Version });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        CatalogItemResponse updated = await first.ReadJsonAsync<CatalogItemResponse>();
        Assert.NotEqual(created.Version, updated.Version);

        // A second manager still holding the original version.
        using HttpResponseMessage stale = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Put, path, new { name = name + " B", displayOrder = 2, version = created.Version });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        ProblemBody problem = await stale.ReadJsonAsync<ProblemBody>();
        Assert.Equal(409, problem.Status);

        CatalogItemResponse current = await manager.Client.GetFromJsonAsync<CatalogItemResponse>(new Uri(path, UriKind.Relative))
            ?? throw new InvalidOperationException("No item returned.");
        Assert.Equal(renamed, current.Name);

        // Exactly one field-level update was audited: the accepted change.
        using IServiceScope scope = factory.Services.CreateScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        string recordId = created.Id.ToString(CultureInfo.InvariantCulture);
        List<string> updates = await db.AuditLogs
            .Where(a => a.TableName == "flavours" && a.RecordId == recordId && a.Action == AuditAction.Update)
            .Select(a => a.Changes)
            .ToListAsync();
        using JsonDocument changes = JsonDocument.Parse(Assert.Single(updates));
        JsonElement nameChange = changes.RootElement.EnumerateArray().Single(c => c.GetProperty("field").GetString() == "name");
        Assert.Equal(name, nameChange.GetProperty("old").GetString());
        Assert.Equal(renamed, nameChange.GetProperty("new").GetString());
    }

    [Fact]
    public async Task DeactivateAndActivate_ChangeAvailabilityAndKeepPrice()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        CatalogItemResponse created = await CreateFlavourAsync(manager, UniqueName());

        using HttpResponseMessage deactivate = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, $"{FlavoursPath}/{created.Id}/deactivate", new { version = created.Version });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        CatalogItemResponse inactive = await deactivate.ReadJsonAsync<CatalogItemResponse>();
        Assert.False(inactive.IsActive);
        Assert.Equal(created.CurrentPrice, inactive.CurrentPrice);

        Assert.DoesNotContain(await ListAsync(manager, "active"), i => i.Id == created.Id);
        Assert.Contains(await ListAsync(manager, "inactive"), i => i.Id == created.Id);

        using HttpResponseMessage stale = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, $"{FlavoursPath}/{created.Id}/activate", new { version = created.Version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using HttpResponseMessage activate = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, $"{FlavoursPath}/{created.Id}/activate", new { version = inactive.Version });
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        Assert.True((await activate.ReadJsonAsync<CatalogItemResponse>()).IsActive);
    }

    [Fact]
    public async Task Database_RejectsNamesDifferingOnlyByCase()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        MilkshakeDbContext db = scope.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        string name = UniqueName("Topping");
        db.Toppings.Add(new Topping(name, 1));
        await db.SaveChangesAsync();

        db.Toppings.Add(new Topping(name.ToUpperInvariant(), 2));
        DbUpdateException exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        PostgresException postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("ux_toppings_lower_name", postgres.ConstraintName);
    }

    [Fact]
    public async Task Database_RejectsUpdateBasedOnStaleRowVersion()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();
        string name = UniqueName();
        CatalogItemResponse created = await CreateFlavourAsync(manager, name);

        using IServiceScope first = factory.Services.CreateScope();
        using IServiceScope second = factory.Services.CreateScope();
        MilkshakeDbContext dbA = first.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        MilkshakeDbContext dbB = second.ServiceProvider.GetRequiredService<MilkshakeDbContext>();
        Flavour inA = await dbA.Flavours.SingleAsync(f => f.Id == created.Id);
        Flavour inB = await dbB.Flavours.SingleAsync(f => f.Id == created.Id);

        inA.Rename(name + " A");
        await dbA.SaveChangesAsync();

        inB.Rename(name + " B");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());
    }

    private static string UniqueName(string prefix = "Flavour") => $"{prefix} {Guid.NewGuid():N}";

    private static async Task<CatalogItemResponse> CreateFlavourAsync(ManagerSession manager, string name)
    {
        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, FlavoursPath, new { name, displayOrder = 50, initialPrice = 12.50m });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<CatalogItemResponse>();
    }

    private static async Task<List<CatalogItemResponse>> ListAsync(ManagerSession manager, string status) =>
        await manager.Client.GetFromJsonAsync<List<CatalogItemResponse>>(new Uri($"{FlavoursPath}?status={status}", UriKind.Relative))
        ?? throw new InvalidOperationException("No list returned.");
}