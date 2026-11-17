namespace Milkshake.Api.Contracts.Auth;

/// <summary>Anti-forgery token for state-changing requests.</summary>
/// <param name="Token">Value to send back in the request header.</param>
/// <param name="HeaderName">Name of the header that must carry the token.</param>
public sealed record CsrfTokenResponse(string Token, string HeaderName);