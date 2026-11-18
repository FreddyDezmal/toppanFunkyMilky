using Microsoft.EntityFrameworkCore;
using Milkshake.Domain.Settings;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Infrastructure.Management;

/// <summary>Reads the configured pricing currency (never hard-coded).</summary>
internal static class PricingCurrency
{
    public static async Task<string> GetAsync(MilkshakeDbContext dbContext, CancellationToken cancellationToken)
    {
        string? currency = await dbContext.SystemSettings.AsNoTracking()
            .Where(setting => setting.Key == SettingKeys.CurrencyCode)
            .Select(setting => setting.Value)
            .SingleOrDefaultAsync(cancellationToken);

        return currency ?? throw new InvalidOperationException(
            $"The '{SettingKeys.CurrencyCode}' setting is missing. Run the database seeder before managing prices.");
    }
}