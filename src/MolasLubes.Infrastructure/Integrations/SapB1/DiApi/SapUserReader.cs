#pragma warning disable CA1416 // COM interop — Windows only

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Validates SAP user codes against OUSR in the configured SAP companies.
/// Used by LiquiMolyRoleService to confirm actor identity before approvals/executions.
/// </summary>
public class SapUserReader
{
    private readonly IntegrationProfilesOptions _profiles;
    private readonly ILogger<SapUserReader> _logger;

    public SapUserReader(
        IOptions<IntegrationProfilesOptions> profileOptions,
        ILogger<SapUserReader> logger)
    {
        _profiles = profileOptions.Value;
        _logger   = logger;
    }

    /// <summary>
    /// Returns true if the given SAP user code exists and is not locked in the specified profile.
    /// </summary>
    public bool ValidateUser(string profileKey, string sapUserCode)
    {
        if (!_profiles.Profiles.TryGetValue(profileKey, out var profile))
            throw new InvalidOperationException($"Profile '{profileKey}' not configured.");

        bool found = false;
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            SapDiApiCriticalSection.Run(() =>
            {
            Company? company = null;
            Recordset? rs    = null;

            try
            {
                company = CreateAndConnect(profile.Sap);
                rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

                var safeCode = sapUserCode.Replace("'", "''");
                rs.DoQuery($@"
SELECT USER_CODE FROM OUSR
WHERE USER_CODE = '{safeCode}'
  AND LOCKED    = 'N'
");
                found = !rs.EoF;
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
            finally
            {
                if (rs != null) Marshal.ReleaseComObject(rs);
                DisconnectAndRelease(company);
            }
            });
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null) throw threadException;
        return found;
    }

    /// <summary>
    /// Returns true if the given SAP user code exists and is not locked in ANY configured profile.
    /// Accepts users registered in either Molas_Lubes_LTD or MOLAS_Live_2021.
    /// </summary>
    public bool ValidateUserAcrossProfiles(string sapUserCode)
    {
        foreach (var profileKey in _profiles.Profiles.Keys)
        {
            try
            {
                if (ValidateUser(profileKey, sapUserCode))
                {
                    _logger.LogDebug(
                        "SapUserReader: user '{User}' validated in profile '{Profile}'",
                        sapUserCode, profileKey);
                    return true;
                }
            }
            catch (Exception ex)
            {
                // Profile unavailable — log and continue to next
                _logger.LogWarning(ex,
                    "SapUserReader: could not validate user '{User}' in profile '{Profile}'",
                    sapUserCode, profileKey);
            }
        }

        return false;
    }

    // ── Helpers ──────────────────────────────────────────

    private static Company CreateAndConnect(SapSettings sap)
    {
        var company = new Company
        {
            Server        = sap.Server,
            CompanyDB     = sap.CompanyDB,
            UserName      = sap.UserName,
            Password      = sap.Password,
            DbServerType  = Enum.Parse<BoDataServerTypes>($"dst_{sap.DbServerType}"),
            language      = BoSuppLangs.ln_English,
            UseTrusted    = false,
            LicenseServer = sap.LicenseServer,
            SLDServer     = sap.SLDServer
        };

        if (company.Connect() != 0)
        {
            company.GetLastError(out var code, out var msg);
            throw new Exception($"SAP connect failed ({code}): {msg} — DB={sap.CompanyDB}");
        }

        return company;
    }

    private static void DisconnectAndRelease(Company? company)
    {
        if (company == null) return;
        try { if (company.Connected) company.Disconnect(); } catch { /* best effort */ }
        Marshal.ReleaseComObject(company);
    }
}
