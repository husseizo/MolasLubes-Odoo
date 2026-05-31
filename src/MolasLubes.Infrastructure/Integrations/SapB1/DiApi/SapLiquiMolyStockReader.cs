#pragma warning disable CA1416 // COM interop — Windows only

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Reads available stock quantity from a specific warehouse in a named SAP profile.
/// Uses OITW (item-warehouse stock) with a fresh STA-thread connection per call.
/// </summary>
public class SapLiquiMolyStockReader
{
    private readonly IntegrationProfilesOptions _profiles;
    private readonly ILogger<SapLiquiMolyStockReader> _logger;

    public SapLiquiMolyStockReader(
        IOptions<IntegrationProfilesOptions> profileOptions,
        ILogger<SapLiquiMolyStockReader> logger)
    {
        _profiles = profileOptions.Value;
        _logger   = logger;
    }

    /// <returns>OnHand quantity from OITW, or 0 if the item/warehouse row does not exist.</returns>
    public decimal GetAvailableQuantity(string profileKey, string itemCode, string warehouseCode)
    {
        if (!_profiles.Profiles.TryGetValue(profileKey, out var profile))
            throw new InvalidOperationException($"Profile '{profileKey}' not configured.");

        decimal qty = 0m;
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

                var safeCode = itemCode.Replace("'", "''");
                var safeWhs  = warehouseCode.Replace("'", "''");

                rs.DoQuery($@"
SELECT ISNULL(OnHand - IsCommited, 0) AS Available
FROM OITW
WHERE ItemCode = '{safeCode}'
  AND WhsCode  = '{safeWhs}'
");

                if (!rs.EoF)
                    qty = Convert.ToDecimal((object)rs.Fields.Item("Available").Value);
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

        if (threadException != null) throw threadException;

        _logger.LogDebug(
            "SapLiquiMolyStockReader: {Profile} | {ItemCode} @ {Whs} → Available={Qty}",
            profileKey, itemCode, warehouseCode, qty);

        return qty;
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
