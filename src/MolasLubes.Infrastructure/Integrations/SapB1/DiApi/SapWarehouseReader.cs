#pragma warning disable CA1416 // COM interop — Windows only

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Application.LiquiMolyReplenishment;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Reads active warehouses (OWHS) from a named SAP profile.
/// Used by the warehouse-options endpoint so the frontend can populate selectors.
/// </summary>
public class SapWarehouseReader
{
    private readonly IntegrationProfilesOptions _profiles;
    private readonly ILogger<SapWarehouseReader> _logger;

    public SapWarehouseReader(
        IOptions<IntegrationProfilesOptions> profileOptions,
        ILogger<SapWarehouseReader> logger)
    {
        _profiles = profileOptions.Value;
        _logger   = logger;
    }

    /// <summary>
    /// Returns active warehouses for both source and target profiles.
    /// </summary>
    public WarehouseOptionsResponse GetWarehouseOptions(
        string sourceProfile,
        string targetProfile)
    {
        return new WarehouseOptionsResponse
        {
            SourceProfile    = sourceProfile,
            TargetProfile    = targetProfile,
            SourceWarehouses = GetWarehousesForProfile(sourceProfile),
            TargetWarehouses = GetWarehousesForProfile(targetProfile)
        };
    }

    /// <summary>
    /// Returns all active warehouses from OWHS for the given profile.
    /// </summary>
    public List<WarehouseOption> GetWarehousesForProfile(string profileKey)
    {
        if (!_profiles.Profiles.TryGetValue(profileKey, out var profile))
        {
            _logger.LogWarning("SapWarehouseReader: Profile '{Profile}' not configured", profileKey);
            return new List<WarehouseOption>();
        }

        var result = new List<WarehouseOption>();
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

                rs.DoQuery(@"
SELECT WhsCode, WhsName
FROM OWHS
WHERE Inactive = 'N'
ORDER BY WhsCode
");

                while (!rs.EoF)
                {
                    var code = rs.Fields.Item("WhsCode").Value?.ToString() ?? string.Empty;
                    var name = rs.Fields.Item("WhsName").Value?.ToString() ?? code;

                    if (!string.IsNullOrWhiteSpace(code))
                        result.Add(new WarehouseOption { Code = code, Name = name });

                    rs.MoveNext();
                }
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
            finally
            {
                if (rs != null) Marshal.ReleaseComObject(rs);
                if (company != null)
                {
                    try { if (company.Connected) company.Disconnect(); } catch { /* best effort */ }
                    Marshal.ReleaseComObject(company);
                }
            }
            });
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
        {
            _logger.LogError(threadException,
                "SapWarehouseReader: failed to read warehouses | Profile={Profile}", profileKey);
            throw threadException;
        }

        _logger.LogDebug(
            "SapWarehouseReader: {Profile} → {Count} warehouse(s)", profileKey, result.Count);

        return result;
    }

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
}
