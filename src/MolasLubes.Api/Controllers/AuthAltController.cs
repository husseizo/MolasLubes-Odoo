using Microsoft.AspNetCore.Mvc;
using MolasLubes.Api.Models.Auth;
using MolasLubes.Infrastructure.Security;

namespace MolasLubes.Api.Controllers;

/// <summary>
/// Alternative authentication routes without /api prefix for frontend compatibility.
/// Duplicates AuthController routes at /auth/* instead of /api/auth/*.
/// </summary>
[ApiController]
[Route("auth")]
public class AuthAltController : ControllerBase
{
    private readonly SapUserAuthService _sapAuth;
    private readonly JwtService _jwtService;
    private readonly RefreshTokenStore _tokenStore;
    private readonly ILogger<AuthAltController> _logger;

    public AuthAltController(
        SapUserAuthService sapAuth,
        JwtService jwtService,
        RefreshTokenStore tokenStore,
        ILogger<AuthAltController> logger)
    {
        _sapAuth = sapAuth;
        _jwtService = jwtService;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    /// <summary>
    /// Authenticates a SAP user and issues JWT tokens.
    /// Alternative route: POST /auth/login
    /// </summary>
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
                    "Login attempt failed for user {UserCode} (alt route)",
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
                "User {UserCode} logged in successfully with role {Role} (alt route)",
                request.SapUserCode,
                role);

            return Ok(new LoginResponse
            {
                Token = accessToken,
                RefreshToken = refreshToken,
                SapUserCode = request.SapUserCode,
                Role = role,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for user {UserCode} (alt route)", request.SapUserCode);
            return StatusCode(500, new { message = "Internal server error during authentication" });
        }
    }

    /// <summary>
    /// Refreshes an expired access token using a valid refresh token.
    /// Alternative route: POST /auth/refresh
    /// </summary>
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
                _logger.LogWarning("Refresh token validation failed or expired (alt route)");
                return Unauthorized(new { message = "Invalid or expired refresh token" });
            }

            // Generate new access token (keep same refresh token)
            var newAccessToken = _jwtService.GenerateAccessToken(session.SapUserCode, session.Role);

            _logger.LogDebug(
                "Access token refreshed for user {UserCode} (alt route)",
                session.SapUserCode);

            return Ok(new RefreshTokenResponse
            {
                Token = newAccessToken,
                RefreshToken = request.RefreshToken,
                SapUserCode = session.SapUserCode,
                Role = session.Role,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh (alt route)");
            return StatusCode(500, new { message = "Internal server error during token refresh" });
        }
    }

    /// <summary>
    /// Logs out the user by revoking their refresh token.
    /// Alternative route: POST /auth/logout
    /// </summary>
    [HttpPost("logout")]
    [ProducesResponseType(204)]
    public IActionResult Logout([FromBody] RefreshTokenRequest request)
    {
        _tokenStore.Revoke(request.RefreshToken);

        _logger.LogInformation("User logged out, refresh token revoked (alt route)");

        return NoContent();
    }

    /// <summary>
    /// Validates the current JWT token.
    /// Alternative route: GET /auth/validate
    /// </summary>
    [HttpGet("validate")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public IActionResult Validate()
    {
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
