namespace Milkshake.Application.Management;

/// <summary>Which records a management list returns.</summary>
public enum StatusFilter
{
    /// <summary>Active and inactive.</summary>
    All,

    /// <summary>Only records available for new orders.</summary>
    Active,

    /// <summary>Only deactivated records.</summary>
    Inactive,
}