using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using MolasLubes.Api.Security;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Security;

namespace MolasLubes.Api.Controllers;

[ApiController]
[Route("api/admin/users")]
[BearerToken]
public class AdminUsersController : ControllerBase
{
    private readonly InternalUserService _users;
    private readonly InternalTokenService _tokens;
    private readonly LiquiMolyRoleService _roles;
    private readonly ILogger<AdminUsersController> _logger;

    public AdminUsersController(
        InternalUserService users,
        InternalTokenService tokens,
        LiquiMolyRoleService roles,
        ILogger<AdminUsersController> logger)
    {
        _users  = users;
        _tokens = tokens;
        _roles  = roles;
        _logger = logger;
    }

    // ── GET /api/admin/users ──────────────────────────────────────────────

    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? role,
        [FromQuery] bool? isActive,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        RequireAdmin();

        var (items, total) = await _users.GetListAsync(search, role, isActive, skip, take);

        return Ok(new
        {
            total,
            items = items.Select(u => MapSummary(u))
        });
    }

    // ── POST /api/admin/users ─────────────────────────────────────────────

    [HttpPost]
    [ProducesResponseType(201)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        RequireAdmin();

        var actor = CurrentUser();
        var (user, error) = await _users.CreateAsync(
            request.Username, request.Password, request.DisplayName,
            request.Role, request.SapUserCode);

        if (error != null)
            return BadRequest(new { message = error });

        await _users.AddAuditEventAsync("USER_CREATED", userId: user!.Id, actorId: actor.Id,
            detail: $"created by {actor.Username}");

        _logger.LogInformation("Admin {Actor} created user {Username}", actor.Username, user.Username);

        return CreatedAtAction(nameof(Get), new { id = user.Id }, MapDetail(user));
    }

    // ── GET /api/admin/users/{id} ─────────────────────────────────────────

    [HttpGet("{id:int}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Get(int id)
    {
        RequireAdmin();

        var user = await _users.GetByIdAsync(id);
        if (user == null)
            return NotFound();

        return Ok(MapDetail(user));
    }

    // ── PUT /api/admin/users/{id} ─────────────────────────────────────────

    [HttpPut("{id:int}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest request)
    {
        RequireAdmin();

        var actor = CurrentUser();
        var (user, error) = await _users.UpdateAsync(id, request.DisplayName, request.Role, request.SapUserCode, request.IsActive);

        if (user == null)
            return NotFound();

        if (error != null)
            return BadRequest(new { message = error });

        if (request.Role != null)
            await _users.AddAuditEventAsync("ROLE_CHANGE", userId: id, actorId: actor.Id,
                detail: $"role → {request.Role}");

        return Ok(MapDetail(user!));
    }

    // ── DELETE /api/admin/users/{id} — soft-delete ────────────────────────

    [HttpDelete("{id:int}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Deactivate(int id)
    {
        RequireAdmin();

        var actor = CurrentUser();
        var user = await _users.GetByIdAsync(id);
        if (user == null)
            return NotFound();

        await _users.DeactivateAsync(id);
        await _tokens.RevokeAllForUserAsync(id);
        await _users.AddAuditEventAsync("USER_DEACTIVATED", userId: id, actorId: actor.Id);

        _logger.LogInformation("Admin {Actor} deactivated user {Id}", actor.Username, id);

        return NoContent();
    }

    // ── POST /api/admin/users/{id}/reset-password ─────────────────────────

    [HttpPost("{id:int}/reset-password")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetPasswordRequest? request)
    {
        RequireAdmin();

        var actor = CurrentUser();
        var (generatedPassword, error) = await _users.ResetPasswordAsync(id, request?.NewPassword);

        if (error != null)
        {
            if (error.Contains("not found", StringComparison.OrdinalIgnoreCase))
                return NotFound();
            return BadRequest(new { message = error });
        }

        await _tokens.RevokeAllForUserAsync(id);
        await _users.AddAuditEventAsync("PASSWORD_CHANGE", userId: id, actorId: actor.Id,
            detail: "admin reset");

        return Ok(new
        {
            generatedPassword,
            message = generatedPassword != null
                ? "Password was generated. Share it securely — it will not be shown again."
                : "Password has been reset."
        });
    }

    // ── POST /api/admin/users/{id}/unlock ────────────────────────────────

    [HttpPost("{id:int}/unlock")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Unlock(int id)
    {
        RequireAdmin();

        var actor = CurrentUser();
        var user = await _users.GetByIdAsync(id);
        if (user == null)
            return NotFound();

        await _users.UnlockAsync(user.Username);
        await _users.AddAuditEventAsync("ACCOUNT_LOCKED", userId: id, actorId: actor.Id,
            detail: "manually unlocked by admin");

        return NoContent();
    }

    // ── POST /api/admin/users/{id}/revoke-sessions ────────────────────────

    [HttpPost("{id:int}/revoke-sessions")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> RevokeSessions(int id)
    {
        RequireAdmin();

        var actor = CurrentUser();
        var user = await _users.GetByIdAsync(id);
        if (user == null)
            return NotFound();

        var count = await _tokens.RevokeAllForUserAsync(id);
        await _users.AddAuditEventAsync("SESSION_REVOKED", userId: id, actorId: actor.Id,
            detail: $"{count} sessions revoked");

        return Ok(new { revokedCount = count });
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private InternalUser CurrentUser() => (InternalUser)HttpContext.Items["CurrentUser"]!;

    private void RequireAdmin() =>
        _roles.AuthorizeUser(CurrentUser(), LiquiMolyRole.Admin);

    private static object MapSummary(InternalUser u) => new
    {
        u.Id,
        u.Username,
        u.DisplayName,
        u.Role,
        u.IsActive,
        u.LastLoginAt,
        u.CreatedAt
    };

    private static object MapDetail(InternalUser u) => new
    {
        u.Id,
        u.Username,
        u.DisplayName,
        u.Role,
        u.SapUserCode,
        u.IsActive,
        u.LastLoginAt,
        u.FailedLoginCount,
        u.LockedUntil,
        u.CreatedAt,
        u.UpdatedAt
    };
}

// ── Request models ────────────────────────────────────────────────────────

public class CreateUserRequest
{
    [Required] public string Username { get; set; } = string.Empty;
    [Required][MinLength(8)] public string Password { get; set; } = string.Empty;
    [Required] public string DisplayName { get; set; } = string.Empty;
    [Required] public string Role { get; set; } = string.Empty;
    public string? SapUserCode { get; set; }
}

public class UpdateUserRequest
{
    public string? DisplayName { get; set; }
    public string? Role { get; set; }
    public string? SapUserCode { get; set; }
    public bool? IsActive { get; set; }
}

public class ResetPasswordRequest
{
    [MinLength(8)] public string? NewPassword { get; set; }
}
