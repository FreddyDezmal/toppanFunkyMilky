using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Milkshake.Api.Contracts.Management;
using Milkshake.Api.Security;
using Milkshake.Application.Management;

namespace Milkshake.Api.Controllers;

/// <summary>
/// Manager maintenance of pickup restaurants (URS §3.1). Deactivated, never deleted; audited;
/// changes must send the version they were based on (stale = 409).
/// </summary>
[ApiController]
[Route("api/manager/restaurants")]
[Authorize(Policy = AuthorizationPolicies.ManagerOnly)]
public sealed class RestaurantManagementController(IRestaurantManagementService restaurants) : ControllerBase
{
    /// <summary>Lists restaurants ordered by name.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RestaurantResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<RestaurantResponse>>> List([FromQuery] StatusFilter status, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(status))
        {
            return this.InvalidStatusFilter();
        }

        IReadOnlyList<RestaurantSummary> found = await restaurants.ListAsync(status, cancellationToken);
        return Ok(found.Select(RestaurantResponse.From).ToList());
    }

    /// <summary>One restaurant.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<RestaurantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RestaurantResponse>> Get(int id, CancellationToken cancellationToken)
    {
        RestaurantSummary? restaurant = await restaurants.GetAsync(id, cancellationToken);
        return restaurant is null
            ? this.ToFailureResponse(ManagementResult<RestaurantSummary>.NotFound())
            : Ok(RestaurantResponse.From(restaurant));
    }

    /// <summary>Creates an active restaurant.</summary>
    [HttpPost]
    [ProducesResponseType<RestaurantResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RestaurantResponse>> Create(CreateRestaurantRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ManagementResult<RestaurantSummary> result = await restaurants.CreateAsync(
            new CreateRestaurantCommand(request.Name, request.Address), cancellationToken);

        if (result.Outcome != ManagementOutcome.Success)
        {
            return this.ToFailureResponse(result);
        }

        RestaurantResponse response = RestaurantResponse.From(result.Value!);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    /// <summary>Changes name or address.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType<RestaurantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RestaurantResponse>> Update(int id, UpdateRestaurantRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ManagementResult<RestaurantSummary> result = await restaurants.UpdateAsync(
            id,
            new UpdateRestaurantCommand(request.Name, request.Address, request.Version.GetValueOrDefault()),
            cancellationToken);

        return ToResponse(result);
    }

    /// <summary>Hides the restaurant from new orders.</summary>
    [HttpPost("{id:int}/deactivate")]
    [ProducesResponseType<RestaurantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<RestaurantResponse>> Deactivate(int id, ChangeStatusRequest request, CancellationToken cancellationToken) =>
        SetActiveAsync(id, active: false, request, cancellationToken);

    /// <summary>Makes the restaurant available again.</summary>
    [HttpPost("{id:int}/activate")]
    [ProducesResponseType<RestaurantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<RestaurantResponse>> Activate(int id, ChangeStatusRequest request, CancellationToken cancellationToken) =>
        SetActiveAsync(id, active: true, request, cancellationToken);

    private async Task<ActionResult<RestaurantResponse>> SetActiveAsync(int id, bool active, ChangeStatusRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ManagementResult<RestaurantSummary> result = await restaurants.SetActiveAsync(
            id, active, request.Version.GetValueOrDefault(), cancellationToken);

        return ToResponse(result);
    }

    private ActionResult<RestaurantResponse> ToResponse(ManagementResult<RestaurantSummary> result) =>
        result.Outcome == ManagementOutcome.Success
            ? Ok(RestaurantResponse.From(result.Value!))
            : this.ToFailureResponse(result);
}