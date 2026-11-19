using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Milkshake.Api;
using Milkshake.Api.Controllers;
using Milkshake.IntegrationTests.Infrastructure;
using static Milkshake.IntegrationTests.Infrastructure.OrderingTestHelpers;

namespace Milkshake.IntegrationTests;

/// <summary>M5 findings F1 (per-patron rate limit) and F2 (draft body cap).</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class OrderingHardeningTests(PostgresApiFactory factory)
{
    private static readonly Uri OptionsUri = new("/api/ordering/options", UriKind.Relative);

    [Fact]
    public async Task OrderingRequests_BeyondTheLimit_Return429_PerPatronNotPerAddress()
    {
        using WebApplicationFactory<ApiAssemblyMarker> app =
            factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:OrderingPermitLimit", "3"));
        using HttpClient first = await RegisterPatronAsync(app);
        using HttpClient second = await RegisterPatronAsync(app);

        for (int call = 1; call <= 3; call++)
        {
            using HttpResponseMessage allowed = await first.GetAsync(OptionsUri);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        using HttpResponseMessage limited = await first.GetAsync(OptionsUri);
        using HttpResponseMessage otherPatron = await second.GetAsync(OptionsUri);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, otherPatron.StatusCode);   // same address, different patron
    }

    [Fact]
    public async Task AnOversizedDraft_Returns413_WhileTheLargestRealDraftFits()
    {
        using HttpClient patron = await factory.CreatePatronClientAsync();
        string largestReal = Draft(50);   // the highest maximum a manager can configure, with the longest ids
        string oversized = Draft(400);
        Assert.InRange(Encoding.UTF8.GetByteCount(largestReal), 1, OrderingController.MaxDraftBodyBytes);
        Assert.True(Encoding.UTF8.GetByteCount(oversized) > OrderingController.MaxDraftBodyBytes);

        using HttpResponseMessage fits = await patron.PostRawJsonWithCsrfAsync(ValidatePath, largestReal);
        using HttpResponseMessage tooLarge = await patron.PostRawJsonWithCsrfAsync(ValidatePath, oversized);

        Assert.Equal(HttpStatusCode.BadRequest, fits.StatusCode);   // read and validated (unknown ids), not refused for size
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Equal((int)HttpStatusCode.RequestEntityTooLarge, (await tooLarge.ReadJsonAsync<ProblemBody>()).Status);

        static string Draft(int drinks) =>
            """{"restaurantId":2147483647,"pickupLocal":"2026-11-20T10:00","drinks":["""
            + string.Join(",", Enumerable.Repeat("""{"flavourId":2147483647,"consistencyId":2147483647,"toppingId":2147483647}""", drinks))
            + "]}";
    }

    private static async Task<HttpClient> RegisterPatronAsync(WebApplicationFactory<ApiAssemblyMarker> app)
    {
        HttpClient client = app.CreateSecureClient();
        using HttpResponseMessage register = await client.PostWithCsrfAsync("/api/auth/register", RegisterBody.Valid());
        if (register.StatusCode != HttpStatusCode.Created)
        {
            client.Dispose();
            throw new InvalidOperationException($"Test patron registration failed: {register.StatusCode}.");
        }

        return client;
    }
}