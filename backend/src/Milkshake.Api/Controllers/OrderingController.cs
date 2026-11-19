using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Milkshake.Api.Contracts.Ordering;
using Milkshake.Api.Security;
using Milkshake.Application.Ordering;

namespace Milkshake.Api.Controllers;

/// <summary>
/// Patron ordering (M5): read-only options now; draft validation follows in the next steps.
/// Nothing here creates an order or takes payment.
/// </summary>
[ApiController]
[Route("api/ordering")]
[Authorize(Policy = AuthorizationPolicies.PatronOnly)]
public sealed class OrderingController(IOrderingCatalogService catalog) : ControllerBase
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
            ? Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: OrderingMessages.UnavailableTitle,
                detail: OrderingMessages.UnavailableDetail)
            : Ok(OrderingOptionsResponse.From(result.Options));
    }
}