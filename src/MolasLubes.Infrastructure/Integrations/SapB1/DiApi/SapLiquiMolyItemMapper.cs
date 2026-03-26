#pragma warning disable CA1416 // COM interop — Windows only

using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Maps a single Liqui Moly source item to its counterpart in the target SAP company.
///
/// Identity model:
///   Source: LUB1000xx (internal ItemCode)  →  article number extracted from U_Item_Name / ItemName
///   Target: OITM.ItemCode == articleNumber  (target DB uses the numeric article number as ItemCode)
///
/// Example:
///   LUB100001 → U_Item_Name "3682 TOP TEC ATF 1200 5L" → article 3682 → target ItemCode "3682"
///   LUB100016 → U_Item_Name "2512 ATFFLUSH"             → article 2512 → target ItemCode "2512"
/// </summary>
public class SapLiquiMolyItemMapper
{
    private readonly IntegrationProfilesOptions _profiles;
    private readonly ILogger<SapLiquiMolyItemMapper> _logger;

    public SapLiquiMolyItemMapper(
        IOptions<IntegrationProfilesOptions> profileOptions,
        ILogger<SapLiquiMolyItemMapper> logger)
    {
        _profiles = profileOptions.Value;
        _logger   = logger;
    }

    /// <summary>
    /// Connects to both source and target SAP companies on a single STA thread and
    /// resolves the item mapping for one transfer line.
    /// </summary>
    public LiquiMolyMappedLine MapLine(
        string sourceProfile,
        string targetProfile,
        string sourceItemCode)
    {
        if (!_profiles.Profiles.TryGetValue(sourceProfile, out var srcProfile))
            throw new InvalidOperationException($"Source profile '{sourceProfile}' not configured.");
        if (!_profiles.Profiles.TryGetValue(targetProfile, out var tgtProfile))
            throw new InvalidOperationException($"Target profile '{targetProfile}' not configured.");

        LiquiMolyMappedLine? result = null;
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            Company? srcCompany = null;
            Company? tgtCompany = null;
            Recordset? rs = null;

            try
            {
                // ── Source lookup ──────────────────────────────────────────
                srcCompany = ConnectCompany(srcProfile.Sap);
                rs = (Recordset)srcCompany.GetBusinessObject(BoObjectTypes.BoRecordset);

                var safeCode = sourceItemCode.Replace("'", "''");
                rs.DoQuery($@"
SELECT ItemCode, ItemName, U_Item_Name, U_MdlTEST, frozenFor
FROM OITM
WHERE ItemCode = '{safeCode}'
");

                if (rs.EoF)
                {
                    result = LiquiMolyMappedLine.Fail(sourceItemCode, "NOT_FOUND",
                        $"Item '{sourceItemCode}' not found in {srcProfile.Sap.CompanyDB}.");
                    return;
                }

                var brand        = rs.Fields.Item("U_MdlTEST").Value?.ToString() ?? string.Empty;
                var frozen       = rs.Fields.Item("frozenFor").Value?.ToString() ?? "N";
                var srcName      = rs.Fields.Item("ItemName").Value?.ToString() ?? string.Empty;
                var sourceLmName = rs.Fields.Item("U_Item_Name").Value?.ToString()?.Trim() ?? string.Empty;

                // Extract the Liqui Moly article number (3–6 digit token) from U_Item_Name,
                // falling back to ItemName. First token wins if it is purely numeric.
                var articleNumber = ExtractArticleNumber(sourceLmName, srcName);

                Marshal.ReleaseComObject(rs); rs = null;

                if (!brand.Equals("LIQUI MOLY", StringComparison.OrdinalIgnoreCase))
                {
                    result = LiquiMolyMappedLine.Fail(sourceItemCode, "NOT_LIQUI_MOLY",
                        $"Item '{sourceItemCode}' has brand '{brand}', expected LIQUI MOLY.");
                    return;
                }

                if (frozen.Equals("Y", StringComparison.OrdinalIgnoreCase))
                {
                    result = LiquiMolyMappedLine.Fail(sourceItemCode, "FROZEN",
                        $"Item '{sourceItemCode}' is frozen in source.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(articleNumber))
                {
                    result = LiquiMolyMappedLine.Fail(sourceItemCode, "NO_ARTICLE_NUM",
                        $"Item '{sourceItemCode}' has no extractable Liqui Moly article number in U_Item_Name or ItemName.");
                    return;
                }

                // ── Target lookup — match by OITM.ItemCode == articleNumber ───────────
                tgtCompany = ConnectCompany(tgtProfile.Sap);
                rs = (Recordset)tgtCompany.GetBusinessObject(BoObjectTypes.BoRecordset);

                var safeArt = articleNumber.Replace("'", "''");
                rs.DoQuery($@"
SELECT TOP 1 ItemCode, ItemName
FROM OITM
WHERE ItemCode  = '{safeArt}'
  AND frozenFor = 'N'
ORDER BY ItemCode
");

                if (rs.EoF)
                {
                    result = LiquiMolyMappedLine.Fail(sourceItemCode, "TARGET_NOT_FOUND",
                        $"No active target item with ItemCode='{articleNumber}' in {tgtProfile.Sap.CompanyDB}.");
                    return;
                }

                var tgtItemCode = rs.Fields.Item("ItemCode").Value?.ToString() ?? string.Empty;
                var tgtName     = rs.Fields.Item("ItemName").Value?.ToString() ?? string.Empty;

                result = new LiquiMolyMappedLine
                {
                    SourceItemCode = sourceItemCode,
                    TargetItemCode = tgtItemCode,
                    ArticleNumber  = articleNumber,
                    SourceItemName = srcName,
                    TargetItemName = tgtName,
                    Outcome        = "OK"
                };
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
            finally
            {
                if (rs          != null) Marshal.ReleaseComObject(rs);
                Disconnect(srcCompany);
                Disconnect(tgtCompany);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null) throw threadException;
        return result!;
    }

    // ── Helpers ──────────────────────────────────────────

    /// <summary>
    /// Extracts the Liqui Moly article number (3–6 consecutive digits) from U_Item_Name,
    /// falling back to ItemName. Prefers the first space-delimited token if it is purely
    /// numeric, then falls back to the first \b\d{3,6}\b match anywhere in the string.
    /// Returns null when no numeric token is found (triggers NO_ARTICLE_NUM).
    /// </summary>
    private static string? ExtractArticleNumber(string? uItemName, string? itemName)
    {
        static string? FindNumber(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var trimmed    = text.Trim();
            var firstToken = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(firstToken) &&
                Regex.IsMatch(firstToken, @"^\d{3,6}$"))
                return firstToken;

            var match = Regex.Match(trimmed, @"\b\d{3,6}\b");
            return match.Success ? match.Value : null;
        }

        return FindNumber(uItemName) ?? FindNumber(itemName);
    }

    private static Company ConnectCompany(SapSettings sap)
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

    private static void Disconnect(Company? company)
    {
        if (company == null) return;
        try { if (company.Connected) company.Disconnect(); } catch { /* best effort */ }
        Marshal.ReleaseComObject(company);
    }
}

public class LiquiMolyMappedLine
{
    public string  SourceItemCode { get; init; } = string.Empty;
    public string? TargetItemCode { get; init; }
    public string? ArticleNumber  { get; init; }
    public string? SourceItemName { get; init; }
    public string? TargetItemName { get; init; }
    public string  Outcome        { get; init; } = string.Empty;
    public string? FailReason     { get; init; }

    public bool IsOk => Outcome == "OK";

    public static LiquiMolyMappedLine Fail(string sourceItemCode, string outcome, string reason) =>
        new() { SourceItemCode = sourceItemCode, Outcome = outcome, FailReason = reason };
}
