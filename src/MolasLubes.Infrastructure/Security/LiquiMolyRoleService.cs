using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    /// that they hold the required role in the config allowlist.
    /// Admins always pass the role check regardless of which role is required.
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

    // ── Private ──────────────────────────────────────────

    private bool HasRole(string userCode, string role)
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
