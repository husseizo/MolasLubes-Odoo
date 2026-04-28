using Microsoft.AspNetCore.Mvc;
using MolasLubes.Api.Models.Auth;
using MolasLubes.Infrastructure.Security;

namespace MolasLubes.Api.Controllers;

/// <summary>
/// Authentication endpoints for JWT token management.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly SapUserAuthService _sapAuth;
    private readonly JwtService _jwtService;
    private readonly RefreshTokenStore _tokenStore;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        SapUserAuthService sapAuth,
        JwtService jwtService,
        RefreshTokenStore tokenStore,
        ILogger<AuthController> logger)
    {
        _sapAuth = sapAuth;
        _jwtService = jwtService;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    /// <summary>
    /// Authenticates a SAP user and issues JWT tokens.
    /// </summary>
    /// <remarks>
    /// POST /api/auth/login
    /// {
    ///   "sapUserCode": "manager",
    ///   "password": "your-password"
    /// }
    /// 
    /// Returns:
    /// {
    ///   "token": "eyJhbGc...",
    ///   "refreshToken": "xyz...",
    ///   "sapUserCode": "manager",
    ///   "role": "Supervisor",
    ///   "expiresAt": "2025-01-03T15:30:00Z"
    /// }
    /// </remarks>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(500)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            // Validate credentials against SAP B1
            var (isValid, role) = await _sapAuth.ValidateCredentialsAsync(
                request.SapUserCode,
                request.Password);

            if (!isValid || role == null)
            {
                _logger.LogWarning(
                    "Login attempt failed for user {UserCode}",
                    request.SapUserCode);
                return Unauthorized(new { message = "Invalid credentials" });
            }

            // Generate tokens
            var accessToken = _jwtService.GenerateAccessToken(request.SapUserCode, role);
            var (refreshToken, refreshExpiresAt) = _jwtService.GenerateRefreshToken();

            // Store refresh token
            _tokenStore.Store(new RefreshTokenSession
            {
                Token = refreshToken,
                SapUserCode = request.SapUserCode,
                Role = role,
                ExpiresAt = refreshExpiresAt
            });

            _logger.LogInformation(
                "User {UserCode} logged in successfully with role {Role}",
                request.SapUserCode,
                role);

            return Ok(new LoginResponse
            {
                Token = accessToken,
                RefreshToken = refreshToken,
                SapUserCode = request.SapUserCode,
                Role = role,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15) // Match JWT expiry
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for user {UserCode}", request.SapUserCode);
            return StatusCode(500, new { message = "Internal server error during authentication" });
        }
    }

    /// <summary>
    /// Refreshes an expired access token using a valid refresh token.
    /// </summary>
    /// <remarks>
    /// POST /api/auth/refresh
    /// {
    ///   "refreshToken": "xyz..."
    /// }
    /// 
    /// Returns:
    /// {
    ///   "token": "eyJhbGc...",
    ///   "refreshToken": "xyz...",
    ///   "sapUserCode": "manager",
    ///   "role": "Supervisor",
    ///   "expiresAt": "2025-01-03T15:45:00Z"
    /// }
    /// </remarks>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(RefreshTokenResponse), 200)]
    [ProducesResponseType(401)]
    public IActionResult Refresh([FromBody] RefreshTokenRequest request)
    {
        try
        {
            // Validate refresh token
            var session = _tokenStore.GetAndValidate(request.RefreshToken);

            if (session == null)
            {
                _logger.LogWarning("Refresh token validation failed or expired");
                return Unauthorized(new { message = "Invalid or expired refresh token" });
            }

            // Generate new access token (keep same refresh token)
            var newAccessToken = _jwtService.GenerateAccessToken(session.SapUserCode, session.Role);

            _logger.LogDebug(
                "Access token refreshed for user {UserCode}",
                session.SapUserCode);

            return Ok(new RefreshTokenResponse
            {
                Token = newAccessToken,
                RefreshToken = request.RefreshToken, // Return same refresh token
                SapUserCode = session.SapUserCode,
                Role = session.Role,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            return StatusCode(500, new { message = "Internal server error during token refresh" });
        }
    }

    /// <summary>
    /// Logs out the user by revoking their refresh token.
    /// </summary>
    /// <remarks>
    /// POST /api/auth/logout
    /// {
    ///   "refreshToken": "xyz..."
    /// }
    /// </remarks>
    [HttpPost("logout")]
    [ProducesResponseType(204)]
    public IActionResult Logout([FromBody] RefreshTokenRequest request)
    {
        _tokenStore.Revoke(request.RefreshToken);

        _logger.LogInformation("User logged out, refresh token revoked");

        return NoContent();
    }

    /// <summary>
    /// Validates the current JWT token (health check endpoint).
    /// Requires valid Bearer token in Authorization header.
    /// </summary>
    [HttpGet("validate")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public IActionResult Validate()
    {
        // If this endpoint is reached, token is valid (middleware validates it)
        var userCode = User.Identity?.Name;
        var role = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value;

        return Ok(new
        {
            isValid = true,
            sapUserCode = userCode,
            role = role
        });
    }
}
