namespace Milkshake.Application.Ordering;

/// <summary>One drink as chosen in the browser: identifiers only, never prices.</summary>
public sealed record DraftDrinkSelection(int? FlavourId, int? ConsistencyId, int? ToppingId);

/// <summary>An in-progress order as sent by the browser. Not an order: nothing is stored.</summary>
public sealed record OrderDraft(int? RestaurantId, IReadOnlyList<DraftDrinkSelection?> Drinks);

/// <summary>A drink whose choices are all active and priced right now.</summary>
public sealed record ValidatedDrink(int Number, OrderingOption Flavour, OrderingOption Consistency, OrderingOption Topping);

/// <summary>A draft whose selections are all valid right now. Still a draft – not ordered or paid.</summary>
public sealed record ValidatedDraft(OrderingRestaurant Restaurant, IReadOnlyList<ValidatedDrink> Drinks);

/// <summary>A validated draft, field-level errors, or "ordering unavailable" (invalid configuration).</summary>
public sealed record DraftValidationResult(ValidatedDraft? Draft, IReadOnlyDictionary<string, string[]> Errors, bool OrderingUnavailable)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    /// <summary>Every selection is valid.</summary>
    public static DraftValidationResult Valid(ValidatedDraft draft) => new(draft, NoErrors, false);

    /// <summary>Some selections are missing or no longer available.</summary>
    public static DraftValidationResult Invalid(IReadOnlyDictionary<string, string[]> errors) => new(null, errors, false);

    /// <summary>Ordering cannot be offered right now.</summary>
    public static DraftValidationResult Unavailable() => new(null, NoErrors, true);
}

/// <summary>Error keys matching the request's JSON paths, so the UI can attach each message to its field.</summary>
public static class OrderDraftFields
{
    /// <summary>The pickup restaurant.</summary>
    public const string RestaurantId = "restaurantId";

    /// <summary>The list of drinks (quantity).</summary>
    public const string Drinks = "drinks";

    /// <summary>A whole drink entry.</summary>
    public static string Drink(int index) => $"drinks[{index}]";

    /// <summary>A drink's flavour.</summary>
    public static string Flavour(int index) => $"drinks[{index}].flavourId";

    /// <summary>A drink's consistency.</summary>
    public static string Consistency(int index) => $"drinks[{index}].consistencyId";

    /// <summary>A drink's topping.</summary>
    public static string Topping(int index) => $"drinks[{index}].toppingId";
}

/// <summary>User-facing draft validation messages.</summary>
public static class OrderDraftMessages
{
    /// <summary>No restaurant chosen.</summary>
    public const string RestaurantRequired = "Choose a pickup restaurant.";

    /// <summary>Restaurant inactive or unknown.</summary>
    public const string RestaurantUnavailable = "The selected restaurant is no longer available. Please choose another.";

    /// <summary>Drink count outside the configured limits.</summary>
    public static string Quantity(int min, int max) =>
        min == max ? $"Order exactly {min} milkshake(s)." : $"Choose between {min} and {max} milkshakes.";

    /// <summary>A drink entry was empty.</summary>
    public static string DrinkMissing(int number) => $"Milkshake {number} has no selections.";

    /// <summary>A required choice is missing.</summary>
    public static string Choose(string label, int number) => $"Choose a {label} for milkshake {number}.";

    /// <summary>Unknown, inactive or unpriced choice (deliberately the same message for all three).</summary>
    public static string NoLongerAvailable(string label, int number) =>
        $"The {label} chosen for milkshake {number} is no longer available. Please choose another.";
}