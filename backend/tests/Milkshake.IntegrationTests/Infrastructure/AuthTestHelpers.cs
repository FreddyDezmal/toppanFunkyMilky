using System.Net.Http.Json;
using Milkshake.Api.Contracts.Auth;

namespace Milkshake.IntegrationTests.Infrastructure;

/// <summary>Body of a 400 ValidationProblemDetails response.</summary>
internal sealed record ValidationErrorBody(int Status, Dictionary<string, string[]> Errors);

/// <summary>Registration payload as the frontend sends it.</summary>
internal sealed record RegisterBody(string FirstName, string Email, string MobileNumber, string Password, string ConfirmPassword)
{
    public const string ValidPassword = "Milkshake1Test";

    public static RegisterBody Valid(string? email = null) =>
        new("Thandi", email ?? UniqueEmail(), "+27 82 123 4567", ValidPassword, ValidPassword);

    public static string UniqueEmail() => $"patron-{Guid.NewGuid():N}@example.test";
}

internal static class AuthHttpExtensions
{
    /// <summary>Fetches an anti-forgery token (its cookie is kept by the client).</summary>
    public static async Task<string> GetCsrfTokenAsync(this HttpClient client)
    {
        CsrfTokenResponse? csrf = await client.GetFromJsonAsync<CsrfTokenResponse>(new Uri("/api/auth/csrf", UriKind.Relative));
        return csrf?.Token ?? throw new InvalidOperationException("The API returned no anti-forgery token.");
    }

    /// <summary>POSTs JSON with a fresh anti-forgery token, as the React client will.</summary>
    public static async Task<HttpResponseMessage> PostWithCsrfAsync<T>(this HttpClient client, string path, T body)
    {
        string token = await client.GetCsrfTokenAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);

        return await client.SendAsync(request);
    }
}