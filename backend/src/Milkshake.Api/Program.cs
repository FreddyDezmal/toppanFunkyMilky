using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Milkshake.Api.Services;
using Milkshake.Application;
using Milkshake.Application.Abstractions;
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
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
        }
    });
});

builder.Services.AddControllers();
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
    app.MapOpenApi();              // OpenAPI document: /openapi/v1.json
    app.MapScalarApiReference();   // Interactive API docs: /scalar
}

app.UseHttpsRedirection();
app.UseCors(FrontendCorsPolicy);
app.UseAuthorization();

app.MapControllers();

app.Run();