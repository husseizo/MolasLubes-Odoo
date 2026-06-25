using Microsoft.AspNetCore.Mvc;
using MolasLubes.Api.Models.Auth;
using MolasLubes.Api.Security;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Security;

namespace MolasLubes.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly InternalUserService _users;
    private readonly InternalTokenService _tokens;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        InternalUserService users,
        InternalTokenService tokens,
        ILogger<AuthController> logger)
    {
        _users  = users;
        _tokens = tokens;
        _logger = logger;
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(423)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var ipHint = HttpContext.Connection.RemoteIpAddress?.ToString();

        var (user, isLocked, lockedUntil) = await _users.ValidateLoginAsync(request.Username, request.Password);

        if (isLocked)
        {
            await _users.AddAuditEventAsync("ACCOUNT_LOCKED", detail: $"Login blocked, locked until {lockedUntil:O}", ipHint: ipHint);
            return StatusCode(423, new { message = $"Account locked until {lockedUntil:O}" });
        }

        if (user == null)
        {
            await _users.AddAuditEventAsync("LOGIN_FAIL", detail: $"username={request.Username}", ipHint: ipHint);
            return Unauthorized(new { message = "Invalid credentials." });
        }

        var rawToken = await _tokens.IssueTokenAsync(user.Id, expiryDays: 30, deviceHint: request.DeviceHint);
        var expiresAt = DateTime.UtcNow.AddDays(30);

        string? sapWarning = null;
        try
        {
            await _users.AddAuditEventAsync("LOGIN_OK", userId: user.Id, ipHint: ipHint);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit write failed for LOGIN_OK user={UserId}", user.Id);
        }

        _logger.LogInformation("User {Username} (id={Id}) logged in", user.Username, user.Id);

        return Ok(new LoginResponse
        {
            Token       = rawToken,
            ExpiresAt   = expiresAt,
            UserId      = user.Id,
            Username    = user.Username,
            DisplayName = user.DisplayName,
            Role        = user.Role,
            SapWarning  = sapWarning
        });
    }

    [HttpPost("logout")]
    [BearerToken]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Logout()
    {
        var user = HttpContext.Items["CurrentUser"] as InternalUser;
        var rawToken = HttpContext.Items["CurrentToken"] as string;

        if (rawToken != null)
            await _tokens.RevokeTokenAsync(rawToken);

        if (user != null)
            await _users.AddAuditEventAsync("LOGOUT", userId: user.Id,
                ipHint: HttpContext.Connection.RemoteIpAddress?.ToString());

        return NoContent();
    }

    [HttpGet("me")]
    [BearerToken]
    [ProducesResponseType(typeof(MeResponse), 200)]
    public IActionResult Me()
    {
        var user = (InternalUser)HttpContext.Items["CurrentUser"]!;

        return Ok(new MeResponse
        {
            UserId      = user.Id,
            Username    = user.Username,
            DisplayName = user.DisplayName,
            Role        = user.Role,
            SapUserCode = user.SapUserCode,
            LastLoginAt = user.LastLoginAt
        });
    }

    [HttpPost("change-password")]
    [BearerToken]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var user = (InternalUser)HttpContext.Items["CurrentUser"]!;

        var error = await _users.ChangePasswordAsync(user.Id, request.CurrentPassword, request.NewPassword);
        if (error != null)
            return BadRequest(new { message = error });

        await _tokens.RevokeAllForUserAsync(user.Id);

        await _users.AddAuditEventAsync("PASSWORD_CHANGE", userId: user.Id,
            ipHint: HttpContext.Connection.RemoteIpAddress?.ToString());

        return NoContent();
    }
}
