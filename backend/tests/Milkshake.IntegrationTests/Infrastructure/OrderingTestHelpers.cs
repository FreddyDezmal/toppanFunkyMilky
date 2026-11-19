using System.Text;

namespace Milkshake.IntegrationTests.Infrastructure;

internal static class OrderingTestHelpers
{
    public const string ValidatePath = "/api/ordering/draft/validate";

    /// <summary>POSTs raw JSON text (for malformed and tampered requests) with a fresh anti-forgery token.</summary>
    public static async Task<HttpResponseMessage> PostRawJsonWithCsrfAsync(this HttpClient client, string path, string json)
    {
        string token = await client.GetCsrfTokenAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);

        return await client.SendAsync(request);
    }
}