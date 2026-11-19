namespace Milkshake.Application.Ordering;

/// <summary>
/// Server-side rules for a draft (URS §3.4 field table, AC-5.1 to AC-5.8): drink count within the configured limits,
/// exactly one active flavour, consistency and topping per drink, an active restaurant, and a pickup time inside the
/// provisional pickup window (<see cref="PickupRules"/>). Pure: the caller supplies the options in force now.
/// </summary>
public static class OrderDraftRules
{
    /// <summary>Validates <paramref name="draft"/> against <paramref name="options"/>.</summary>
    public static DraftValidationResult Validate(OrderDraft draft, OrderingOptions options)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(options);

        var errors = new Dictionary<string, string[]>();
        OrderingRestaurant? restaurant = ValidateRestaurant(draft.RestaurantId, options, errors);
        ValidatedPickup? pickup = ValidatePickup(draft.PickupLocal, options, errors);
        List<ValidatedDrink> drinks = ValidateDrinks(draft.Drinks, options, errors);

        return errors.Count == 0 && restaurant is not null && pickup is not null
            ? DraftValidationResult.Valid(new ValidatedDraft(restaurant, pickup, drinks))
            : DraftValidationResult.Invalid(errors);
    }

    private static OrderingRestaurant? ValidateRestaurant(int? restaurantId, OrderingOptions options, Dictionary<string, string[]> errors)
    {
        if (restaurantId is not int id)
        {
            Add(errors, OrderDraftFields.RestaurantId, OrderDraftMessages.RestaurantRequired);
            return null;
        }

        OrderingRestaurant? restaurant = options.Restaurants.FirstOrDefault(r => r.Id == id);
        if (restaurant is null)
        {
            Add(errors, OrderDraftFields.RestaurantId, OrderDraftMessages.RestaurantUnavailable);
        }

        return restaurant;
    }

    private static ValidatedPickup? ValidatePickup(string? pickupLocal, OrderingOptions options, Dictionary<string, string[]> errors)
    {
        PickupCheck check = PickupRules.Check(pickupLocal, options.Settings, options.Pickup);
        if (check.Error is not null)
        {
            Add(errors, OrderDraftFields.PickupLocal, check.Error);
        }

        return check.Pickup;
    }

    private static List<ValidatedDrink> ValidateDrinks(
        IReadOnlyList<DraftDrinkSelection?>? selections,
        OrderingOptions options,
        Dictionary<string, string[]> errors)
    {
        IReadOnlyList<DraftDrinkSelection?> drinks = selections ?? [];
        int min = options.Settings.MinDrinks;
        int max = options.Settings.MaxDrinks;

        // Outside the limits: report only the quantity (a tampered request with thousands of drinks gets one message).
        if (drinks.Count < min || drinks.Count > max)
        {
            Add(errors, OrderDraftFields.Drinks, OrderDraftMessages.Quantity(min, max));
            return [];
        }

        Dictionary<int, OrderingOption> flavours = options.Flavours.ToDictionary(o => o.Id);
        Dictionary<int, OrderingOption> consistencies = options.Consistencies.ToDictionary(o => o.Id);
        Dictionary<int, OrderingOption> toppings = options.Toppings.ToDictionary(o => o.Id);

        var validated = new List<ValidatedDrink>(drinks.Count);
        for (int index = 0; index < drinks.Count; index++)
        {
            int number = index + 1;
            DraftDrinkSelection? drink = drinks[index];
            if (drink is null)
            {
                Add(errors, OrderDraftFields.Drink(index), OrderDraftMessages.DrinkMissing(number));
                continue;
            }

            OrderingOption? flavour = Pick(drink.FlavourId, flavours, OrderDraftFields.Flavour(index), "flavour", number, errors);
            OrderingOption? consistency = Pick(drink.ConsistencyId, consistencies, OrderDraftFields.Consistency(index), "consistency", number, errors);
            OrderingOption? topping = Pick(drink.ToppingId, toppings, OrderDraftFields.Topping(index), "topping", number, errors);

            if (flavour is not null && consistency is not null && topping is not null)
            {
                validated.Add(new ValidatedDrink(number, flavour, consistency, topping));
            }
        }

        return validated;
    }

    private static OrderingOption? Pick(
        int? id,
        Dictionary<int, OrderingOption> available,
        string field,
        string label,
        int number,
        Dictionary<string, string[]> errors)
    {
        if (id is not int value)
        {
            Add(errors, field, OrderDraftMessages.Choose(label, number));
            return null;
        }

        if (!available.TryGetValue(value, out OrderingOption? option))
        {
            Add(errors, field, OrderDraftMessages.NoLongerAvailable(label, number));
            return null;
        }

        return option;
    }

    private static void Add(Dictionary<string, string[]> errors, string field, string message) =>
        errors[field] = errors.TryGetValue(field, out string[]? existing) ? [.. existing, message] : [message];
}