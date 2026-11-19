using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Milkshake.Api.Contracts.Management;
using Milkshake.Api.Security;
using Milkshake.Application.Management;

namespace Milkshake.Api.Controllers;

/// <summary>
/// Manager maintenance of the three frequent-customer discount tiers (URS §3.4.5). Thresholds and rates can be
/// edited; activation is refused until the discount rules are confirmed (Q-01 to Q-04).
/// </summary>
[ApiController]
[Route("api/manager/discount-tiers")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.ManagerOnly)]
public sealed class DiscountTierManagementController(IDiscountTierManagementService tiers) : ControllerBase
{
    /// <summary>Tiers 1 to 3.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DiscountTierResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DiscountTierResponse>>> List(CancellationToken cancellationToken)
    {
        IReadOnlyList<DiscountTierSummary> found = await tiers.ListAsync(cancellationToken);
        return Ok(found.Select(DiscountTierResponse.From).ToList());
    }

    /// <summary>Changes a tier's thresholds and rate.</summary>
    [HttpPut("{tierNumber:int:range(1,3)}")]
    [ProducesResponseType<DiscountTierResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DiscountTierResponse>> Update(
        int tierNumber,
        UpdateDiscountTierRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ManagementResult<DiscountTierSummary> result = await tiers.UpdateAsync(
            tierNumber,
            new UpdateDiscountTierCommand(
                request.MinPreviousOrders.GetValueOrDefault(),
                request.MinDrinksPerOrder.GetValueOrDefault(),
                request.DiscountRate.GetValueOrDefault(),
                request.Version.GetValueOrDefault()),
            cancellationToken);

        return ToResponse(result);
    }

    /// <summary>Activates a tier. Currently always refused (400) until the discount rules are confirmed.</summary>
    [HttpPost("{tierNumber:int:range(1,3)}/activate")]
    [ProducesResponseType<DiscountTierResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DiscountTierResponse>> Activate(
        int tierNumber,
        ChangeStatusRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ManagementResult<DiscountTierSummary> result = await tiers.ActivateAsync(
            tierNumber, request.Version.GetValueOrDefault(), cancellationToken);

        return ToResponse(result);
    }

    private ActionResult<DiscountTierResponse> ToResponse(ManagementResult<DiscountTierSummary> result) =>
        result.Outcome == ManagementOutcome.Success
            ? Ok(DiscountTierResponse.From(result.Value!))
            : this.ToFailureResponse(result);
}