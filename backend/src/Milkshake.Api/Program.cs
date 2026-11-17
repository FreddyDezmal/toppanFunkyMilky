using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Milkshake.Api.Security;
using Milkshake.Api.Services;
using Milkshake.Application;
using Milkshake.Application.Abstractions;
using Milkshake.Domain.Users;
using Milkshake.Infrastructure;
using Scalar.AspNetCore;

const string FrontendCorsPolicy = "Frontend";

var builder = WebApplication.CreateBuilder(args);

// ---- Services ------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ---- Authentication: ASP.NET Core Identity session cookie ----------------
builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "milkshake.session";
    options.Cookie.HttpOnly = true;                           // not readable by JavaScript (XSS protection)
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;  // HTTPS only
    options.Cookie.SameSite = SameSiteMode.Strict;            // never sent on cross-site requests
    options.ExpireTimeSpan = TimeSpan.FromHours(2);
    options.SlidingExpiration = true;                         // activity extends the session

    // This is a JSON API: answer 401/403 instead of redirecting to a login page.
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// ---- Authorization: everything requires a signed-in user unless marked [AllowAnonymous] ----
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(AuthorizationPolicies.ManagerOnly, policy => policy.RequireRole(Roles.Manager));

// ---- CSRF protection for cookie sessions ---------------------------------
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "milkshake.antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

// Only origins listed in configuration (Cors:AllowedOrigins) may call the API from a browser.
// Read lazily through options so tests and environments can supply origins without code changes.
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>().Configure<IConfiguration>((options, configuration) =>
{
    string[] allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
    });
});

builder.Services.AddControllers(options =>
{
    options.Filters.Add<ValidateCsrfTokenFilter>();

    // Validation errors use the JSON property names (camelCase) the frontend sends and displays.
    options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider());
});

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("API is running."));

var app = builder.Build();

// ---- HTTP pipeline -------------------------------------------------------
// Unhandled exceptions become RFC 9457 ProblemDetails JSON; no stack traces reach the client.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();              // OpenAPI document: /openapi/v1.json
    app.MapScalarApiReference().AllowAnonymous();   // Interactive API docs: /scalar
}

app.UseHttpsRedirection();
app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();   // who is calling (reads the session cookie)
app.UseAuthorization();    // what they may do (policies, roles)

app.MapControllers();

app.Run();