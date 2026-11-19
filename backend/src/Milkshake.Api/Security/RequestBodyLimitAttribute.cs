using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Milkshake.Api.Security;

/// <summary>
/// Caps one action's request body (M5 finding F2). It sets the server's own limit, so Kestrel stops reading a larger
/// body (chunked ones too), and refuses a declared Content-Length above the cap before anything is read, so the cap
/// also holds where the server has no such limit (the in-memory test host).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequestBodyLimitAttribute(long maxBytes) : Attribute, IResourceFilter
{
    /// <summary>Largest accepted body, in bytes.</summary>
    public long MaxBytes { get; } = maxBytes;

    /// <inheritdoc />
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        HttpContext http = context.HttpContext;

        IHttpMaxRequestBodySizeFeature? serverLimit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (serverLimit is { IsReadOnly: false })
        {
            serverLimit.MaxRequestBodySize = MaxBytes;
        }

        if (http.Request.ContentLength > MaxBytes)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "The request is too large.",
                Detail = "Please try again with fewer items.",
            })
            {
                StatusCode = StatusCodes.Status413PayloadTooLarge,
            };
        }
    }

    /// <inheritdoc />
    public void OnResourceExecuted(ResourceExecutedContext context)
    {
        // Nothing to do after the action.
    }
}