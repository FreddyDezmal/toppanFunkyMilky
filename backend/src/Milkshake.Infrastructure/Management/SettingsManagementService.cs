using Microsoft.EntityFrameworkCore;
using Milkshake.Application.Management;
using Milkshake.Domain.Settings;
using Milkshake.Infrastructure.Persistence;

namespace Milkshake.Infrastructure.Management;

/// <summary>Manager maintenance of system settings. Audited by the SaveChanges interceptor; xmin-protected.</summary>
internal sealed class SettingsManagementService(MilkshakeDbContext dbContext) : ISettingsManagementService
{
    public async Task<IReadOnlyList<SettingSummary>> ListAsync(CancellationToken cancellationToken)
    {
        List<SystemSetting> settings = await dbContext.SystemSettings.AsNoTracking().ToListAsync(cancellationToken);
        return ToSummaries(settings);
    }

    public async Task<ManagementResult<IReadOnlyList<SettingSummary>>> UpdateAsync(UpdateSettingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A handful of rows: load them all (tracked) so cross-checks see current values.
        List<SystemSetting> settings = await dbContext.SystemSettings.ToListAsync(cancellationToken);
        SystemSetting? setting = settings.SingleOrDefault(s => s.Key == command.Key);
        if (setting is null)
        {
            return ManagementResult<IReadOnlyList<SettingSummary>>.NotFound();
        }

        if (setting.Version != command.Version)
        {
            return ManagementResult<IReadOnlyList<SettingSummary>>.Conflict();
        }

        Dictionary<string, string> current = settings.ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
        SettingValidation validation = SettingRules.Validate(setting.Key, command.Value, current);
        if (!validation.IsValid)
        {
            return ManagementResult<IReadOnlyList<SettingSummary>>.Invalid(ManagementFields.Value, validation.Error!);
        }

        if (validation.NormalisedValue == setting.Value)
        {
            return ManagementResult<IReadOnlyList<SettingSummary>>.Success(ToSummaries(settings));
        }

        setting.ChangeValue(validation.NormalisedValue!);

        // Minimum and maximum drinks are validated against each other. Version-check the partner row as well, so
        // two managers changing one each at the same moment cannot together produce minimum > maximum.
        SystemSetting? partner = settings.SingleOrDefault(s => s.Key == SettingRules.PartnerOf(setting.Key));
        if (partner is not null)
        {
            dbContext.Entry(partner).Property(s => s.UpdatedAt).IsModified = true;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ManagementResult<IReadOnlyList<SettingSummary>>.Conflict();
        }

        return ManagementResult<IReadOnlyList<SettingSummary>>.Success(ToSummaries(settings));
    }

    private static List<SettingSummary> ToSummaries(IEnumerable<SystemSetting> settings) =>
        settings
            .OrderBy(s => SettingRules.SortIndex(s.Key))
            .ThenBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => new SettingSummary(
                s.Key, s.Value, s.ValueType.ToString(), s.Description,
                SettingRules.IsEditable(s.Key), SettingRules.Hint(s.Key), s.Version))
            .ToList();
}