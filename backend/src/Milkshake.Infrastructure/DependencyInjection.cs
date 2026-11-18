using Milkshake.Application.Management;
using Milkshake.Infrastructure.Management;
using Milkshake.Application.Accounts;
using Microsoft.AspNetCore.Identity;
using Milkshake.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Milkshake.Application.Abstractions;
using Milkshake.Infrastructure.Persistence;
using Milkshake.Infrastructure.Persistence.Auditing;
using Milkshake.Infrastructure.Persistence.Seeding;
using Npgsql;

namespace Milkshake.Infrastructure;

/// <summary>
/// Registers infrastructure services: persistence, auditing, seeding, identity stores, email and payment clients.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Name of the connection string under the "ConnectionStrings" configuration section.
    /// The connection string holds everything except the password.
    /// </summary>
    public const string ConnectionStringName = "MilkshakeDb";

    /// <summary>
    /// Configuration key for the database password, stored separately from the connection string
    /// (user secrets locally; environment variable "Database__Password" or a secrets store in production).
    /// </summary>
    public const string DatabasePasswordKey = "Database:Password";

    /// <summary>
    /// Adds the infrastructure layer to the service collection.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Fallbacks for hosts without an HTTP pipeline (the API registers its own ICurrentUser first).
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser, SystemCurrentUser>();

        services.AddScoped<AuditingSaveChangesInterceptor>();
        services.AddOptions<SeedDataOptions>().Bind(configuration.GetSection(SeedDataOptions.SectionName));
        services.AddScoped<DatabaseSeeder>();

        // The connection string is built when the DbContext is first created, not at startup,
        // so the app (and the test host) can start without a database configured.
        services.AddDbContext<MilkshakeDbContext>((serviceProvider, options) =>
            options
                .UseNpgsql(BuildConnectionString(configuration), npgsql =>
                {
                    npgsql.MigrationsHistoryTable("__ef_migrations_history", MilkshakeDbContext.Schema);
                    npgsql.EnableRetryOnFailure(maxRetryCount: 3);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(serviceProvider.GetRequiredService<AuditingSaveChangesInterceptor>())
                .UseSeeding((context, _) =>
                    serviceProvider.GetRequiredService<DatabaseSeeder>().Seed((MilkshakeDbContext)context))
                .UseAsyncSeeding((context, _, cancellationToken) =>
                    serviceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync((MilkshakeDbContext)context, cancellationToken)));
                    
        AddIdentityServices(services);
        services.AddScoped<ICatalogManagementService, CatalogManagementService>();
        services.AddScoped<IRestaurantManagementService, RestaurantManagementService>();
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>(
                name: "database",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["db", "ready"],
                timeout: TimeSpan.FromSeconds(20));

        return services;
    }

    /// <summary>
    /// Combines the password-free connection string with the separately stored password.
    /// NpgsqlConnectionStringBuilder escapes every value correctly, whatever characters the password contains.
    /// </summary>
    
        /// <summary>
    /// Registers ASP.NET Core Identity (users, roles, password hashing, lockout, sign-in) on the existing tables.
    /// </summary>
    private static void AddIdentityServices(IServiceCollection services)
    {
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // One account per email address; the email is also the user name.
                options.User.RequireUniqueEmail = true;

                // URS example screen: at least 8 characters. Composition rules are a project recommendation.
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;

                // Brute-force protection: 5 wrong passwords lock the account for 15 minutes.
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                // Email confirmation arrives with the email milestone (M9).
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<MilkshakeDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // Sessions are re-checked against the database every 5 minutes, so a password change,
        // role change or "sign out everywhere" takes effect quickly.
        services.Configure<SecurityStampValidatorOptions>(options =>
            options.ValidationInterval = TimeSpan.FromMinutes(5));
            
        services.AddHttpContextAccessor();
        services.AddScoped<SecurityEventRecorder>();
        services.AddScoped<IAccountService, AccountService>();
    }
    private static string BuildConnectionString(IConfiguration configuration)
    {
        string baseConnectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. " +
                $"For local development, set it with: dotnet user-secrets set \"ConnectionStrings:{ConnectionStringName}\" \"<value>\" --project backend/src/Milkshake.Api");

        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString);

        string? password = configuration[DatabasePasswordKey];
        if (!string.IsNullOrEmpty(password))
        {
            builder.Password = password;
        }

        return builder.ConnectionString;
    }
}