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
///   Source (Molas_Lubes_LTD): ItemCode IS the numeric article number (e.g. "2315").
///     U_Article_No may be blank; ItemCode is used directly as the article number.
///     All items are Liqui Moly — no brand discriminator needed on the source.
///
///   Target (MOLAS_Live_2021 / AutoHub): ItemCode uses LUB prefix (e.g. "LUB100011").
///     Article number is the leading digits in ItemName ("2315 TOP TEC 4600 5W30 1L").
///     Lookup: OITM WHERE ItemName LIKE '{article} %' AND U_MdlTEST = 'LIQUI MOLY'.
///
/// Example:
///   Source "2315" → article "2315" → target LUB100011 ("2315 TOP TEC 4600 5W30 1L")
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
            SapDiApiCriticalSection.Run(() =>
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
SELECT ItemCode, ItemName, frozenFor
FROM OITM
WHERE ItemCode = '{safeCode}'
");

                if (rs.EoF)
                {
                    result = LiquiMolyMappedLine.Fail(sourceItemCode, "NOT_FOUND",
                        $"Item '{sourceItemCode}' not found in {srcProfile.Sap.CompanyDB}.");
                    return;
                }

                var frozen  = rs.Fields.Item("frozenFor").Value?.ToString() ?? "N";
                var srcName = rs.Fields.Item("ItemName").Value?.ToString() ?? string.Empty;

                // In Molas_Lubes_LTD the ItemCode IS the numeric Liqui Moly article number.
                // All items in this company are Liqui Moly — no brand check needed.
                var articleNumber = sourceItemCode;

                Marshal.ReleaseComObject(rs); rs = null;

                if (frozen.Equals("Y", StringComparison.OrdinalIgnoreCase))
                {
                    result = LiquiMolyMappedLine.Fail(sourceItemCode, "FROZEN",
                        $"Item '{sourceItemCode}' is frozen in source.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(articleNumber))
                {
                    result = LiquiMolyMappedLine.Fail(sourceItemCode, "NO_ARTICLE_NUM",
                        $"Item '{sourceItemCode}' has no extractable Liqui Moly article number.");
                    return;
                }

                // ── Target lookup — match by article number prefix in ItemName ──────────
                // AutoHub uses LUB-prefix ItemCodes; article number is the leading digits
                // in ItemName, e.g. "2315 TOP TEC 4600 5W30 1L".
                tgtCompany = ConnectCompany(tgtProfile.Sap);
                rs = (Recordset)tgtCompany.GetBusinessObject(BoObjectTypes.BoRecordset);

                var safeArt = articleNumber.Replace("'", "''");
                rs.DoQuery($@"
SELECT TOP 1 ItemCode, ItemName
FROM OITM
WHERE (ItemName LIKE '{safeArt} %' OR ItemName LIKE '{safeArt}-%')
  AND U_MdlTEST = 'LIQUI MOLY'
  AND frozenFor = 'N'
ORDER BY ItemCode
");

                if (rs.EoF)
                {
                    result = LiquiMolyMappedLine.Fail(sourceItemCode, "TARGET_NOT_FOUND",
                        $"No active LIQUI MOLY target item with ItemCode='{articleNumber}' in {tgtProfile.Sap.CompanyDB}.");
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

            // Match the first 3–6 digit sequence that is not part of a longer digit run.
            // Uses negative lookahead/lookbehind instead of \b so that alpha-suffixed
            // tokens like "3091DOT" resolve correctly (\b treats digit→letter as within
            // the same \w run and would skip "3091" in "3091DOT 4 250MLS").
            var match = Regex.Match(text.Trim(), @"(?<!\d)\d{3,6}(?!\d)");
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
