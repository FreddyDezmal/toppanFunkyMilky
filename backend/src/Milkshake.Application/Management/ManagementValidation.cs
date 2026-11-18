namespace Milkshake.Application.Management;

/// <summary>Input checks shared by management services. Messages are keyed by JSON field name.</summary>
public static class ManagementValidation
{
    /// <summary>Largest amount the database column can hold (numeric(12,2)): a technical limit, not a business rule.</summary>
    public const decimal MaxAmount = 9_999_999_999.99m;

    /// <summary>Required name of at most <paramref name="maxLength"/> characters after trimming.</summary>
    public static void Name(Dictionary<string, string[]> errors, string field, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(errors, field, "Name is required.");
        }
        else if (value.Trim().Length > maxLength)
        {
            Add(errors, field, $"Name cannot exceed {maxLength} characters.");
        }
    }

    /// <summary>Optional text of at most <paramref name="maxLength"/> characters after trimming.</summary>
    public static void OptionalText(Dictionary<string, string[]> errors, string field, string label, string? value, int maxLength)
    {
        if (value is not null && value.Trim().Length > maxLength)
        {
            Add(errors, field, $"{label} cannot exceed {maxLength} characters.");
        }
    }

    /// <summary>Display order must not be negative.</summary>
    public static void DisplayOrder(Dictionary<string, string[]> errors, string field, int value)
    {
        if (value < 0)
        {
            Add(errors, field, "Display order cannot be negative.");
        }
    }

    /// <summary>Non-negative amount with at most two decimal places.</summary>
    public static void Amount(Dictionary<string, string[]> errors, string field, decimal? value)
    {
        if (value is not decimal amount)
        {
            Add(errors, field, "Price is required.");
        }
        else if (amount < 0)
        {
            Add(errors, field, "Price cannot be negative.");
        }
        else if (decimal.Round(amount, 2) != amount)
        {
            Add(errors, field, "Price can have at most two decimal places.");
        }
        else if (amount > MaxAmount)
        {
            Add(errors, field, "Price is too large.");
        }
    }

    /// <summary>Adds a message to a field.</summary>
    public static void Add(Dictionary<string, string[]> errors, string field, string message)
    {
        ArgumentNullException.ThrowIfNull(errors);
        errors[field] = errors.TryGetValue(field, out string[]? existing) ? [.. existing, message] : [message];
    }
}