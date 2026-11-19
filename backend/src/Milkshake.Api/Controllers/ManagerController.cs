using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Milkshake.Api.Security;

namespace Milkshake.Api.Controllers;

/// <summary>
/// Manager-only endpoints. For now a single probe that proves role enforcement;
/// lookup management and reports are added in later milestones.
/// </summary>
[ApiController]
[Route("api/manager")]
[Authorize(Policy = AuthorizationPolicies.ManagerOnly)]
public sealed class ManagerController : ControllerBase
{
    /// <summary>Confirms the caller is signed in as a Manager.</summary>
    /// <response code="200">The caller is a Manager.</response>
    /// <response code="401">Not signed in.</response>
    /// <response code="403">Signed in, but not a Manager.</response>
    [HttpGet("ping")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult Ping() => Ok(new { message = "Manager access confirmed." });
}