using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Milkshake.Api.Contracts.Management;
using Milkshake.Api.Security;
using Milkshake.Application.Management;

namespace Milkshake.Api.Controllers;

/// <summary>
/// Manager maintenance of flavours, toppings and consistencies (URS §3.4).
/// Items are deactivated, never deleted. Every change is audited. Changes must send the version they were based on;
/// a stale version is rejected with 409 so one manager cannot silently overwrite another's change.
/// </summary>
[ApiController]
[Route("api/manager/catalog/{kind:regex(^(flavours|toppings|consistencies)$)}")]
[Authorize(Policy = AuthorizationPolicies.ManagerOnly)]
public sealed class CatalogManagementController(ICatalogManagementService catalog) : ControllerBase
{
    /// <summary>Lists items with their current price, ordered by display order.</summary>
    /// <param name="kind">flavours, toppings or consistencies.</param>
    /// <param name="status">all (default), active or inactive.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CatalogItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<CatalogItemResponse>>> List(
        CatalogKind kind,
        [FromQuery] StatusFilter status,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(status))
        {
            return this.InvalidStatusFilter();
        }

        IReadOnlyList<CatalogItemSummary> items = await catalog.ListAsync(kind, status, cancellationToken);
        return Ok(items.Select(CatalogItemResponse.From).ToList());
    }

    /// <summary>One item.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<CatalogItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CatalogItemResponse>> Get(CatalogKind kind, int id, CancellationToken cancellationToken)
    {
        CatalogItemSummary? item = await catalog.GetAsync(kind, id, cancellationToken);
        return item is null
            ? this.ToFailureResponse(ManagementResult<CatalogItemSummary>.NotFound())
            : Ok(CatalogItemResponse.From(item));
    }

    /// <summary>Creates an active item with its initial price, effective immediately.</summary>
    /// <response code="201">Created.</response>
    /// <response code="400">Validation failed or the name is already used (ignoring letter case).</response>
    [HttpPost]
    [ProducesResponseType<CatalogItemResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CatalogItemResponse>> Create(
        CatalogKind kind,
        CreateCatalogItemRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ManagementResult<CatalogItemSummary> result = await catalog.CreateAsync(
            kind,
            new CreateCatalogItemCommand(request.Name, request.DisplayOrder.GetValueOrDefault(), request.InitialPrice.GetValueOrDefault()),
            cancellationToken);

        if (result.Outcome != ManagementOutcome.Success)
        {
            return this.ToFailureResponse(result);
        }

        CatalogItemResponse response = CatalogItemResponse.From(result.Value!);
        return CreatedAtAction(nameof(Get), new { kind = RouteName(kind), id = response.Id }, response);
    }

    /// <summary>Renames or reorders an item.</summary>
    /// <response code="409">Someone else changed the item since it was loaded.</response>
    [HttpPut("{id:int}")]
    [ProducesResponseType<CatalogItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CatalogItemResponse>> Update(
        CatalogKind kind,
        int id,
        UpdateCatalogItemRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ManagementResult<CatalogItemSummary> result = await catalog.UpdateAsync(
            kind,
            id,
            new UpdateCatalogItemCommand(request.Name, request.DisplayOrder.GetValueOrDefault(), request.Version.GetValueOrDefault()),
            cancellationToken);

        return ToResponse(result);
    }

    /// <summary>Hides the item from new orders. Existing orders and price history are kept.</summary>
    [HttpPost("{id:int}/deactivate")]
    [ProducesResponseType<CatalogItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<CatalogItemResponse>> Deactivate(CatalogKind kind, int id, ChangeStatusRequest request, CancellationToken cancellationToken) =>
        SetActiveAsync(kind, id, active: false, request, cancellationToken);

    /// <summary>Makes the item available for new orders again.</summary>
    [HttpPost("{id:int}/activate")]
    [ProducesResponseType<CatalogItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ActionResult<CatalogItemResponse>> Activate(CatalogKind kind, int id, ChangeStatusRequest request, CancellationToken cancellationToken) =>
        SetActiveAsync(kind, id, active: true, request, cancellationToken);

    private async Task<ActionResult<CatalogItemResponse>> SetActiveAsync(
        CatalogKind kind,
        int id,
        bool active,
        ChangeStatusRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ManagementResult<CatalogItemSummary> result = await catalog.SetActiveAsync(
            kind, id, active, request.Version.GetValueOrDefault(), cancellationToken);

        return ToResponse(result);
    }

    private ActionResult<CatalogItemResponse> ToResponse(ManagementResult<CatalogItemSummary> result) =>
        result.Outcome == ManagementOutcome.Success
            ? Ok(CatalogItemResponse.From(result.Value!))
            : this.ToFailureResponse(result);

    private static string RouteName(CatalogKind kind) => kind.ToString().ToLowerInvariant();
}