#pragma warning disable CA1416 // COM interop — Windows only

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Reads Inventory Transfer Requests (OWTQ) and Stock Transfers (OWTR)
/// from the MolasLubes SAP company for syncing to Neon.
/// </summary>
public class SapLiquiMolyTransferReader
{
    private const string ProfileKey = "MolasLubes";

    // SAP B1 ObjectType numbers for the two transfer document types.
    public const string OwtqObjectType = "67";
    public const string OwtrObjectType = "1250000001";

    private readonly IntegrationProfilesOptions _profiles;
    private readonly ILogger<SapLiquiMolyTransferReader> _logger;

    public SapLiquiMolyTransferReader(
        IOptions<IntegrationProfilesOptions> profileOptions,
        ILogger<SapLiquiMolyTransferReader> logger)
    {
        _profiles = profileOptions.Value;
        _logger   = logger;
    }

    /// <summary>
    /// Reads all OWTQ and OWTR documents whose UpdateDate is on or after <paramref name="fromDate"/>.
    /// Used for watermark-based incremental sync — no outbox required.
    /// </summary>
    public IReadOnlyList<SapTransferDocumentDto> ReadTransfersSince(DateTime fromDate)
    {
        // Build synthetic requests: one entry per DocType with a sentinel DocEntry of -1
        // signals the overloaded reader to use date-filter mode.
        // Simpler: delegate to the per-doctype reader used by ReadTransfers.
        var results         = new List<SapTransferDocumentDto>();
        Exception? threadEx = null;

        if (!_profiles.Profiles.TryGetValue(ProfileKey, out var profile))
            throw new InvalidOperationException($"Profile '{ProfileKey}' not configured.");

        var fromDateStr = fromDate.ToString("yyyy-MM-dd");

        var thread = new Thread(() =>
        {
            SapDiApiCriticalSection.Run(() =>
            {
                Company? company = null;
                Recordset? rs    = null;

                try
                {
                    company = CreateAndConnect(profile.Sap);
                    rs      = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

                    foreach (var (docType, hdrTable, lineTable) in new[]
                    {
                        ("OWTQ", "OWTQ", "OWTQ1"),
                        ("OWTR", "OWTR", "WTR1")
                    })
                    {
                        // ── Headers by UpdateDate ─────────────────────────────
                        rs.DoQuery($@"
SELECT h.DocEntry, h.DocNum,
       ISNULL(h.DocDate,    '1970-01-01') AS DocDate,
       ISNULL(h.TaxDate,    '1970-01-01') AS TaxDate,
       ISNULL(h.DocDueDate, '1970-01-01') AS DocDueDate,
       ISNULL(h.fWhsCode, '') AS fWhsCode,
       ISNULL(h.tWhsCode, '') AS tWhsCode,
       h.Comments,
       ISNULL(h.DocStatus, 'O') AS DocStatus,
       h.UserSign,
       ISNULL(h.DocTotal, 0) AS DocTotal
FROM {hdrTable} h
WHERE h.UpdateDate >= '{fromDateStr}'");

                        var headers = new Dictionary<int, (int DocNum, DateTime DocDate, DateTime? TaxDate,
                            DateTime? DocDueDate, string From, string To, string? Comments,
                            string Status, int? UserSign, decimal Total)>();

                        while (!rs.EoF)
                        {
                            var de = ToInt(rs.Fields.Item("DocEntry").Value);
                            headers[de] = (
                                DocNum:     ToInt(rs.Fields.Item("DocNum").Value),
                                DocDate:    ToDate(rs.Fields.Item("DocDate").Value)!.Value,
                                TaxDate:    ToDate(rs.Fields.Item("TaxDate").Value),
                                DocDueDate: ToDate(rs.Fields.Item("DocDueDate").Value),
                                From:       rs.Fields.Item("fWhsCode").Value?.ToString() ?? "",
                                To:         rs.Fields.Item("tWhsCode").Value?.ToString() ?? "",
                                Comments:   NullIfEmpty(rs.Fields.Item("Comments").Value?.ToString()),
                                Status:     rs.Fields.Item("DocStatus").Value?.ToString() ?? "O",
                                UserSign:   ToNullableInt(rs.Fields.Item("UserSign").Value),
                                Total:      ToDecimal(rs.Fields.Item("DocTotal").Value));
                            rs.MoveNext();
                        }

                        if (headers.Count == 0) continue;

                        var inList = string.Join(",", headers.Keys);

                        // ── Lines ─────────────────────────────────────────────
                        rs.DoQuery($@"
SELECT l.DocEntry, l.LineNum, l.ItemCode,
       l.Dscription,
       ISNULL(l.Quantity,    0) AS Quantity,
       ISNULL(l.OpenQty,     0) AS OpenQty,
       l.uomCode,
       ISNULL(l.FromWhsCode, '') AS FromWhsCode,
       ISNULL(l.ToWhsCode,   '') AS ToWhsCode,
       l.Price, l.LineTotal,
       l.BaseType, l.BaseEntry, l.BaseLine
FROM {lineTable} l
WHERE l.DocEntry IN ({inList})
ORDER BY l.DocEntry, l.LineNum");

                        var linesByDoc = new Dictionary<int, List<SapTransferLineDto>>();
                        while (!rs.EoF)
                        {
                            var de = ToInt(rs.Fields.Item("DocEntry").Value);
                            if (!linesByDoc.TryGetValue(de, out List<SapTransferLineDto>? bucket))
                                linesByDoc[de] = bucket = new List<SapTransferLineDto>();

                            bucket.Add(new SapTransferLineDto(
                                DocEntry:    de,
                                LineNum:     ToInt(rs.Fields.Item("LineNum").Value),
                                ItemCode:    rs.Fields.Item("ItemCode").Value?.ToString() ?? "",
                                Description: NullIfEmpty(rs.Fields.Item("Dscription").Value?.ToString()),
                                Quantity:    ToDecimal(rs.Fields.Item("Quantity").Value),
                                OpenQty:     ToDecimal(rs.Fields.Item("OpenQty").Value),
                                UomCode:     NullIfEmpty(rs.Fields.Item("uomCode").Value?.ToString()),
                                FromWhsCode: NullIfEmpty(rs.Fields.Item("FromWhsCode").Value?.ToString()),
                                ToWhsCode:   NullIfEmpty(rs.Fields.Item("ToWhsCode").Value?.ToString()),
                                Price:       ToNullableDecimal(rs.Fields.Item("Price").Value),
                                LineTotal:   ToNullableDecimal(rs.Fields.Item("LineTotal").Value),
                                BaseType:    ToNullableInt(rs.Fields.Item("BaseType").Value),
                                BaseEntry:   ToNullableInt(rs.Fields.Item("BaseEntry").Value),
                                BaseLine:    ToNullableInt(rs.Fields.Item("BaseLine").Value)));

                            rs.MoveNext();
                        }

                        foreach (var (de, h) in headers)
                        {
                            results.Add(new SapTransferDocumentDto(
                                DocEntry:    de,
                                DocType:     docType,
                                DocNum:      h.DocNum,
                                DocDate:     h.DocDate,
                                TaxDate:     h.TaxDate,
                                DocDueDate:  h.DocDueDate,
                                FromWhsCode: h.From,
                                ToWhsCode:   h.To,
                                Comments:    h.Comments,
                                DocStatus:   h.Status,
                                UserSign:    h.UserSign,
                                DocTotal:    h.Total,
                                Lines:       linesByDoc.TryGetValue(de, out var ls) ? ls : new List<SapTransferLineDto>()));
                        }
                    }
                }
                catch (Exception ex)
                {
                    threadEx = ex;
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

        if (threadEx != null) throw threadEx;

        _logger.LogInformation(
            "SapLiquiMolyTransferReader: read {Count} transfers since {From}",
            results.Count, fromDate);

        return results;
    }

    /// <summary>
    /// Reads a batch of transfer documents from SAP.  Documents with the same DocType
    /// are fetched in a single SQL round-trip to minimise connection time.
    /// </summary>
    public IReadOnlyList<SapTransferDocumentDto> ReadTransfers(
        IReadOnlyList<(int DocEntry, string DocType)> requests)
    {
        if (requests.Count == 0) return Array.Empty<SapTransferDocumentDto>();

        if (!_profiles.Profiles.TryGetValue(ProfileKey, out var profile))
            throw new InvalidOperationException($"Profile '{ProfileKey}' not configured.");

        var results         = new List<SapTransferDocumentDto>();
        Exception? threadEx = null;

        var thread = new Thread(() =>
        {
            SapDiApiCriticalSection.Run(() =>
            {
                Company? company = null;
                Recordset? rs    = null;

                try
                {
                    company = CreateAndConnect(profile.Sap);
                    rs      = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

                    foreach (var group in requests.GroupBy(r => r.DocType))
                    {
                        var docType     = group.Key;
                        var entries     = group.Select(g => g.DocEntry).ToList();
                        var inList      = string.Join(",", entries);
                        var (hdrTable, lineTable) = docType == "OWTR"
                            ? ("OWTR", "WTR1")
                            : ("OWTQ", "OWTQ1");

                        // ── Headers ──────────────────────────────────────────
                        rs.DoQuery($@"
SELECT h.DocEntry, h.DocNum,
       ISNULL(h.DocDate,    '1970-01-01') AS DocDate,
       ISNULL(h.TaxDate,    '1970-01-01') AS TaxDate,
       ISNULL(h.DocDueDate, '1970-01-01') AS DocDueDate,
       ISNULL(h.fWhsCode, '') AS fWhsCode,
       ISNULL(h.tWhsCode, '') AS tWhsCode,
       h.Comments,
       ISNULL(h.DocStatus, 'O') AS DocStatus,
       h.UserSign,
       ISNULL(h.DocTotal, 0) AS DocTotal
FROM {hdrTable} h
WHERE h.DocEntry IN ({inList})");

                        var headers = new Dictionary<int, (int DocNum, DateTime DocDate, DateTime? TaxDate,
                            DateTime? DocDueDate, string From, string To, string? Comments,
                            string Status, int? UserSign, decimal Total)>();

                        while (!rs.EoF)
                        {
                            var de = ToInt(rs.Fields.Item("DocEntry").Value);
                            headers[de] = (
                                DocNum:    ToInt(rs.Fields.Item("DocNum").Value),
                                DocDate:   ToDate(rs.Fields.Item("DocDate").Value)!.Value,
                                TaxDate:   ToDate(rs.Fields.Item("TaxDate").Value),
                                DocDueDate:ToDate(rs.Fields.Item("DocDueDate").Value),
                                From:      rs.Fields.Item("fWhsCode").Value?.ToString() ?? "",
                                To:        rs.Fields.Item("tWhsCode").Value?.ToString() ?? "",
                                Comments:  NullIfEmpty(rs.Fields.Item("Comments").Value?.ToString()),
                                Status:    rs.Fields.Item("DocStatus").Value?.ToString() ?? "O",
                                UserSign:  ToNullableInt(rs.Fields.Item("UserSign").Value),
                                Total:     ToDecimal(rs.Fields.Item("DocTotal").Value));

                            rs.MoveNext();
                        }

                        if (headers.Count == 0) continue;

                        // ── Lines ─────────────────────────────────────────────
                        rs.DoQuery($@"
SELECT l.DocEntry, l.LineNum, l.ItemCode,
       l.Dscription,
       ISNULL(l.Quantity,    0) AS Quantity,
       ISNULL(l.OpenQty,     0) AS OpenQty,
       l.uomCode,
       ISNULL(l.FromWhsCode, '') AS FromWhsCode,
       ISNULL(l.ToWhsCode,   '') AS ToWhsCode,
       l.Price, l.LineTotal,
       l.BaseType, l.BaseEntry, l.BaseLine
FROM {lineTable} l
WHERE l.DocEntry IN ({inList})
ORDER BY l.DocEntry, l.LineNum");

                        var linesByDoc = new Dictionary<int, List<SapTransferLineDto>>();
                        while (!rs.EoF)
                        {
                            var de = ToInt(rs.Fields.Item("DocEntry").Value);
                            if (!linesByDoc.TryGetValue(de, out List<SapTransferLineDto>? bucket))
                                linesByDoc[de] = bucket = new List<SapTransferLineDto>();

                            bucket.Add(new SapTransferLineDto(
                                DocEntry:    de,
                                LineNum:     ToInt(rs.Fields.Item("LineNum").Value),
                                ItemCode:    rs.Fields.Item("ItemCode").Value?.ToString() ?? "",
                                Description: NullIfEmpty(rs.Fields.Item("Dscription").Value?.ToString()),
                                Quantity:    ToDecimal(rs.Fields.Item("Quantity").Value),
                                OpenQty:     ToDecimal(rs.Fields.Item("OpenQty").Value),
                                UomCode:     NullIfEmpty(rs.Fields.Item("uomCode").Value?.ToString()),
                                FromWhsCode: NullIfEmpty(rs.Fields.Item("FromWhsCode").Value?.ToString()),
                                ToWhsCode:   NullIfEmpty(rs.Fields.Item("ToWhsCode").Value?.ToString()),
                                Price:       ToNullableDecimal(rs.Fields.Item("Price").Value),
                                LineTotal:   ToNullableDecimal(rs.Fields.Item("LineTotal").Value),
                                BaseType:    ToNullableInt(rs.Fields.Item("BaseType").Value),
                                BaseEntry:   ToNullableInt(rs.Fields.Item("BaseEntry").Value),
                                BaseLine:    ToNullableInt(rs.Fields.Item("BaseLine").Value)));

                            rs.MoveNext();
                        }

                        foreach (var (de, h) in headers)
                        {
                            results.Add(new SapTransferDocumentDto(
                                DocEntry:    de,
                                DocType:     docType,
                                DocNum:      h.DocNum,
                                DocDate:     h.DocDate,
                                TaxDate:     h.TaxDate,
                                DocDueDate:  h.DocDueDate,
                                FromWhsCode: h.From,
                                ToWhsCode:   h.To,
                                Comments:    h.Comments,
                                DocStatus:   h.Status,
                                UserSign:    h.UserSign,
                                DocTotal:    h.Total,
                                Lines:       linesByDoc.TryGetValue(de, out var ls) ? ls : new List<SapTransferLineDto>()));
                        }
                    }
                }
                catch (Exception ex)
                {
                    threadEx = ex;
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

        if (threadEx != null) throw threadEx;

        _logger.LogInformation(
            "SapLiquiMolyTransferReader: read {Count} transfer documents",
            results.Count);

        return results;
    }

    // ── Conversion helpers ────────────────────────────────────────────────────

    private static int ToInt(object val) =>
        Convert.ToInt32(val);

    private static decimal ToDecimal(object val) =>
        Convert.ToDecimal(val);

    private static int? ToNullableInt(object val)
    {
        if (val == null || Convert.IsDBNull(val)) return null;
        var i = Convert.ToInt32(val);
        return i == int.MinValue ? (int?)null : i;
    }

    private static decimal? ToNullableDecimal(object val)
    {
        if (val == null || Convert.IsDBNull(val)) return null;
        return Convert.ToDecimal(val);
    }

    private static DateTime? ToDate(object val)
    {
        if (val == null || Convert.IsDBNull(val)) return null;
        var dt = Convert.ToDateTime(val);
        return dt.Year < 1900 ? (DateTime?)null : dt;
    }

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;

    // ── SAP connection helpers ────────────────────────────────────────────────

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
