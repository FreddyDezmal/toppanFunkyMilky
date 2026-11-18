using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Milkshake.Api;
using Milkshake.Api.Contracts.Auth;

namespace Milkshake.IntegrationTests.Infrastructure;

/// <summary>Body of a 400 ValidationProblemDetails response.</summary>
internal sealed record ValidationErrorBody(int Status, Dictionary<string, string[]> Errors);

/// <summary>Body of a ProblemDetails response.</summary>
internal sealed record ProblemBody(int Status, string? Title, string? Detail);

/// <summary>Body of GET /api/auth/me and POST /api/auth/login.</summary>
internal sealed record CurrentUserBody(Guid Id, string FirstName, string Email, string? MobileNumber, string[] Roles);

/// <summary>Login payload.</summary>
internal sealed record LoginBody(string Email, string Password);

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

    /// <summary>The full Set-Cookie header for the named cookie.</summary>
    public static string GetSetCookie(this HttpResponseMessage response, string cookieName) =>
        response.Headers.GetValues("Set-Cookie").First(value => value.StartsWith(cookieName + "=", StringComparison.Ordinal));

    /// <summary>An https client for a derived factory (WithWebHostBuilder).</summary>
    public static HttpClient CreateSecureClient(this WebApplicationFactory<ApiAssemblyMarker> app, bool handleCookies = true) =>
        app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = handleCookies,
        });
}