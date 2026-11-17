using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Milkshake.Api.Contracts.Auth;
using Milkshake.Application.Accounts;

namespace Milkshake.Api.Controllers;

/// <summary>
/// Registration, login, logout and the current user.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(
    IAntiforgery antiforgery,
    IOptions<AntiforgeryOptions> antiforgeryOptions) : ControllerBase
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

    /// <summary>Registers a new patron account.</summary>
    /// <response code="201">Account created with the Patron role.</response>
    /// <response code="400">Validation failed, the email is already registered, or the anti-forgery token is missing.</response>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<RegisteredUserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RegisteredUserResponse>> Register(
        RegisterRequest request,
        [FromServices] IAccountService accountService,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        RegistrationResult result = await accountService.RegisterPatronAsync(
            new RegisterPatronCommand(request.FirstName, request.Email, request.MobileNumber, request.Password),
            cancellationToken);

        if (!result.Succeeded)
        {
            foreach ((string field, string[] messages) in result.Errors)
            {
                foreach (string message in messages)
                {
                    ModelState.AddModelError(field, message);
                }
            }

            return ValidationProblem(ModelState);
        }

        RegisteredUser user = result.User!;
        return StatusCode(StatusCodes.Status201Created, new RegisteredUserResponse(user.Id, user.FirstName, user.Email));
    }
}