using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

namespace MolasLubes.Infrastructure.Security;

/// <summary>
/// Validates actor identity (SAP OUSR) and checks workflow role authorization
/// from the appsettings "LiquiMolyPermissions" allowlist.
///
/// Usage pattern:
///   roleService.Authorize(actorSapUser, LiquiMolyRole.Supervisor);
///   // throws UnauthorizedAccessException if invalid or not in role
/// </summary>
public class LiquiMolyRoleService
{
    private readonly LiquiMolyPermissionsOptions _permissions;
    private readonly SapUserReader _userReader;
    private readonly ILogger<LiquiMolyRoleService> _logger;

    public LiquiMolyRoleService(
        IOptions<LiquiMolyPermissionsOptions> permOptions,
        SapUserReader userReader,
        ILogger<LiquiMolyRoleService> logger)
    {
        _permissions = permOptions.Value;
        _userReader  = userReader;
        _logger      = logger;
    }

    // ── Role membership checks (config-only, no SAP call) ────────────

    public bool IsPlanner(string userCode)    => HasRole(userCode, LiquiMolyRole.Planner);
    public bool IsSupervisor(string userCode) => HasRole(userCode, LiquiMolyRole.Supervisor);
    public bool IsExecutor(string userCode)   => HasRole(userCode, LiquiMolyRole.Executor);
    public bool IsAdmin(string userCode)      => HasRole(userCode, LiquiMolyRole.Admin);
    public bool IsViewer(string userCode)     => HasRole(userCode, LiquiMolyRole.Viewer);

    // ── Full authorization: SAP identity check + role check ──────────

    /// <summary>
    /// Validates that the SAP user exists and is active (queries OUSR), then checks
    /// that they hold the required capability in the config allowlist.
    /// Admins always pass the role check regardless of which role is required.
    /// Effective capability matrix:
    ///   Viewer     <= Viewer, Planner, Executor, Supervisor, Admin
    ///   Planner    <= Planner, Executor, Supervisor, Admin
    ///   Executor   <= Executor, Planner, Admin
    ///   Supervisor <= Supervisor, Admin
    ///   Admin      <= Admin
    /// Throws <see cref="UnauthorizedAccessException"/> on failure.
    /// </summary>
    public void Authorize(string sapUserCode, string requiredRole)
    {
        if (string.IsNullOrWhiteSpace(sapUserCode))
            throw new UnauthorizedAccessException("actorSapUserCode is required.");

        // SAP identity validation
        bool sapValid = false;
        try
        {
            sapValid = _userReader.ValidateUserAcrossProfiles(sapUserCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "LiquiMolyRoleService: SAP user validation failed for '{User}' — rejecting", sapUserCode);
            throw new UnauthorizedAccessException(
                $"SAP user validation failed for '{sapUserCode}'. Ensure SAP is reachable.");
        }

        if (!sapValid)
            throw new UnauthorizedAccessException(
                $"SAP user '{sapUserCode}' not found or is locked in any configured company.");

        // Role authorization (Admins bypass all role checks)
        if (IsAdmin(sapUserCode))
        {
            _logger.LogDebug(
                "LiquiMolyRoleService: '{User}' authorized as Admin for role '{Role}'",
                sapUserCode, requiredRole);
            return;
        }

        if (!HasRole(sapUserCode, requiredRole))
            throw new UnauthorizedAccessException(
                $"SAP user '{sapUserCode}' is not authorized for role '{requiredRole}'.");

        _logger.LogDebug(
            "LiquiMolyRoleService: '{User}' authorized for role '{Role}'",
            sapUserCode, requiredRole);
    }

    // ── New-auth overload (InternalUser, no SAP call) ────────────────────

    /// <summary>
    /// Checks that the resolved InternalUser holds the required capability.
    /// No SAP call — role is read from the user's DB record.
    /// </summary>
    public void AuthorizeUser(InternalUser user, string requiredRole)
    {
        if (!user.IsActive)
            throw new UnauthorizedAccessException("Account is not active.");

        if (!HasRoleForUser(user.Role, requiredRole))
            throw new UnauthorizedAccessException(
                $"Role '{user.Role}' is not authorized for '{requiredRole}'.");
    }

