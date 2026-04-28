using System.ComponentModel.DataAnnotations;

namespace MolasLubes.Api.Models.Auth;

/// <summary>
/// Login request payload.
/// </summary>
public class LoginRequest
{
    [Required]
    public string SapUserCode { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Successful login response with tokens and user context.
/// </summary>
public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string SapUserCode { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Refresh token request.
/// </summary>
public class RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>
/// Refresh token response with new access token and full session context.
/// </summary>
public class RefreshTokenResponse
{
    public string Token { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string SapUserCode { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
