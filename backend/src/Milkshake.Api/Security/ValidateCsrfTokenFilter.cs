using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Milkshake.Api.Security;

/// <summary>
/// Rejects state-changing requests (POST, PUT, PATCH, DELETE) that lack a valid anti-forgery token.
/// Cookie sessions are sent automatically by the browser, so without this check another website
/// could trigger actions on behalf of a signed-in user (cross-site request forgery).
/// </summary>
internal sealed class ValidateCsrfTokenFilter(IAntiforgery antiforgery, ProblemDetailsFactory problemDetailsFactory)
    : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) ||
            HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
        {
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            ProblemDetails problem = problemDetailsFactory.CreateProblemDetails(
                context.HttpContext,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid or missing anti-forgery token.",
                detail: "Request a token from GET /api/auth/csrf and send it in the X-XSRF-TOKEN header.");

            context.Result = new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest };
        }
    }
}