namespace Milkshake.Api.Security;

/// <summary>
/// Security response headers on every response, error responses included (M13 SEC, OWASP ZAP findings 10020, 10021,
/// 10038, 10063 and 90004).
/// <list type="bullet">
/// <item>API (<c>/api</c>): JSON only, so its Content-Security-Policy allows nothing to load and nothing to frame it.</item>
/// <item>Website (every other path, M14): the policy of <c>frontend/security-headers.ts</c>, so the production container,
/// which serves the built site itself, sends what <c>vite preview</c> sends in the E2E stack. The two must stay equal;
/// <c>SiteServingTests</c> checks the values here.</item>
/// <item>The development-only API reference (<c>/scalar</c>, <c>/openapi</c>) gets the common headers only, so it keeps
/// working.</item>
/// </list>
/// HSTS is added separately (UseHsts outside Development; it skips localhost).
/// </summary>
public static class SecurityHeaders
{
    /// <summary>Content-Security-Policy for API responses: no content of any kind, never framed.</summary>
    public const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    /// <summary>Content-Security-Policy for the website: only its own files (same as <c>frontend/security-headers.ts</c>).</summary>
    public const string SiteContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; " +
        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

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
                PathString path = context.Request.Path;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Cross-Origin-Resource-Policy"] = "same-origin";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Permissions-Policy"] = PermissionsPolicy;

                if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    headers["Referrer-Policy"] = "no-referrer";
                    headers.ContentSecurityPolicy = ApiContentSecurityPolicy;
                }
                else if (path.StartsWithSegments("/scalar", StringComparison.OrdinalIgnoreCase) ||
                         path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase))
                {
                    headers["Referrer-Policy"] = "no-referrer";
                }
                else
                {
                    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                    headers.ContentSecurityPolicy = SiteContentSecurityPolicy;
                    headers["Cross-Origin-Embedder-Policy"] = "require-corp";
                }

                return Task.CompletedTask;
            });
            await next(context);
        });
    }
}
