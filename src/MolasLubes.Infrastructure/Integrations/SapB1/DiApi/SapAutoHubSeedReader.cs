#pragma warning disable CA1416 // COM interop — Windows only

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.Germax;
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
    private readonly GermaxScraperSettings _scraperSettings;
    private readonly ILogger<SapAutoHubSeedReader> _logger;

    public SapAutoHubSeedReader(
        IOptions<IntegrationProfilesOptions> profileOptions,
        IOptions<GermaxScraperSettings> scraperOptions,
        ILogger<SapAutoHubSeedReader> logger)
    {
        _profiles        = profileOptions.Value;
        _scraperSettings = scraperOptions.Value;
        _logger          = logger;
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

                var sql = BuildQuery(watermark, _scraperSettings.AllowedItemGroups);
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

    // =====================================================
    // TANTIVY PARTS — VIKA / BORSEHUNG / DPA items only
    // =====================================================
    public List<TantivyPartRow> ReadTantivyParts()
    {
        if (!_profiles.Profiles.TryGetValue(ProfileKey, out var profile))
            throw new InvalidOperationException(
                $"Integration profile '{ProfileKey}' is not configured.");

        var sap = profile.Sap;
        var results = new List<TantivyPartRow>();
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            Company? company = null;
            Recordset? rs = null;

            try
            {
                company = new Company
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
                    throw new Exception(
                        $"SapAutoHubSeedReader.ReadTantivyParts: SAP connect failed ({code}): {msg}");
                }

                rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

                const string sql = @"
SELECT
    T0.ItemCode,
    T0.ItemName,
    T0.U_MdlTEST,
    T0.U_Article_No,
    T0.U_Engine_Code
FROM OITM T0
WHERE
    T0.frozenFor  = 'N'
    AND T0.InvntItem = 'Y'
    AND T0.U_MdlTEST IN ('VIKA', 'BORSEHUNG', 'DPA')
ORDER BY T0.U_MdlTEST, T0.ItemCode";

                rs.DoQuery(sql);

                while (!rs.EoF)
                {
                    results.Add(new TantivyPartRow(
                        ItemCode:   rs.Fields.Item("ItemCode").Value?.ToString()    ?? "",
                        ItemName:   rs.Fields.Item("ItemName").Value?.ToString()    ?? "",
                        MdlTest:    rs.Fields.Item("U_MdlTEST").Value?.ToString(),
                        ArticleNo:  rs.Fields.Item("U_Article_No").Value?.ToString(),
                        EngineCode: rs.Fields.Item("U_Engine_Code").Value?.ToString()
                    ));
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
            "SapAutoHubSeedReader.ReadTantivyParts: {Count} rows returned", results.Count);

        return results;
    }

    private static string BuildQuery(DateTime? watermark, IReadOnlyList<string> allowedGroups)
    {
        if (allowedGroups.Count == 0)
            throw new InvalidOperationException(
                "GermaxScraper.AllowedItemGroups must contain at least one entry.");

        // Groups come from config, not user input — single-quote escaping is a safety measure
        var inClause = string.Join(", ",
            allowedGroups.Select(g => $"'{g.Replace("'", "''")}'"));

        var baseQuery = $@"
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
    AND T1.ItmsGrpNam IN ({inClause})";

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

public record TantivyPartRow(
    string  ItemCode,
    string  ItemName,
    string? MdlTest,
    string? ArticleNo,
    string? EngineCode);
