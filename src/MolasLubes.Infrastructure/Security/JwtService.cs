using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace MolasLubes.Infrastructure.Security;

/// <summary>
/// JWT token generation and validation service.
/// Generates access tokens (short-lived) and refresh tokens (long-lived).
/// </summary>
public class JwtService
{
    private readonly IConfiguration _config;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public JwtService(IConfiguration config)
    {
        _config = config;
    }

    /// <summary>
    /// Generates a JWT access token for the authenticated user.
    /// Default expiration: 15 minutes (configurable via appsettings).
    /// </summary>
    public string GenerateAccessToken(string sapUserCode, string role)
    {
        var secret = GetJwtSecret();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expiryMinutes = _config.GetValue<int?>("Jwt:AccessTokenExpiryMinutes") ?? 15;

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, sapUserCode),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Sub, sapUserCode),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "MolasLubes.Api",
            audience: _config["Jwt:Audience"] ?? "MolasLubes.Clients",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials
        );

        return _tokenHandler.WriteToken(token);
    }

    /// <summary>
    /// Generates a cryptographically secure refresh token.
    /// Default expiration: 7 days (configurable via appsettings).
    /// </summary>
    public (string Token, DateTime ExpiresAt) GenerateRefreshToken()
    {
        var expiryDays = _config.GetValue<int?>("Jwt:RefreshTokenExpiryDays") ?? 7;
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);

        return (
            Token: Convert.ToBase64String(randomBytes),
            ExpiresAt: DateTime.UtcNow.AddDays(expiryDays)
        );
    }

    /// <summary>
    /// Validates a JWT access token and extracts the principal.
    /// Returns null if token is invalid or expired.
    /// </summary>
    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var secret = GetJwtSecret();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = _config["Jwt:Issuer"] ?? "MolasLubes.Api",
                ValidateAudience = true,
                ValidAudience = _config["Jwt:Audience"] ?? "MolasLubes.Clients",
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };

            var principal = _tokenHandler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the SAP user code from a JWT token without full validation.
    /// Used for refresh token scenarios where we need the user identity.
    /// </summary>
    public string? ExtractSapUserCode(string token)
    {
        try
        {
            var jwtToken = _tokenHandler.ReadJwtToken(token);
            return jwtToken.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
        }
        catch
        {
            return null;
        }
    }

    private string GetJwtSecret()
    {
        var secret = _config["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException(
                "JWT secret is not configured. Set Jwt:Secret in appsettings or user secrets.");

        if (secret.Length < 32)
            throw new InvalidOperationException(
                "JWT secret must be at least 32 characters long for security. Generate with: openssl rand -hex 32");

        return secret;
    }
}
