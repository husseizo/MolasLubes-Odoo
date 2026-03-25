#pragma warning disable CA1416 // COM interop — Windows only

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.Germax.Dtos;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Reads seed items from the MOLAS_Live_2021 SAP company (AutoHub profile).
/// Completely isolated from SapDiApiConnection (Profile A). Creates its own
/// COM Company object from the AutoHub profile settings.
/// </summary>
public class SapAutoHubSeedReader
{
    private const string ProfileKey = "AutoHub";

    private readonly IntegrationProfilesOptions _profiles;
    private readonly ILogger<SapAutoHubSeedReader> _logger;

    public SapAutoHubSeedReader(
        IOptions<IntegrationProfilesOptions> profileOptions,
        ILogger<SapAutoHubSeedReader> logger)
    {
        _profiles = profileOptions.Value;
        _logger   = logger;
    }

    // =====================================================
    // FULL SEED — all active Land Rover / Volvo items
    // =====================================================
    public List<GermaxSeedDto> ReadAll()
    {
        _logger.LogInformation(
            "SapAutoHubSeedReader: full read started (MOLAS_Live_2021)");

        return ExecuteQuery(null);
    }

    // =====================================================
    // DELTA SEED — only items changed since watermark
    // =====================================================
    public List<GermaxSeedDto> ReadSince(DateTime watermark)
    {
        _logger.LogInformation(
            "SapAutoHubSeedReader: delta read started (watermark={Watermark:u})",
            watermark);

        return ExecuteQuery(watermark);
    }

    // =====================================================
    // INTERNAL — STA thread, connection-per-call
    // =====================================================
    private List<GermaxSeedDto> ExecuteQuery(DateTime? watermark)
    {
        if (!_profiles.Profiles.TryGetValue(ProfileKey, out var profile))
            throw new InvalidOperationException(
                $"Integration profile '{ProfileKey}' is not configured.");

        var sap = profile.Sap;
        var results = new List<GermaxSeedDto>();
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            Company? company = null;
            Recordset? rs = null;

            try
            {
                company = new Company
                {
                    Server       = sap.Server,
                    CompanyDB    = sap.CompanyDB,
                    UserName     = sap.UserName,
                    Password     = sap.Password,
                    DbServerType = Enum.Parse<BoDataServerTypes>($"dst_{sap.DbServerType}"),
                    language     = BoSuppLangs.ln_English,
                    UseTrusted   = false,
                    LicenseServer = sap.LicenseServer,
                    SLDServer    = sap.SLDServer
                };

                if (company.Connect() != 0)
                {
                    company.GetLastError(out var code, out var msg);
                    throw new Exception(
                        $"SapAutoHubSeedReader: SAP connect failed ({code}): {msg}");
                }

                _logger.LogInformation(
                    "SapAutoHubSeedReader: connected to {CompanyDB}", sap.CompanyDB);

                rs = (Recordset)company.GetBusinessObject(
                    BoObjectTypes.BoRecordset);

                var sql = BuildQuery(watermark);
                rs.DoQuery(sql);

                while (!rs.EoF)
                {
                    results.Add(new GermaxSeedDto
                    {
                        ItemCode      = rs.Fields.Item("ItemCode").Value?.ToString()   ?? "",
                        ItemName      = rs.Fields.Item("ItemName").Value?.ToString()   ?? "",
                        EngineCode    = rs.Fields.Item("U_Engine_Code").Value?.ToString(),
                        ItemGroupCode = rs.Fields.Item("ItmsGrpCod").Value?.ToString() ?? "",
                        ItemGroupName = rs.Fields.Item("ItmsGrpNam").Value?.ToString() ?? ""
                    });
                    rs.MoveNext();
                }
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
            finally
            {
                if (rs != null)
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(rs);

                if (company != null && company.Connected)
                    company.Disconnect();

                if (company != null)
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(company);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw threadException;

        _logger.LogInformation(
            "SapAutoHubSeedReader: read completed | Count={Count}", results.Count);

        return results;
    }

    private static string BuildQuery(DateTime? watermark)
    {
        var baseQuery = @"
SELECT
    T0.ItemCode,
    T0.ItemName,
    T0.U_Engine_Code,
    T1.ItmsGrpCod,
    T1.ItmsGrpNam,
    T0.UpdateDate
FROM OITM T0
INNER JOIN OITB T1
    ON T0.ItmsGrpCod = T1.ItmsGrpCod
WHERE
    T0.frozenFor = 'N'
    AND T1.ItmsGrpNam IN ('Land Rover', 'Volvo')";

        if (watermark.HasValue)
        {
            var ts = watermark.Value.ToString("yyyy-MM-dd");
            baseQuery += $@"
    AND T0.UpdateDate >= '{ts}'
ORDER BY T0.UpdateDate, T0.ItemCode";
        }
        else
        {
            baseQuery += @"
ORDER BY T1.ItmsGrpNam, T0.ItemCode";
        }

        return baseQuery;
    }
}
