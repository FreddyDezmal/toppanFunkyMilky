using System.Security.Claims;
using Milkshake.Application.Abstractions;

namespace Milkshake.Api.Services;

/// <summary>Reads the current user from the authenticated HTTP request (populated once Identity login exists).</summary>
internal sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id) ? id : null;

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;
}