namespace Milkshake.Application.Ordering;

/// <summary>Validates a draft against the options in force right now (stale selections are caught).</summary>
public interface IOrderDraftValidator
{
    /// <summary>Loads the current options and applies <see cref="OrderDraftRules"/>.</summary>
    Task<DraftValidationResult> ValidateAsync(OrderDraft draft, CancellationToken cancellationToken);
}

/// <summary>Default validator; M7 reuses it before creating a real order.</summary>
internal sealed class OrderDraftValidator(IOrderingCatalogService catalog) : IOrderDraftValidator
{
    public async Task<DraftValidationResult> ValidateAsync(OrderDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        OrderingOptionsResult current = await catalog.GetOptionsAsync(cancellationToken);
        return current.Options is null
            ? DraftValidationResult.Unavailable()
            : OrderDraftRules.Validate(draft, current.Options);
    }
}