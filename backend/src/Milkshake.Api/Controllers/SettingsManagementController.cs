using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Milkshake.Api.Contracts.Management;
using Milkshake.Api.Security;
using Milkshake.Application.Management;

namespace Milkshake.Api.Controllers;

/// <summary>
/// Manager maintenance of system settings (URS §3.4.6). One setting per request; changes are validated against
/// the M10 limits, version-checked (stale = 409) and audited. Changes apply to orders placed afterwards.
/// </summary>
[ApiController]
[Route("api/manager/settings")]
[Authorize(Policy = AuthorizationPolicies.ManagerOnly)]
public sealed class SettingsManagementController(ISettingsManagementService settings) : ControllerBase
{
    /// <summary>All settings in screen order.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SettingResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SettingResponse>>> List(CancellationToken cancellationToken)
    {
        IReadOnlyList<SettingSummary> found = await settings.ListAsync(cancellationToken);
        return Ok(found.Select(SettingResponse.From).ToList());
    }

    /// <summary>Changes one setting and returns all settings (versions may change).</summary>
    /// <response code="400">Value outside the allowed limits, or the setting is read-only.</response>
    /// <response code="404">Unknown setting key.</response>
    /// <response code="409">Someone else changed this setting (or its minimum/maximum partner) since it was loaded.</response>
    [HttpPut("{key}")]
    [ProducesResponseType<IReadOnlyList<SettingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IReadOnlyList<SettingResponse>>> Update(
        string key,
        UpdateSettingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ManagementResult<IReadOnlyList<SettingSummary>> result = await settings.UpdateAsync(
            new UpdateSettingCommand(key, request.Value, request.Version.GetValueOrDefault()),
            cancellationToken);

        return result.Outcome == ManagementOutcome.Success
            ? Ok(result.Value!.Select(SettingResponse.From).ToList())
            : this.ToFailureResponse(result);
    }
}