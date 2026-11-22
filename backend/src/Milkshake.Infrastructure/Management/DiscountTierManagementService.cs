using Microsoft.EntityFrameworkCore;
using Milkshake.Application.Management;
using Milkshake.Domain.Settings;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Infrastructure.Management;

/// <summary>Manager maintenance of discount tiers. Audited; xmin-protected; activation gated by DiscountPolicy.</summary>
internal sealed class DiscountTierManagementService(MilkshakeDbContext dbContext) : IDiscountTierManagementService
{
    // numeric(5,4): a technical storage limit, not a business rule.
    private const int RateDecimalPlaces = 4;

    public async Task<IReadOnlyList<DiscountTierSummary>> ListAsync(CancellationToken cancellationToken)
    {
        List<DiscountTier> tiers = await dbContext.DiscountTiers.AsNoTracking()
            .OrderBy(t => t.TierNumber)
            .ToListAsync(cancellationToken);

        return tiers.ConvertAll(ToSummary);
    }

    public async Task<ManagementResult<DiscountTierSummary>> UpdateAsync(int tierNumber, UpdateDiscountTierCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new Dictionary<string, string[]>();
        if (command.MinPreviousOrders < 0)
        {
            ManagementValidation.Add(errors, ManagementFields.MinPreviousOrders, "Previous orders cannot be negative.");
        }

        if (command.MinDrinksPerOrder < 1)
        {
            ManagementValidation.Add(errors, ManagementFields.MinDrinksPerOrder, "Drinks per order must be at least 1.");
        }

        if (command.DiscountRate < 0m || command.DiscountRate > 1m)
        {
            ManagementValidation.Add(errors, ManagementFields.DiscountRate, "Discount rate must be between 0 and 1, e.g. 0.05 for 5%.");
        }
        else if (decimal.Round(command.DiscountRate, RateDecimalPlaces) != command.DiscountRate)
        {
            ManagementValidation.Add(errors, ManagementFields.DiscountRate, $"Discount rate can have at most {RateDecimalPlaces} decimal places.");
        }

        if (errors.Count > 0)
        {
            return ManagementResult<DiscountTierSummary>.Invalid(errors);
        }

        DiscountTier? tier = await dbContext.DiscountTiers.SingleOrDefaultAsync(t => t.TierNumber == tierNumber, cancellationToken);
        if (tier is null)
        {
            return ManagementResult<DiscountTierSummary>.NotFound();
        }

        if (tier.Version != command.Version)
        {
            return ManagementResult<DiscountTierSummary>.Conflict();
        }

        tier.Update(command.MinPreviousOrders, command.MinDrinksPerOrder, command.DiscountRate);
        return await SaveAsync(tier, cancellationToken);
    }

    public async Task<ManagementResult<DiscountTierSummary>> ActivateAsync(int tierNumber, uint version, CancellationToken cancellationToken)
    {
        DiscountTier? tier = await dbContext.DiscountTiers.SingleOrDefaultAsync(t => t.TierNumber == tierNumber, cancellationToken);
        if (tier is null)
        {
            return ManagementResult<DiscountTierSummary>.NotFound();
        }

        if (!DiscountPolicy.RulesConfirmed)
        {
            return ManagementResult<DiscountTierSummary>.Invalid(ManagementFields.IsActive, DiscountPolicy.ActivationBlockedMessage);
        }

        if (tier.Version != version)
        {
            return ManagementResult<DiscountTierSummary>.Conflict();
        }

        tier.Activate();
        return await SaveAsync(tier, cancellationToken);
    }

    public async Task<ManagementResult<DiscountTierSummary>> DeactivateAsync(int tierNumber, uint version, CancellationToken cancellationToken)
    {
        DiscountTier? tier = await dbContext.DiscountTiers.SingleOrDefaultAsync(t => t.TierNumber == tierNumber, cancellationToken);
        if (tier is null)
        {
            return ManagementResult<DiscountTierSummary>.NotFound();
        }

        if (tier.Version != version)
        {
            return ManagementResult<DiscountTierSummary>.Conflict();
        }

        tier.Deactivate();
        return await SaveAsync(tier, cancellationToken);
    }

    private async Task<ManagementResult<DiscountTierSummary>> SaveAsync(DiscountTier tier, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ManagementResult<DiscountTierSummary>.Conflict();
        }

        return ManagementResult<DiscountTierSummary>.Success(ToSummary(tier));
    }

    private static DiscountTierSummary ToSummary(DiscountTier t) =>
        new(
            t.TierNumber,
            t.MinPreviousOrders,
            t.MinDrinksPerOrder,
            t.DiscountRate,
            t.IsActive,
            CanActivate: DiscountPolicy.RulesConfirmed && !t.IsActive,
            ActivationBlockedReason: DiscountPolicy.RulesConfirmed ? null : DiscountPolicy.ActivationBlockedMessage,
            t.Version);
}