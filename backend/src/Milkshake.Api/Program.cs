using Microsoft.Extensions.Diagnostics.HealthChecks;
using Milkshake.Application;
using Milkshake.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ---- Services ------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

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
app.UseAuthorization();

app.MapControllers();

app.Run();