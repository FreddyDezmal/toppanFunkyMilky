using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Milkshake.Api.Contracts.Management;
using Milkshake.Api.Security;
using Milkshake.Application.Management;
using Milkshake.Domain.Notifications;

namespace Milkshake.Api.Controllers;

/// <summary>
/// Management → Emails (M9, AC-9.5, D6): delivery status of the transactional emails, and sending a sent or failed one
/// again. Managers see delivery state only, never an email's content.
/// </summary>
[ApiController]
[Route("api/manager/emails")]
[Authorize(Policy = AuthorizationPolicies.ManagerOnly)]
public sealed class ManagerEmailsController(IManagerEmailService emails) : ControllerBase
{
    private const int ListLimit = 100;
    private const int ReferenceMaxLength = 32;

    /// <summary>Up to 100 emails, newest first, optionally of one status and/or one order, with counts per status.</summary>
    /// <response code="200">The emails.</response>
    /// <response code="400">Unknown status or invalid order reference.</response>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<EmailDeliveryListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EmailDeliveryListResponse>> List(
        [FromQuery] string? status, [FromQuery] string? order, CancellationToken cancellationToken)
    {
        NotificationStatus? wanted = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse(status.Trim(), ignoreCase: true, out NotificationStatus parsed) ||
                !Enum.IsDefined(parsed) || status.Trim().All(char.IsAsciiDigit))
            {
                ModelState.AddModelError(nameof(status), ManagerEmailMessages.StatusInvalid);
                return ValidationProblem(ModelState);
            }

            wanted = parsed;
        }

        if (order is { Length: > ReferenceMaxLength })
        {
            ModelState.AddModelError(nameof(order), ManagerEmailMessages.OrderReferenceInvalid);
            return ValidationProblem(ModelState);
        }

        return Ok(EmailDeliveryListResponse.From(await emails.ListAsync(wanted, order, ListLimit, cancellationToken)));
    }

    /// <summary>
    /// Queues a sent or failed email again: same recipient and content, a fresh set of attempts and a new provider
    /// idempotency key. The dispatcher sends it within a minute.
    /// </summary>
    /// <response code="200">Queued; the body is the email's new state.</response>
    /// <response code="404">No email with this id.</response>
    /// <response code="409">The email is waiting, being sent or suppressed, or changed meanwhile.</response>
    /// <response code="429">Too many resends; try again shortly.</response>
    [HttpPost("{id:int}/resend")]
    [EnableRateLimiting(RateLimitPolicies.EmailResend)]
    [RequestBodyLimit(1024)]
    [ProducesResponseType<EmailDeliveryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EmailDeliveryResponse>> Resend(int id, CancellationToken cancellationToken)
    {
        ResendEmailResult result = await emails.ResendAsync(id, cancellationToken);
        return result.Outcome switch
        {
            ResendEmailOutcome.Resent => Ok(EmailDeliveryResponse.From(result.Email!)),
            ResendEmailOutcome.NotResendable => Problem(statusCode: StatusCodes.Status409Conflict,
                title: ManagerEmailMessages.NotResendableTitle, detail: ManagerEmailMessages.NotResendableDetail),
            ResendEmailOutcome.Changed => Problem(statusCode: StatusCodes.Status409Conflict, title: ManagerEmailMessages.ChangedTitle),
            _ => Problem(statusCode: StatusCodes.Status404NotFound, title: ManagerEmailMessages.NotFoundTitle),
        };
    }
}
