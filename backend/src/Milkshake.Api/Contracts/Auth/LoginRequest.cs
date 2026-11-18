using System.ComponentModel.DataAnnotations;

namespace Milkshake.Api.Contracts.Auth;

/// <summary>Email and password sign-in.</summary>
public sealed class LoginRequest
{
    /// <summary>Email address used at registration.</summary>
    [Required(ErrorMessage = "Email address is required.")]
    [StringLength(256, ErrorMessage = "Email address cannot exceed 256 characters.")]
    public string Email { get; init; } = string.Empty;

    /// <summary>Account password.</summary>
    [Required(ErrorMessage = "Password is required.")]
    [StringLength(128, ErrorMessage = "Password cannot exceed 128 characters.")]
    public string Password { get; init; } = string.Empty;
}