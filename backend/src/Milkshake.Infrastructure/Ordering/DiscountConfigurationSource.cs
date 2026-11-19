using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Milkshake.Application.Management;
using Milkshake.Application.Ordering;
using Milkshake.Domain.Common;
using Milkshake.Domain.Pricing;
using Milkshake.Domain.Settings;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Infrastructure.Ordering;

/// <summary>
/// Reads the discount tiers and the maximum discount (M10 configuration). Only called once the discount rules are
/// confirmed (see <see cref="DiscountResolver"/>). Invalid configuration is refused rather than guessed.
/// </summary>
internal sealed class DiscountConfigurationSource(MilkshakeDbContext dbContext) : IDiscountConfigurationSource
{
    public async Task<DiscountConfiguration> GetAsync(CancellationToken cancellationToken)
    {
        List<DiscountTierRule> tiers = await dbContext.DiscountTiers.AsNoTracking()
            .OrderBy(tier => tier.TierNumber)
            .Select(tier => new DiscountTierRule(tier.TierNumber, tier.MinPreviousOrders, tier.MinDrinksPerOrder, tier.DiscountRate, tier.IsActive))
            .ToListAsync(cancellationToken);

        Dictionary<string, string> stored = await dbContext.SystemSettings.AsNoTracking()
            .ToDictionaryAsync(setting => setting.Key, setting => setting.Value, StringComparer.Ordinal, cancellationToken);

        if (!stored.TryGetValue(SettingKeys.MaxDiscountAmount, out string? maxDiscount) ||
            !stored.TryGetValue(SettingKeys.CurrencyCode, out string? currency))
        {
            throw new PricingConfigurationException("The maximum discount or the pricing currency is not configured.");
        }

        SettingValidation checkedMax = SettingRules.Validate(SettingKeys.MaxDiscountAmount, maxDiscount, stored);
        if (!checkedMax.IsValid || checkedMax.NormalisedValue is null)
        {
            throw new PricingConfigurationException($"The maximum discount is invalid: {checkedMax.Error}");
        }

        decimal amount = decimal.Parse(checkedMax.NormalisedValue, NumberStyles.Number, CultureInfo.InvariantCulture);
        return new DiscountConfiguration(tiers, new Money(amount, currency));
    }
}