    public bool UserHasRole(InternalUser user, string requiredRole)
        => user.IsActive && HasRoleForUser(user.Role, requiredRole);

    /// <summary>
    /// Dual-path authorization. When bearerUser is non-null (request authenticated via
    /// bearer token) the InternalUser's current DB role is used. Otherwise falls back to
    /// the SAP user + appsettings allowlist path. Call sites pass
    /// HttpContext.Items["CurrentUser"] as InternalUser as the first argument.
    /// </summary>
    public void AuthorizeAny(InternalUser? bearerUser, string sapUserCode, string requiredRole)
    {
        if (bearerUser != null)
            AuthorizeUser(bearerUser, requiredRole);
        else
            Authorize(sapUserCode, requiredRole);
    }

    // ── Private ──────────────────────────────────────────

    private static bool HasRoleForUser(string userRole, string requiredRole)
    {
        if (userRole == LiquiMolyRole.Admin) return true;

        return requiredRole switch
        {
            LiquiMolyRole.Viewer =>
                userRole is LiquiMolyRole.Viewer
                         or LiquiMolyRole.Planner
                         or LiquiMolyRole.Executor
                         or LiquiMolyRole.Supervisor
                         or LiquiMolyRole.Inventory,

            LiquiMolyRole.Planner =>
                userRole is LiquiMolyRole.Planner
                         or LiquiMolyRole.Executor
                         or LiquiMolyRole.Supervisor,

            LiquiMolyRole.Executor =>
                userRole is LiquiMolyRole.Executor
                         or LiquiMolyRole.Planner,

            LiquiMolyRole.Supervisor =>
                userRole == LiquiMolyRole.Supervisor,

            LiquiMolyRole.Inventory =>
                userRole == LiquiMolyRole.Inventory,

            LiquiMolyRole.Admin =>
                userRole == LiquiMolyRole.Admin,

            _ => false
        };
    }

    private bool HasRole(string userCode, string role)
    {
        if (IsInConfiguredRole(userCode, LiquiMolyRole.Admin))
            return true;

        return role switch
        {
            LiquiMolyRole.Viewer =>
                IsInConfiguredRole(userCode, LiquiMolyRole.Viewer) ||
                IsInConfiguredRole(userCode, LiquiMolyRole.Planner) ||
                IsInConfiguredRole(userCode, LiquiMolyRole.Executor) ||
                IsInConfiguredRole(userCode, LiquiMolyRole.Supervisor),

            LiquiMolyRole.Planner =>
                IsInConfiguredRole(userCode, LiquiMolyRole.Planner) ||
                IsInConfiguredRole(userCode, LiquiMolyRole.Executor) ||
                IsInConfiguredRole(userCode, LiquiMolyRole.Supervisor),

            LiquiMolyRole.Executor =>
                IsInConfiguredRole(userCode, LiquiMolyRole.Executor) ||
                IsInConfiguredRole(userCode, LiquiMolyRole.Planner),

            LiquiMolyRole.Supervisor =>
                IsInConfiguredRole(userCode, LiquiMolyRole.Supervisor),

            LiquiMolyRole.Admin =>
                IsInConfiguredRole(userCode, LiquiMolyRole.Admin),

            _ => false
        };
    }

    private bool IsInConfiguredRole(string userCode, string role)
    {
        var list = role switch
        {
            LiquiMolyRole.Planner    => _permissions.Planners,
            LiquiMolyRole.Supervisor => _permissions.Supervisors,
            LiquiMolyRole.Executor   => _permissions.Executors,
            LiquiMolyRole.Admin      => _permissions.Admins,
            LiquiMolyRole.Viewer     => _permissions.Viewers,
            _                        => new List<string>()
        };

        return list.Contains(userCode, StringComparer.OrdinalIgnoreCase);
    }
}
