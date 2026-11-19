using System.ComponentModel.DataAnnotations;
using Milkshake.Application.Management;

namespace Milkshake.Api.Contracts.Management;

/// <summary>A system setting. Values are invariant-culture text, e.g. "0.15". Send Version back with a change.</summary>
public sealed record SettingResponse(
    string Key,
    string Value,
    string ValueType,
    string? Description,
    bool IsEditable,
    string Hint,
    uint Version)
{
    /// <summary>Maps an application summary.</summary>
    public static SettingResponse From(SettingSummary s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new(s.Key, s.Value, s.ValueType, s.Description, s.IsEditable, s.Hint, s.Version);
    }
}

/// <summary>New value for one setting.</summary>
public sealed class UpdateSettingRequest
{
    /// <summary>Invariant-culture text, e.g. "12", "0.15" or "en-ZA".</summary>
    [Required(ErrorMessage = "A value is required.")]
    [StringLength(500, ErrorMessage = "Value cannot exceed 500 characters.")]
    public string Value { get; init; } = string.Empty;

    /// <summary>The version returned when the setting was loaded.</summary>
    [Required(ErrorMessage = "Version is required. Reload and try again.")]
    public uint? Version { get; init; }
}

/// <summary>A frequent-customer discount tier.</summary>
public sealed record DiscountTierResponse(
    int TierNumber,
    int MinPreviousOrders,
    int MinDrinksPerOrder,
    decimal DiscountRate,
    bool IsActive,
    bool CanActivate,
    string? ActivationBlockedReason,
    uint Version)
{
    /// <summary>Maps an application summary.</summary>
    public static DiscountTierResponse From(DiscountTierSummary t)
    {
        ArgumentNullException.ThrowIfNull(t);
        return new(t.TierNumber, t.MinPreviousOrders, t.MinDrinksPerOrder, t.DiscountRate, t.IsActive,
            t.CanActivate, t.ActivationBlockedReason, t.Version);
    }
}

/// <summary>New thresholds and rate for a tier.</summary>
public sealed class UpdateDiscountTierRequest
{
    /// <summary>Paid orders a patron needs before this tier applies.</summary>
    [Required(ErrorMessage = "Previous orders is required.")]
    public int? MinPreviousOrders { get; init; }

    /// <summary>Drinks an order needs to count towards this tier.</summary>
    [Required(ErrorMessage = "Drinks per order is required.")]
    public int? MinDrinksPerOrder { get; init; }

    /// <summary>Rate as a fraction, e.g. 0.05 for 5%.</summary>
    [Required(ErrorMessage = "Discount rate is required.")]
    public decimal? DiscountRate { get; init; }

    /// <summary>The version returned when the tier was loaded.</summary>
    [Required(ErrorMessage = "Version is required. Reload and try again.")]
    public uint? Version { get; init; }
}