namespace Milkshake.Api.Security;

/// <summary>
/// Security response headers on every API response, error responses included (M13 SEC, OWASP ZAP findings 10020,
/// 10021, 10038, 10063 and 90004). The API returns JSON only, so its Content-Security-Policy allows nothing to load
/// and nothing to frame it. The policy is applied to /api paths only, so the development-only API reference page
/// (/scalar) keeps working. HSTS is added separately (UseHsts outside Development; it skips localhost).
/// </summary>
public static class SecurityHeaders
{
    /// <summary>Content-Security-Policy for API responses: no content of any kind, never framed.</summary>
    public const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    /// <summary>Browser features the application never uses.</summary>
    public const string PermissionsPolicy = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

    /// <summary>Adds the headers when the response starts, so they are present whatever later middleware does.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                IHeaderDictionary headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Cross-Origin-Resource-Policy"] = "same-origin";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Permissions-Policy"] = PermissionsPolicy;
                if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    headers.ContentSecurityPolicy = ApiContentSecurityPolicy;
                }

                return Task.CompletedTask;
            });
            await next(context);
        });
    }
}
