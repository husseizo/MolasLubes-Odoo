#pragma warning disable CA1416 // COM interop — Windows only

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using SAPbobsCOM;
using System.Runtime.InteropServices;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public record AutoHubStockRow(string ItemCode, string ItemName, decimal OnHand, decimal Available);

/// <summary>
/// Reads per-item stock totals (summed across all warehouses) from the AutoHub SAP B1 company.
/// Uses the same COM/STA thread pattern as SapAutoHubSeedReader — one connection per call.
/// </summary>
public class SapAutoHubStockReader
{
    private const string ProfileKey = "AutoHub";

    private readonly IntegrationProfilesOptions _profiles;
    private readonly ILogger<SapAutoHubStockReader> _logger;

    public SapAutoHubStockReader(
        IOptions<IntegrationProfilesOptions> profileOptions,
        ILogger<SapAutoHubStockReader> logger)
    {
        _profiles = profileOptions.Value;
        _logger   = logger;
    }

    public List<AutoHubStockRow> ReadAll()
    {
        if (!_profiles.Profiles.TryGetValue(ProfileKey, out var profile))
            throw new InvalidOperationException($"Integration profile '{ProfileKey}' is not configured.");

        var results         = new List<AutoHubStockRow>();
        Exception? threadEx = null;

        var thread = new Thread(() =>
        {
            Company?   company = null;
            Recordset? rs      = null;

            try
            {
                var sap = profile.Sap;

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
                        $"SapAutoHubStockReader: SAP connect failed ({code}): {msg}");
                }

                _logger.LogInformation(
                    "SapAutoHubStockReader: connected to {Db}", sap.CompanyDB);

                rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

                rs.DoQuery(@"
SELECT
    i.ItemCode,
    i.ItemName,
    CONVERT(DECIMAL(19,6), SUM(ISNULL(w.OnHand,     0)))                          AS OnHand,
    CONVERT(DECIMAL(19,6), SUM(ISNULL(w.OnHand, 0) - ISNULL(w.IsCommited, 0)))    AS Available
FROM OITM i
LEFT JOIN OITW w ON w.ItemCode = i.ItemCode
WHERE i.frozenFor = 'N'
GROUP BY i.ItemCode, i.ItemName
ORDER BY i.ItemCode");

                while (!rs.EoF)
                {
                    var itemCode = rs.Fields.Item("ItemCode").Value?.ToString() ?? "";
                    var itemName = rs.Fields.Item("ItemName").Value?.ToString() ?? "";
                    var onHand   = Convert.ToDecimal(rs.Fields.Item("OnHand").Value   ?? 0m);
                    var avail    = Convert.ToDecimal(rs.Fields.Item("Available").Value ?? 0m);

                    if (!string.IsNullOrWhiteSpace(itemCode))
                        results.Add(new AutoHubStockRow(itemCode, itemName, onHand, avail));

                    rs.MoveNext();
                }
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
            finally
            {
                if (rs != null)      Marshal.ReleaseComObject(rs);
                if (company is { Connected: true }) company.Disconnect();
                if (company != null) Marshal.ReleaseComObject(company);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null) throw threadEx;

        _logger.LogInformation(
            "SapAutoHubStockReader: read complete | Count={Count}", results.Count);

        return results;
    }
}
