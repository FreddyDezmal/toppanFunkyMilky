using Microsoft.AspNetCore.Mvc;
using Milkshake.Application.Management;

namespace Milkshake.Api.Controllers;

/// <summary>Turns failed management results into RFC 9457 ProblemDetails responses.</summary>
internal static class ManagementResultExtensions
{
    /// <summary>400 ValidationProblemDetails, 404 or 409 ProblemDetails.</summary>
    public static ActionResult ToFailureResponse<T>(this ControllerBase controller, ManagementResult<T> result)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(result);

        switch (result.Outcome)
        {
            case ManagementOutcome.NotFound:
                return controller.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: ManagementMessages.NotFoundTitle,
                    detail: ManagementMessages.NotFoundDetail);

            case ManagementOutcome.Conflict:
                return controller.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: ManagementMessages.ConflictTitle,
                    detail: ManagementMessages.ConflictDetail);

            case ManagementOutcome.Invalid:
                foreach ((string field, string[] messages) in result.Errors)
                {
                    foreach (string message in messages)
                    {
                        controller.ModelState.AddModelError(field, message);
                    }
                }

                return controller.ValidationProblem(controller.ModelState);

            default:
                throw new InvalidOperationException("Only failed results can be converted to a problem response.");
        }
    }

    /// <summary>400 for an unknown ?status= value.</summary>
    public static ActionResult InvalidStatusFilter(this ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        controller.ModelState.AddModelError("status", "Status must be all, active or inactive.");
        return controller.ValidationProblem(controller.ModelState);
    }
}