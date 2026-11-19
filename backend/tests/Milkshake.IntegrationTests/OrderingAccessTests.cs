using System.Net;
using Milkshake.Application.Ordering;
using Milkshake.IntegrationTests.Infrastructure;
using static Milkshake.IntegrationTests.Infrastructure.OrderingTestHelpers;

namespace Milkshake.IntegrationTests;

/// <summary>M5 security review: access to draft validation and the shape of what it returns to a hostile caller.</summary>
[Collection(PostgresTestGroup.Name)]
public sealed class OrderingAccessTests(PostgresApiFactory factory)
{
    [Fact]
    public async Task Validate_ForAManagerOnlyAccount_Returns403()
    {
        using ManagerSession manager = await factory.CreateManagerSessionAsync();

        using HttpResponseMessage response = await manager.Client.SendWithCsrfAsync(
            HttpMethod.Post, ValidatePath, new { restaurantId = 1, drinks = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ValidationErrors_AreProblemJson_AndNeverEchoWhatWasSent()
    {
        using HttpClient patron = await factory.CreatePatronClientAsync();
        const string Script = "<script>alert('x')</script>";

        using HttpResponseMessage response = await patron.SendWithCsrfAsync(
            HttpMethod.Post, ValidatePath, new { restaurantId = 999_999, pickupLocal = Script, drinks = new[] { new { flavourId = -1 } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<script>", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("999999", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HundredsOfDrinks_GetOneQuantityMessage_NotHundreds()
    {
        using HttpClient patron = await factory.CreatePatronClientAsync();
        object[] drinks = Enumerable.Range(0, 300).Select(_ => (object)new { flavourId = 1, consistencyId = 1, toppingId = 1 }).ToArray();
        
        using HttpResponseMessage response = await patron.SendWithCsrfAsync(HttpMethod.Post, ValidatePath, new { restaurantId = 1, drinks });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ValidationErrorBody body = await response.ReadJsonAsync<ValidationErrorBody>();
        Assert.Single(body.Errors[OrderDraftFields.Drinks]);
        Assert.DoesNotContain(body.Errors.Keys, key => key.StartsWith("drinks[", StringComparison.Ordinal));
    }
}