using Milkshake.Application.Ordering;
using Milkshake.Infrastructure.Ordering;
using Milkshake.Application.Payments;
using Milkshake.Infrastructure.Payments;
using Milkshake.Application.Notifications;
using Milkshake.Infrastructure.Notifications;
using Microsoft.Extensions.Options;
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
        services.AddScoped<NotificationSaveChangesInterceptor>();
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
                // Order matters: the email outbox rows (M9) are added first, so the audit interceptor stamps and audits them.
                .AddInterceptors(
                    serviceProvider.GetRequiredService<NotificationSaveChangesInterceptor>(),
                    serviceProvider.GetRequiredService<AuditingSaveChangesInterceptor>())
                .UseSeeding((context, _) =>
                    serviceProvider.GetRequiredService<DatabaseSeeder>().Seed((MilkshakeDbContext)context))
                .UseAsyncSeeding((context, _, cancellationToken) =>
                    serviceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync((MilkshakeDbContext)context, cancellationToken)));
                    
        AddIdentityServices(services);

        services.AddScoped<ICatalogManagementService, CatalogManagementService>();
        services.AddScoped<IRestaurantManagementService, RestaurantManagementService>();
        services.AddScoped<ICatalogPricingService, CatalogPricingService>();
        services.AddScoped<ISettingsManagementService, SettingsManagementService>();
        services.AddScoped<IDiscountTierManagementService, DiscountTierManagementService>();
        services.AddScoped<IOrderingCatalogService, OrderingCatalogService>();
        services.AddScoped<IOrderPlacementService, OrderPlacementService>();
        services.AddScoped<IOrderQueryService, OrderQueryService>();

        // Payments (M8): Paystack over a typed HttpClient. The secret key comes from user secrets / environment variables.
        services.AddOptions<PaystackOptions>().Bind(configuration.GetSection(PaystackOptions.SectionName));
        services.AddHttpClient<IPaymentGateway, PaystackGateway>((provider, client) =>
        {
            PaystackOptions paystack = provider.GetRequiredService<IOptions<PaystackOptions>>().Value;
            client.BaseAddress = paystack.BaseUrl;
            client.Timeout = TimeSpan.FromSeconds(paystack.TimeoutSeconds);
        });
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentNotificationService, PaymentNotificationService>();
        services.AddSingleton<PaystackWebhookAuthenticator>();

        // Recovery without webhooks (P7): patron refresh, plus a background job checking open attempts every few minutes.
        services.AddOptions<PaymentReconciliationOptions>().Bind(configuration.GetSection(PaymentReconciliationOptions.SectionName));
        services.AddScoped<PaymentSettler>();
        services.AddScoped<IPaymentReconciler, PaymentReconciler>();
        services.AddScoped<IPaymentStatusService, PaymentStatusService>();
        services.AddScoped<RefundSubmitter>();
        services.AddScoped<IOrderCancellationService, OrderCancellationService>();
        services.AddScoped<IManagerOrderService, ManagerOrderService>();
        services.AddHostedService<PaymentReconciliationWorker>();
        services.AddScoped<IDiscountConfigurationSource, DiscountConfigurationSource>();

        // Email (M9, decision D3): Mailpit (local capture) or Resend, chosen by Email:Provider; "None" leaves emails queued.
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.SectionName));
        services.AddHttpClient<ResendEmailSender>((provider, client) =>
            client.Timeout = TimeSpan.FromSeconds(provider.GetRequiredService<IOptions<EmailOptions>>().Value.Resend.TimeoutSeconds));
        services.AddSingleton<MailpitEmailSender>();
        services.AddScoped<IEmailSender>(provider => provider.GetRequiredService<IOptions<EmailOptions>>().Value.Provider switch
        {
            EmailOptions.MailpitProvider => provider.GetRequiredService<MailpitEmailSender>(),
            EmailOptions.ResendProvider => provider.GetRequiredService<ResendEmailSender>(),
            _ => new DisabledEmailSender(),
        });
        services.AddScoped<IEmailDispatcher, EmailDispatcher>();
        services.AddHostedService<EmailDispatchWorker>();

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