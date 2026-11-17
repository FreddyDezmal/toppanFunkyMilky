using System.ComponentModel.DataAnnotations;

namespace Milkshake.Api.Contracts.Auth;

/// <summary>
/// Patron registration (URS §3.1: first name, mobile number, email). There is deliberately no role field:
/// the server always assigns the Patron role.
/// </summary>
public sealed class RegisterRequest
{
    /// <summary>First name.</summary>
    [Required(ErrorMessage = "First name is required.")]
    [StringLength(100, ErrorMessage = "First name cannot exceed 100 characters.")]
    public string FirstName { get; init; } = string.Empty;

    /// <summary>Email address; also used to sign in.</summary>
    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256, ErrorMessage = "Email address cannot exceed 256 characters.")]
    public string Email { get; init; } = string.Empty;

    /// <summary>Mobile number in international format, e.g. +27 82 123 4567.</summary>
    [Required(ErrorMessage = "Mobile number is required.")]
    [StringLength(25, ErrorMessage = "Mobile number is too long.")]
    public string MobileNumber { get; init; } = string.Empty;

    /// <summary>At least 8 characters with upper case, lower case and a digit.</summary>
    [Required(ErrorMessage = "Password is required.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be between 8 and 128 characters.")]
    public string Password { get; init; } = string.Empty;

    /// <summary>Must match <see cref="Password"/>.</summary>
    [Required(ErrorMessage = "Please confirm your password.")]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; init; } = string.Empty;
}