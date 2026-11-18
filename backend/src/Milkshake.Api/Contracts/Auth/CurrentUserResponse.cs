namespace Milkshake.Api.Contracts.Auth;

/// <summary>The signed-in user. Never includes password hashes, security stamps or tokens.</summary>
public sealed record CurrentUserResponse(
    Guid Id,
    string FirstName,
    string Email,
    string? MobileNumber,
    IReadOnlyList<string> Roles);