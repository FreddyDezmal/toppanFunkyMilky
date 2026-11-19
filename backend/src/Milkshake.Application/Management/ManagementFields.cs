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

    
    /// <summary>Price amount.</summary>
    public const string Amount = "amount";

    /// <summary>When a price starts.</summary>
    public const string EffectiveFrom = "effectiveFrom";

    /// <summary>A price period.</summary>
    public const string PriceId = "priceId";

    
    /// <summary>Setting value.</summary>
    public const string Value = "value";

    /// <summary>Discount tier: paid orders needed.</summary>
    public const string MinPreviousOrders = "minPreviousOrders";

    /// <summary>Discount tier: drinks an order needs.</summary>
    public const string MinDrinksPerOrder = "minDrinksPerOrder";

    /// <summary>Discount tier: rate as a fraction.</summary>
    public const string DiscountRate = "discountRate";

    /// <summary>Active flag.</summary>
    public const string IsActive = "isActive";
}