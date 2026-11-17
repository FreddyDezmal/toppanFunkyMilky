using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Milkshake.Api.Contracts.Auth;

namespace Milkshake.Api.Controllers;

/// <summary>
/// Registration, login, logout and the current user.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(IAntiforgery antiforgery, IOptions<AntiforgeryOptions> antiforgeryOptions) : ControllerBase
{
    /// <summary>
    /// Issues an anti-forgery token. Call it on start-up and again after every login or logout,
    /// because a token is bound to the user it was issued for.
    /// </summary>
    [HttpGet("csrf")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<CsrfTokenResponse>(StatusCodes.Status200OK)]
    public ActionResult<CsrfTokenResponse> GetCsrfToken()
    {
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(HttpContext);

        return Ok(new CsrfTokenResponse(
            tokens.RequestToken ?? string.Empty,
            antiforgeryOptions.Value.HeaderName ?? string.Empty));
    }
}