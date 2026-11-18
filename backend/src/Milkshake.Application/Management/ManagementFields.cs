namespace Milkshake.Application.Management;

/// <summary>JSON field names used as validation-error keys, matching the API contracts.</summary>
public static class ManagementFields
{
    /// <summary>Display name.</summary>
    public const string Name = "name";

    /// <summary>Position in lists.</summary>
    public const string DisplayOrder = "displayOrder";

    /// <summary>Price set when an item is created.</summary>
    public const string InitialPrice = "initialPrice";

    /// <summary>Restaurant address.</summary>
    public const string Address = "address";
}