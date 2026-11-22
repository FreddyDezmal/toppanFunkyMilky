using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Milkshake.Api.Contracts.Management;
using Milkshake.Api.Security;
using Milkshake.Application.Management;

namespace Milkshake.Api.Controllers;

/// <summary>
/// Manager maintenance of the three frequent-customer discount tiers (URS §3.4.5): thresholds and rates can be
/// edited, and each tier switched on or off (rules Q-01 to Q-04 decided 22 Nov 2026, see DiscountPolicy).
/// </summary>
[ApiController]
[Route("api/manager/discount-tiers")]
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

    /// <summary>Activates a tier (400 if activation is not allowed).</summary>
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

    /// <summary>Deactivates a tier: new prices no longer include its discount.</summary>
    [HttpPost("{tierNumber:int:range(1,3)}/deactivate")]
    [ProducesResponseType<DiscountTierResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DiscountTierResponse>> Deactivate(
        int tierNumber,
        ChangeStatusRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ManagementResult<DiscountTierSummary> result = await tiers.DeactivateAsync(
            tierNumber, request.Version.GetValueOrDefault(), cancellationToken);

        return ToResponse(result);
    }

    private ActionResult<DiscountTierResponse> ToResponse(ManagementResult<DiscountTierSummary> result) =>
        result.Outcome == ManagementOutcome.Success
            ? Ok(DiscountTierResponse.From(result.Value!))
            : this.ToFailureResponse(result);
}