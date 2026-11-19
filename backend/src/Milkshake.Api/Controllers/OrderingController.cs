using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Milkshake.Api.Contracts.Ordering;
using Milkshake.Api.Security;
using Milkshake.Application.Ordering;

namespace Milkshake.Api.Controllers;

/// <summary>
/// Patron ordering (M5): read-only options and stateless draft validation.
/// Nothing here creates an order, stores a draft or takes payment.
/// </summary>
[ApiController]
[Route("api/ordering")]
[Authorize(Policy = AuthorizationPolicies.PatronOnly)]
public sealed class OrderingController(IOrderingCatalogService catalog, IOrderDraftValidator validator) : ControllerBase
{
    /// <summary>
    /// Active flavours, consistencies and toppings with current fees, active restaurants, drink limits and the
    /// provisional pickup window. Fees are not final prices: VAT and discounts are calculated at checkout (M6).
    /// </summary>
    /// <response code="200">The options.</response>
    /// <response code="401">Not signed in.</response>
    /// <response code="403">Signed in without the Patron role.</response>
    /// <response code="503">Ordering configuration is invalid; details are in the server log.</response>
    [HttpGet("options")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<OrderingOptionsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OrderingOptionsResponse>> GetOptions(CancellationToken cancellationToken)
    {
        OrderingOptionsResult result = await catalog.GetOptionsAsync(cancellationToken);

        return result.Options is null
            ? Unavailable()
            : Ok(OrderingOptionsResponse.From(result.Options));
    }

    /// <summary>
    /// Checks a draft against the options in force now: drink count within the configured limits, exactly one active
    /// flavour, consistency and topping per drink, an active restaurant, and a pickup time inside the provisional
    /// pickup window (business time zone). Stores nothing.
    /// </summary>
    /// <response code="200">Every selection is valid; the resolved draft summary.</response>
    /// <response code="400">Missing, unavailable or malformed selections, keyed by field (e.g. drinks[1].toppingId).</response>
    /// <response code="503">Ordering configuration is invalid.</response>
    [HttpPost("draft/validate")]
    [ProducesResponseType<DraftSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DraftSummaryResponse>> ValidateDraft(ValidateDraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DraftValidationResult result = await validator.ValidateAsync(request.ToDraft(), cancellationToken);

        if (result.OrderingUnavailable)
        {
            return Unavailable();
        }

        if (result.Draft is null)
        {
            foreach ((string field, string[] messages) in result.Errors)
            {
                foreach (string message in messages)
                {
                    ModelState.AddModelError(field, message);
                }
            }

            return ValidationProblem(ModelState);
        }

        return Ok(DraftSummaryResponse.From(result.Draft));
    }

    private ObjectResult Unavailable() =>
        Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: OrderingMessages.UnavailableTitle,
            detail: OrderingMessages.UnavailableDetail);
}