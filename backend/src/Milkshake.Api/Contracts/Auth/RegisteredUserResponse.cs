namespace Milkshake.Api.Contracts.Auth;

/// <summary>Returned after registration. Contains no security-sensitive fields.</summary>
public sealed record RegisteredUserResponse(Guid Id, string FirstName, string Email);