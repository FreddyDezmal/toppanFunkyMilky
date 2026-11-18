using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Milkshake.Api.Contracts.Auth;
using Milkshake.Api.Security;
using Milkshake.Application.Abstractions;
using Milkshake.Application.Accounts;

namespace Milkshake.Api.Controllers;

/// <summary>
/// Registration, login, logout and the current user.
/// Services that need the database are injected per action, so the CSRF endpoint never needs it.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(
    IAntiforgery antiforgery,
    IOptions<AntiforgeryOptions> antiforgeryOptions) : ControllerBase
{
    /// <summary>
    /// Issues an anti-forgery token. Call it on start-up and again after every login, registration or logout,
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

    /// <summary>Registers a new patron account and signs the patron in.</summary>
    /// <response code="201">Account created with the Patron role; session cookie set.</response>
    /// <response code="400">Validation failed, the email is already registered, or the anti-forgery token is missing.</response>
    /// <response code="429">Too many attempts from this address.</response>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<RegisteredUserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
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

    /// <summary>Signs in with email and password.</summary>
    /// <response code="200">Signed in; session cookie set.</response>
    /// <response code="401">Sign-in failed (the reason is deliberately not disclosed).</response>
    /// <response code="429">Too many attempts from this address.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CurrentUserResponse>> Login(
        LoginRequest request,
        [FromServices] IAccountService accountService,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        UserProfile? profile = await accountService.LoginAsync(new LoginCommand(request.Email, request.Password), cancellationToken);
        if (profile is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign-in failed.", detail: LoginMessages.Failed);
        }

        return Ok(ToResponse(profile));
    }

    /// <summary>Signs out and invalidates the user's other sessions.</summary>
    /// <response code="204">Signed out.</response>
    /// <response code="401">Not signed in.</response>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(
        [FromServices] IAccountService accountService,
        [FromServices] ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        if (currentUser.UserId is Guid userId)
        {
            await accountService.LogoutAsync(userId, cancellationToken);
        }

        return NoContent();
    }

    /// <summary>The signed-in user's own profile.</summary>
    /// <response code="200">The current user.</response>
    /// <response code="401">Not signed in.</response>
    [HttpGet("me")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserResponse>> Me(
        [FromServices] IAccountService accountService,
        [FromServices] ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        if (currentUser.UserId is not Guid userId)
        {
            return Unauthorized();
        }

        UserProfile? profile = await accountService.GetProfileAsync(userId, cancellationToken);
        if (profile is null)
        {
            return Unauthorized();
        }

        return Ok(ToResponse(profile));
    }

    private static CurrentUserResponse ToResponse(UserProfile profile) =>
        new(profile.Id, profile.FirstName, profile.Email, profile.MobileNumber, profile.Roles);
}