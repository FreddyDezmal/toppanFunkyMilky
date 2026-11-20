using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Milkshake.Api.Contracts.Ordering;
using Milkshake.Api.Security;
using Milkshake.Application.Management;

namespace Milkshake.Api.Controllers;

/// <summary>
/// Management → Orders (M8b, Q3): find an order by reference, ask its patron to accept cancelling it (B1.2), and list
/// refunds that need manual handling (Q4). Managers never cancel or refund directly.
/// </summary>
[ApiController]
[Route("api/manager")]
[Authorize(Policy = AuthorizationPolicies.ManagerOnly)]
public sealed class ManagerOrdersController(IManagerOrderService orders) : ControllerBase
{
    /// <summary>One order by its reference, with its cancellation requests and refunds.</summary>
    /// <response code="200">The order.</response>
    /// <response code="404">No order with this reference.</response>
    [HttpGet("orders/{reference}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<ManagerOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagerOrderResponse>> GetOrder(string reference, CancellationToken cancellationToken)
    {
        ManagerOrderView? order = await orders.GetAsync(reference, cancellationToken);
        return order is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: OrderMessages.NotFoundTitle)
            : Ok(ManagerOrderResponse.From(order));
    }

    /// <summary>
    /// Asks the patron to accept cancelling their order (B1.2). The order is not changed until the patron accepts; if
    /// they do and it was paid, they are refunded in full (Q1). Unanswered requests lapse at the pickup time.
    /// </summary>
    /// <response code="201">Requested; the body is the order with the new request.</response>
    /// <response code="400">Missing or invalid reason, or unknown fields.</response>
    /// <response code="404">No order with this reference.</response>
    /// <response code="409">A request is already waiting for the patron.</response>
    /// <response code="422">The order is cancelled or its pickup time has passed.</response>
    [HttpPost("orders/{reference}/cancellation-requests")]
    [RequestBodyLimit(1024)]
    [ProducesResponseType<ManagerOrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ManagerOrderResponse>> RequestCancellation(
        string reference, RequestCancellationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequestCancellationResult result = await orders.RequestCancellationAsync(reference, request.Reason, cancellationToken);
        switch (result.Outcome)
        {
            case RequestCancellationOutcome.Requested:
                return CreatedAtAction(nameof(GetOrder), new { reference = result.Order!.Reference }, ManagerOrderResponse.From(result.Order));
            case RequestCancellationOutcome.InvalidReason:
                ModelState.AddModelError("reason", ManagerOrderMessages.ReasonInvalid);
                return ValidationProblem(ModelState);
            case RequestCancellationOutcome.AlreadyRequested:
                return Problem(statusCode: StatusCodes.Status409Conflict, title: ManagerOrderMessages.AlreadyRequestedTitle);
            case RequestCancellationOutcome.NotCancellable:
                return Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: ManagerOrderMessages.NotCancellableTitle,
                    detail: ManagerOrderMessages.NotCancellableDetail);
            default:
                return Problem(statusCode: StatusCodes.Status404NotFound, title: OrderMessages.NotFoundTitle);
        }
    }

    /// <summary>Refunds that failed at Paystack or need the patron's bank details, newest first (Q4).</summary>
    /// <response code="200">Up to 100 refunds needing manual handling.</response>
    [HttpGet("refunds/attention")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<IReadOnlyList<RefundNeedingAttentionResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RefundNeedingAttentionResponse>>> RefundsNeedingAttention(CancellationToken cancellationToken) =>
        Ok((await orders.RefundsNeedingAttentionAsync(cancellationToken)).Select(RefundNeedingAttentionResponse.From).ToList());
}
