using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Security;

/// <summary>
/// Validates SAP B1 user credentials and retrieves user role.
/// Uses SAP DI API to authenticate against SAP Business One.
/// </summary>
public class SapUserAuthService
{
    private readonly SapSettings _sapSettings;
    private readonly ILogger<SapUserAuthService> _logger;

    public SapUserAuthService(
        IOptions<SapSettings> sapSettings,
        ILogger<SapUserAuthService> logger)
    {
        _sapSettings = sapSettings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Validates SAP user credentials.
    /// Returns (IsValid, Role) tuple.
    /// Role is determined by SAP user's department or custom UDF.
    /// </summary>
    public async Task<(bool IsValid, string? Role)> ValidateCredentialsAsync(
        string sapUserCode,
        string password)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Attempt to connect to SAP with user's credentials
                var testCompany = new Company
                {
                    Server = _sapSettings.Server,
                    CompanyDB = _sapSettings.CompanyDB,
                    UserName = sapUserCode,
                    Password = password,
                    DbServerType = Enum.Parse<BoDataServerTypes>($"dst_{_sapSettings.DbServerType}"),
                    language = BoSuppLangs.ln_English,
                    LicenseServer = _sapSettings.LicenseServer,
                    SLDServer = _sapSettings.SLDServer
                };

                var result = testCompany.Connect();

                if (result != 0)
                {
                    var error = testCompany.GetLastErrorDescription();
                    _logger.LogWarning(
                        "SAP authentication failed for user {UserCode}: {Error}",
                        sapUserCode,
                        error);
                    return (false, null);
                }

                // Credentials valid - now get user role
                var role = GetUserRole(testCompany, sapUserCode);

                testCompany.Disconnect();

                _logger.LogInformation(
                    "SAP user {UserCode} authenticated successfully with role {Role}",
                    sapUserCode,
                    role);

                return (true, role);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error validating SAP credentials for user {UserCode}",
                    sapUserCode);
                return (false, null);
            }
        });
    }

    /// <summary>
    /// Determines user role from SAP user properties.
    /// Priority: Custom UDF (U_MolasRole) > Department > Default (Viewer).
    /// </summary>
    private string GetUserRole(Company company, string sapUserCode)
    {
        try
        {
            // Query SAP user master data
            var recordset = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

            // JOIN with OUDP to get department name (not code) for proper mapping
            // NOTE: U_MolasRole UDF is optional - if it exists, it will be checked first
            // For now, using only department-based mapping
            var query = $@"
                SELECT TOP 1 
                    U.USER_CODE,
                    ISNULL(D.Name, '') AS DepartmentName
                FROM OUSR U
                LEFT JOIN OUDP D ON U.Department = D.Code
                WHERE U.USER_CODE = '{sapUserCode.Replace("'", "''")}'
            ";

            recordset.DoQuery(query);

            if (!recordset.EoF)
            {
                // Map department name to role
                var departmentName = recordset.Fields.Item("DepartmentName").Value?.ToString();
                return MapDepartmentToRole(departmentName);
            }

            return "Viewer"; // Default role
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error retrieving role for SAP user {UserCode}, defaulting to Viewer",
                sapUserCode);
            return "Viewer";
        }
    }

    /// <summary>
    /// Maps SAP department names to application roles.
    /// Customize this based on your organization's department structure.
    /// Uses department NAME from OUDP table (e.g., "General", "Admin"), not numeric codes.
    /// </summary>
    private static string MapDepartmentToRole(string? department)
    {
        return department?.ToUpperInvariant() switch
        {
            "ADMIN" or "IT" or "GENERAL" => "Admin",
            "WAREHOUSE" or "LOGISTICS" => "Executor",
            "PURCHASING" or "PLANNING" => "Planner",
            "MANAGEMENT" or "SUPERVISOR" => "Supervisor",
            _ => "Viewer"
        };
    }

    /// <summary>
    /// Validates role string against known application roles.
    /// </summary>
    private static bool IsValidRole(string role)
    {
        var validRoles = new[] { "Viewer", "Planner", "Executor", "Supervisor", "Admin" };
        return validRoles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }
}
