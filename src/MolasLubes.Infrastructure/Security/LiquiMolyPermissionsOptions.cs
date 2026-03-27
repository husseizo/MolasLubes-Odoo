namespace MolasLubes.Infrastructure.Security;

/// <summary>
/// App-config allowlist that maps SAP user codes to Liqui Moly workflow roles.
/// Loaded from appsettings "LiquiMolyPermissions" section.
///
/// Roles:
///   Planners    — may generate drafts and submit for approval
///   Supervisors — may approve or reject pending requests
///   Executors   — may execute approved requests (system service accounts)
///   Admins      — may perform any action
///   Viewers     — read-only access to reports
/// </summary>
public class LiquiMolyPermissionsOptions
{
    public const string SectionName = "LiquiMolyPermissions";

    public List<string> Planners    { get; set; } = new();
    public List<string> Supervisors { get; set; } = new();
    public List<string> Executors   { get; set; } = new();
    public List<string> Admins      { get; set; } = new();
    public List<string> Viewers     { get; set; } = new();
}

public static class LiquiMolyRole
{
    public const string Planner    = "Planner";
    public const string Supervisor = "Supervisor";
    public const string Executor   = "Executor";
    public const string Admin      = "Admin";
    public const string Viewer     = "Viewer";
}
