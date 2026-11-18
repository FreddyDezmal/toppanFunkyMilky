using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Milkshake.Api.Contracts.Management;
using Milkshake.Api.Security;
using Milkshake.Application.Management;

namespace Milkshake.Api.Controllers;

/// <summary>
/// Effective-dated prices of one flavour, topping or consistency. Prices that have taken effect are never
/// edited, so orders keep the price that applied when they were placed. Changes send the item version (stale = 409).
/// </summary>
[ApiController]
[Route("api/manager/catalog/{kind:regex(^(flavours|toppings|consistencies)$)}/{id:int}/prices")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.ManagerOnly)]
public sealed class CatalogPricingController(ICatalogPricingService pricing) : ControllerBase
{
    /// <summary>The item and its full price history, newest first.</summary>
    [HttpGet]
    [ProducesResponseType<PriceHistoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PriceHistoryResponse>> Get(CatalogKind kind, int id, CancellationToken cancellationToken)
    {
        PriceHistorySummary? history = await pricing.GetHistoryAsync(kind, id, cancellationToken);
        return history is null
            ? this.ToFailureResponse(ManagementResult<PriceHistorySummary>.NotFound())
            : Ok(PriceHistoryResponse.From(history));
    }

    /// <summary>Schedules a new price, immediately or from a future moment.</summary>
    /// <response code="400">Invalid amount, a start in the past, or a start before an already scheduled price.</response>
    /// <response code="409">Someone else changed the item or its prices since it was loaded.</response>
    [HttpPost]
    [ProducesResponseType<PriceHistoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PriceHistoryResponse>> Schedule(
        CatalogKind kind,
        int id,
        SchedulePriceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ManagementResult<PriceHistorySummary> result = await pricing.ScheduleAsync(
            kind,
            id,
            new SchedulePriceCommand(request.Amount.GetValueOrDefault(), request.EffectiveFrom, request.Version.GetValueOrDefault()),
            cancellationToken);

        return ToResponse(result);
    }

    /// <summary>Cancels the latest price if it has not started yet; the previous price continues.</summary>
    [HttpPost("{priceId:int}/cancel")]
    [ProducesResponseType<PriceHistoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PriceHistoryResponse>> Cancel(
        CatalogKind kind,
        int id,
        int priceId,
        ChangeStatusRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ManagementResult<PriceHistorySummary> result = await pricing.CancelScheduledAsync(
            kind, id, priceId, request.Version.GetValueOrDefault(), cancellationToken);

        return ToResponse(result);
    }

    private ActionResult<PriceHistoryResponse> ToResponse(ManagementResult<PriceHistorySummary> result) =>
        result.Outcome == ManagementOutcome.Success
            ? Ok(PriceHistoryResponse.From(result.Value!))
            : this.ToFailureResponse(result);
}