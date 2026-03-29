using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Security;

namespace MolasLubes.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController : ControllerBase
{
    private readonly AppUserRepository _users;
    private readonly JwtService        _jwt;
    private readonly RefreshTokenStore _store;
    private readonly SapUserReader     _sapUsers;
    private readonly JwtOptions        _opts;

    public AuthController(
        AppUserRepository  users,
        JwtService         jwt,
        RefreshTokenStore  store,
        SapUserReader      sapUsers,
        IOptions<JwtOptions> opts)
    {
        _users    = users;
        _jwt      = jwt;
        _store    = store;
        _sapUsers = sapUsers;
        _opts     = opts.Value;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.SapUserCode) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest("sapUserCode and password are required.");

        var user = await _users.FindAsync(req.SapUserCode);
        if (user is null || !user.IsActive)
            return Unauthorized("Invalid credentials.");

        if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized("Invalid credentials.");

        // Belt-and-suspenders: confirm the SAP account is still active
        var sapActive = _sapUsers.ValidateUserAcrossProfiles(req.SapUserCode);
        if (!sapActive)
            return Unauthorized("SAP account is inactive or not found.");

        await _users.UpdateLastLoginAsync(req.SapUserCode);

        var accessToken  = _jwt.IssueAccessToken(req.SapUserCode);
        var refreshToken = _jwt.IssueRefreshToken();
        _store.Save(refreshToken, req.SapUserCode, _opts.RefreshExpiryDays);

        return Ok(new LoginResponse(accessToken, refreshToken, req.SapUserCode,
                                    _jwt.ResolveRolePublic(req.SapUserCode)));
    }

    [HttpPost("refresh")]
    public IActionResult Refresh([FromBody] RefreshRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken))
            return BadRequest("refreshToken is required.");

        var sapUserCode = _store.Consume(req.RefreshToken);
        if (sapUserCode is null)
            return Unauthorized("Refresh token is invalid or expired.");

        var newAccess  = _jwt.IssueAccessToken(sapUserCode);
        var newRefresh = _jwt.IssueRefreshToken();
        _store.Save(newRefresh, sapUserCode, _opts.RefreshExpiryDays);

        return Ok(new { accessToken = newAccess, refreshToken = newRefresh });
    }
}

public sealed record LoginRequest(string SapUserCode, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LoginResponse(string AccessToken, string RefreshToken,
                                   string SapUserCode, string Role);
