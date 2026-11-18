namespace Milkshake.Domain.Catalog;

/// <summary>User-facing messages for price-schedule rules.</summary>
public static class PricingMessages
{
    /// <summary>Back-dating is not allowed.</summary>
    public const string NoBackDating =
        "A new price must start now or in the future. Prices that have taken effect cannot be changed.";

    /// <summary>New price would start before (or with) the latest scheduled price.</summary>
    public const string MustStartAfterLatest =
        "A new price must start after the latest scheduled price. Cancel the scheduled price first to choose an earlier date.";

    /// <summary>Nothing in the future to cancel.</summary>
    public const string NothingScheduled =
        "There is no future price to cancel. Prices that have taken effect cannot be changed.";

    /// <summary>Cancelling would leave the item without a price.</summary>
    public const string CannotCancelOnlyPrice = "An item's only price cannot be cancelled.";
}