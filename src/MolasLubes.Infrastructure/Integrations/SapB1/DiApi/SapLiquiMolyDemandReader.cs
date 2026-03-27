#pragma warning disable CA1416 // COM interop — Windows only

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Reads Liqui Moly item demand metrics from the AutoHub company (MOLAS_Live_2021).
/// Returns current available stock + 30/60/90-day sold quantities from AR Invoices.
/// </summary>
public class SapLiquiMolyDemandReader
{
    private readonly IntegrationProfilesOptions _profiles;
    private readonly ILogger<SapLiquiMolyDemandReader> _logger;

    public SapLiquiMolyDemandReader(
        IOptions<IntegrationProfilesOptions> profileOptions,
        ILogger<SapLiquiMolyDemandReader> logger)
    {
        _profiles = profileOptions.Value;
        _logger   = logger;
    }

    /// <summary>
    /// Reads all active LIQUI MOLY items from the given profile with their current available
    /// stock in the specified warehouse and invoice-based sales metrics for 30/60/90 days.
    /// </summary>
    public IReadOnlyList<LiquiMolyDemandItem> ReadDemandItems(string profileKey, string warehouseCode)
    {
        if (!_profiles.Profiles.TryGetValue(profileKey, out var profile))
            throw new InvalidOperationException($"Profile '{profileKey}' not configured.");

        var results       = new List<LiquiMolyDemandItem>();
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            Company? company = null;
            Recordset? rs    = null;

            try
            {
                company = CreateAndConnect(profile.Sap);
                rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

                var safeWhs = warehouseCode.Replace("'", "''");
                rs.DoQuery($@"
SELECT
    i.ItemCode,
    i.ItemName,
    ISNULL(w.OnHand,     0) AS OnHand,
    ISNULL(w.IsCommited, 0) AS Committed,
    ISNULL(SUM(CASE WHEN h.DocDate >= DATEADD(day, -30, GETDATE())
                    THEN l.Quantity ELSE 0 END), 0) AS Qty30d,
    ISNULL(SUM(CASE WHEN h.DocDate >= DATEADD(day, -60, GETDATE())
                    THEN l.Quantity ELSE 0 END), 0) AS Qty60d,
    ISNULL(SUM(CASE WHEN h.DocDate >= DATEADD(day, -90, GETDATE())
                    THEN l.Quantity ELSE 0 END), 0) AS Qty90d
FROM OITM i
LEFT JOIN OITW w ON w.ItemCode = i.ItemCode
                AND w.WhsCode  = '{safeWhs}'
LEFT JOIN INV1 l ON l.ItemCode = i.ItemCode
LEFT JOIN OINV h ON h.DocEntry  = l.DocEntry
                AND h.DocDate  >= DATEADD(day, -90, GETDATE())
                AND ISNULL(h.CANCELED, 'N') = 'N'
WHERE i.U_MdlTEST = 'LIQUI MOLY'
  AND i.frozenFor = 'N'
GROUP BY i.ItemCode, i.ItemName, w.OnHand, w.IsCommited
ORDER BY i.ItemCode
");

                while (!rs.EoF)
                {
                    var onHand    = Convert.ToDecimal((object)rs.Fields.Item("OnHand").Value);
                    var committed = Convert.ToDecimal((object)rs.Fields.Item("Committed").Value);

                    results.Add(new LiquiMolyDemandItem(
                        ItemCode:   rs.Fields.Item("ItemCode").Value?.ToString() ?? string.Empty,
                        ItemName:   rs.Fields.Item("ItemName").Value?.ToString(),
                        Available:  onHand - committed,
                        QtySold30d: Convert.ToDecimal((object)rs.Fields.Item("Qty30d").Value),
                        QtySold60d: Convert.ToDecimal((object)rs.Fields.Item("Qty60d").Value),
                        QtySold90d: Convert.ToDecimal((object)rs.Fields.Item("Qty90d").Value)));

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
                DisconnectAndRelease(company);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null) throw threadException;

        _logger.LogInformation(
            "SapLiquiMolyDemandReader: {Profile} @ {Whs} → {Count} items",
            profileKey, warehouseCode, results.Count);

        return results;
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

/// <summary>
/// Raw demand metrics for one Liqui Moly item in the target company.
/// ItemCode in the target DB equals the Liqui Moly article number (e.g. "3682").
/// </summary>
public record LiquiMolyDemandItem(
    string  ItemCode,
    string? ItemName,
    decimal Available,
    decimal QtySold30d,
    decimal QtySold60d,
    decimal QtySold90d);
