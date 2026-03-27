#pragma warning disable CA1416 // COM interop — Windows only

using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Reads all active LIQUI MOLY items from the source company (Molas_Lubes_LTD) and
/// builds an article-number → ItemCode lookup map.
///
/// Example:
///   "3682" → "LUB100001"
///   "8374" → "LUB100002"
///
/// Uses the same ExtractArticleNumber logic as SapLiquiMolyItemMapper so that the
/// reverse-mapping is consistent with the forward-mapping used in transfers.
/// </summary>
public class SapLiquiMolySourceMapReader
{
    private readonly IntegrationProfilesOptions _profiles;
    private readonly ILogger<SapLiquiMolySourceMapReader> _logger;

    public SapLiquiMolySourceMapReader(
        IOptions<IntegrationProfilesOptions> profileOptions,
        ILogger<SapLiquiMolySourceMapReader> logger)
    {
        _profiles = profileOptions.Value;
        _logger   = logger;
    }

    /// <summary>
    /// Returns a dictionary keyed by extracted article number → (ItemCode, AvailableStock).
    /// Joins OITW for the given warehouse so the analyzer can cap suggested quantities by
    /// what the supplier actually has on hand minus committed.
    /// Items with no extractable article number are skipped.
    /// </summary>
    public IReadOnlyDictionary<string, SourceItemData> ReadSourceItemMap(
        string profileKey, string warehouseCode)
    {
        if (!_profiles.Profiles.TryGetValue(profileKey, out var profile))
            throw new InvalidOperationException($"Profile '{profileKey}' not configured.");

        var map = new Dictionary<string, SourceItemData>(StringComparer.OrdinalIgnoreCase);
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
SELECT i.ItemCode, i.ItemName, i.U_Item_Name,
       ISNULL(w.OnHand,     0) AS OnHand,
       ISNULL(w.IsCommited, 0) AS Committed
FROM OITM i
LEFT JOIN OITW w ON w.ItemCode = i.ItemCode
                AND w.WhsCode  = '{safeWhs}'
WHERE i.U_MdlTEST = 'LIQUI MOLY'
  AND i.frozenFor = 'N'
ORDER BY i.ItemCode
");

                while (!rs.EoF)
                {
                    var itemCode  = rs.Fields.Item("ItemCode").Value?.ToString() ?? string.Empty;
                    var itemName  = rs.Fields.Item("ItemName").Value?.ToString();
                    var uItemName = rs.Fields.Item("U_Item_Name").Value?.ToString()?.Trim();
                    var onHand    = Convert.ToDecimal((object)rs.Fields.Item("OnHand").Value);
                    var committed = Convert.ToDecimal((object)rs.Fields.Item("Committed").Value);

                    var artNum = ExtractArticleNumber(uItemName, itemName);
                    if (artNum != null && !map.ContainsKey(artNum))
                        map[artNum] = new SourceItemData(itemCode, onHand - committed);

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
            "SapLiquiMolySourceMapReader: {Profile} @ {Whs} → {Count} article→item mappings",
            profileKey, warehouseCode, map.Count);

        return map;
    }

    /// <summary>
    /// Returns a dictionary keyed by extracted article number → source ItemCode.
    /// Items whose U_Item_Name and ItemName yield no numeric token are skipped.
    /// </summary>
    public IReadOnlyDictionary<string, string> ReadArticleMap(string profileKey)
    {
        if (!_profiles.Profiles.TryGetValue(profileKey, out var profile))
            throw new InvalidOperationException($"Profile '{profileKey}' not configured.");

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            Company? company = null;
            Recordset? rs    = null;

            try
            {
                company = CreateAndConnect(profile.Sap);
                rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

                rs.DoQuery(@"
SELECT ItemCode, ItemName, U_Item_Name
FROM OITM
WHERE U_MdlTEST = 'LIQUI MOLY'
  AND frozenFor = 'N'
ORDER BY ItemCode
");

                while (!rs.EoF)
                {
                    var itemCode    = rs.Fields.Item("ItemCode").Value?.ToString() ?? string.Empty;
                    var itemName    = rs.Fields.Item("ItemName").Value?.ToString();
                    var uItemName   = rs.Fields.Item("U_Item_Name").Value?.ToString()?.Trim();

                    var artNum = ExtractArticleNumber(uItemName, itemName);
                    if (artNum != null && !map.ContainsKey(artNum))
                        map[artNum] = itemCode;

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
            "SapLiquiMolySourceMapReader: {Profile} → {Count} article→ItemCode mappings",
            profileKey, map.Count);

        return map;
    }

    // ── Helpers ──────────────────────────────────────────

    /// <summary>
    /// Mirrors SapLiquiMolyItemMapper.ExtractArticleNumber — must stay in sync.
    /// </summary>
    private static string? ExtractArticleNumber(string? uItemName, string? itemName)
    {
        static string? FindNumber(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var match = Regex.Match(text.Trim(), @"(?<!\d)\d{3,6}(?!\d)");
            return match.Success ? match.Value : null;
        }

        return FindNumber(uItemName) ?? FindNumber(itemName);
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

    private static void DisconnectAndRelease(Company? company)
    {
        if (company == null) return;
        try { if (company.Connected) company.Disconnect(); } catch { /* best effort */ }
        Marshal.ReleaseComObject(company);
    }
}

/// <summary>
/// Source item identity + current available stock in a specific warehouse.
/// Available = OnHand - IsCommited (SAP B1 definition).
/// </summary>
public record SourceItemData(string ItemCode, decimal AvailableStock);
