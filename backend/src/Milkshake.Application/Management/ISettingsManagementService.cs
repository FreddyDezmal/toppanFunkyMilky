namespace Milkshake.Application.Management;

/// <summary>Manager maintenance of system settings (URS §3.4.6). Values are validated by <see cref="SettingRules"/>.</summary>
public interface ISettingsManagementService
{
    /// <summary>All settings in screen order.</summary>
    Task<IReadOnlyList<SettingSummary>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Changes one setting. Returns all settings, because saving minimum or maximum drinks also changes
    /// the other one's version.
    /// </summary>
    Task<ManagementResult<IReadOnlyList<SettingSummary>>> UpdateAsync(UpdateSettingCommand command, CancellationToken cancellationToken);
}

/// <summary>A new value for one setting, based on the version the manager saw.</summary>
public sealed record UpdateSettingCommand(string Key, string Value, uint Version);

/// <summary>A setting as managers see it.</summary>
public sealed record SettingSummary(
    string Key,
    string Value,
    string ValueType,
    string? Description,
    bool IsEditable,
    string Hint,
    uint Version);