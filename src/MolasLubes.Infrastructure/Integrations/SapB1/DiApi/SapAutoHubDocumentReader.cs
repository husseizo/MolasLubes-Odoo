#pragma warning disable CA1416 // COM interop — Windows only

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using SAPbobsCOM;
using System.Runtime.InteropServices;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

// ── Row records ──────────────────────────────────────────────────────────────

public record AutoHubDeliveryRow(
    int DocEntry, int DocNum, string CardCode, string? CardName,
    DateTime DocDate, string DocStatus, bool IsCancelled, string? Comments, DateTime? UpdatedInSap);

public record AutoHubDeliveryLineRow(
    int DocEntry, int LineNum, string ItemCode, string Description,
    decimal Quantity, decimal Price, decimal LineTotal, string? WhsCode);

public record AutoHubSalesOrderRow(
    int DocEntry, int DocNum, string CardCode, string? CardName,
    DateTime DocDate, DateTime? DocDueDate, string DocStatus, decimal DocTotal,
    bool IsCancelled, string? Comments, DateTime? UpdatedInSap);

public record AutoHubSalesOrderLineRow(
    int DocEntry, int LineNum, string ItemCode, string? Description,
    decimal Quantity, decimal OpenQty, decimal Price, decimal LineTotal, string? WhsCode);

public record AutoHubInvoiceRow(
    int DocEntry, int DocNum, string CardCode, string? CardName,
    DateTime DocDate, string DocStatus, decimal DocTotal, decimal VatSum, decimal PaidToDate,
    bool IsCancelled, string? Comments, DateTime? UpdatedInSap);

public record AutoHubInvoiceLineRow(
    int DocEntry, int LineNum, string ItemCode, string? Description,
    decimal Quantity, decimal Price, decimal LineTotal, string? WhsCode);

public record AutoHubGoodsReceiptRow(
    int DocEntry, int DocNum, DateTime DocDate, string? Comments, DateTime? UpdatedInSap);

public record AutoHubGoodsReceiptLineRow(
    int DocEntry, int LineNum, string ItemCode, string? Description,
    decimal Quantity, string? WhsCode);

public record AutoHubStockTransferRow(
    int DocEntry, int DocNum, DateTime DocDate, string? Comments, DateTime? UpdatedInSap);

public record AutoHubStockTransferLineRow(
    int DocEntry, int LineNum, string ItemCode, string? Description,
    decimal Quantity, string? FromWhsCode, string? ToWhsCode);

public record AutoHubInventoryCountingRow(
    int DocEntry, int DocNum, DateTime CountDate, string? Remarks, DateTime? UpdatedInSap);

public record AutoHubInventoryCountingLineRow(
    int DocEntry, int LineNum, string ItemCode, string? WhsCode, decimal CountedQty);

public record AutoHubPurchaseOrderRow(
    int DocEntry, int DocNum, string CardCode, string? CardName,
    DateTime DocDate, DateTime? DocDueDate, string DocStatus, decimal DocTotal,
    bool IsCancelled, string? Comments, DateTime? UpdatedInSap);

public record AutoHubPurchaseOrderLineRow(
    int DocEntry, int LineNum, string ItemCode, string? Description,
    decimal Quantity, decimal OpenQty, decimal Price, decimal LineTotal, string? WhsCode);

public record AutoHubUoMRow(int UomEntry, string UomCode, string? UomName, int? GroupEntry);

// ── Reader ───────────────────────────────────────────────────────────────────

/// <summary>
/// Reads AutoHub document types from SAP B1 using UpdateDate delta filtering.
/// Each Read* call opens its own COM/STA connection.
/// </summary>
public class SapAutoHubDocumentReader
{
    private const string ProfileKey = "AutoHub";

    private readonly IntegrationProfilesOptions _profiles;
    private readonly ILogger<SapAutoHubDocumentReader> _logger;

    public SapAutoHubDocumentReader(
        IOptions<IntegrationProfilesOptions> profileOptions,
        ILogger<SapAutoHubDocumentReader> logger)
    {
        _profiles = profileOptions.Value;
        _logger   = logger;
    }

    // ── Deliveries ───────────────────────────────────────────────────────────

    public (List<AutoHubDeliveryRow> Headers, List<AutoHubDeliveryLineRow> Lines)
        ReadDeliveries(string sinceDate) =>
        ExecuteDocQuery(
            sinceDate,
            headerTable: "ODLN",
            linesTable:  "DLN1",
            headerSql: $@"
SELECT DocEntry, DocNum, CardCode, CardName, DocDate, DocStatus, CANCELED, Comments,
       UpdateDate, UpdateTS
FROM ODLN
WHERE UpdateDate >= '{sinceDate}'
ORDER BY DocEntry",
            linesSql: $@"
SELECT l.DocEntry, l.LineNum, l.ItemCode, l.Dscription, l.Quantity, l.Price, l.LineTotal, l.WhsCode
FROM DLN1 l
INNER JOIN ODLN h ON h.DocEntry = l.DocEntry
WHERE h.UpdateDate >= '{sinceDate}'
ORDER BY l.DocEntry, l.LineNum",
            mapHeader: rs => new AutoHubDeliveryRow(
                DocEntry:     ToInt(rs, "DocEntry"),
                DocNum:       ToInt(rs, "DocNum"),
                CardCode:     Str(rs, "CardCode")!,
                CardName:     Str(rs, "CardName"),
                DocDate:      ToDate(rs, "DocDate"),
                DocStatus:    Str(rs, "DocStatus") ?? "O",
                IsCancelled:  Str(rs, "CANCELED") == "Y",
                Comments:     Str(rs, "Comments"),
                UpdatedInSap: ToSapDateTime(rs, "UpdateDate", "UpdateTS")),
            mapLine: rs => new AutoHubDeliveryLineRow(
                DocEntry:    ToInt(rs, "DocEntry"),
                LineNum:     ToInt(rs, "LineNum"),
                ItemCode:    Str(rs, "ItemCode")!,
                Description: Str(rs, "Dscription") ?? "",
                Quantity:    ToDec(rs, "Quantity"),
                Price:       ToDec(rs, "Price"),
                LineTotal:   ToDec(rs, "LineTotal"),
                WhsCode:     Str(rs, "WhsCode")));

    // ── Sales Orders ─────────────────────────────────────────────────────────

    public (List<AutoHubSalesOrderRow> Headers, List<AutoHubSalesOrderLineRow> Lines)
        ReadSalesOrders(string sinceDate) =>
        ExecuteDocQuery(
            sinceDate,
            headerTable: "ORDR",
            linesTable:  "RDR1",
            headerSql: $@"
SELECT DocEntry, DocNum, CardCode, CardName, DocDate, DocDueDate, DocStatus, DocTotal,
       CANCELED, Comments, UpdateDate, UpdateTS
FROM ORDR
WHERE UpdateDate >= '{sinceDate}'
ORDER BY DocEntry",
            linesSql: $@"
SELECT l.DocEntry, l.LineNum, l.ItemCode, l.Dscription, l.Quantity, l.OpenQty,
       l.Price, l.LineTotal, l.WhsCode
FROM RDR1 l
INNER JOIN ORDR h ON h.DocEntry = l.DocEntry
WHERE h.UpdateDate >= '{sinceDate}'
ORDER BY l.DocEntry, l.LineNum",
            mapHeader: rs => new AutoHubSalesOrderRow(
                DocEntry:     ToInt(rs, "DocEntry"),
                DocNum:       ToInt(rs, "DocNum"),
                CardCode:     Str(rs, "CardCode")!,
                CardName:     Str(rs, "CardName"),
                DocDate:      ToDate(rs, "DocDate"),
                DocDueDate:   ToNullDate(rs, "DocDueDate"),
                DocStatus:    Str(rs, "DocStatus") ?? "O",
                DocTotal:     ToDec(rs, "DocTotal"),
                IsCancelled:  Str(rs, "CANCELED") == "Y",
                Comments:     Str(rs, "Comments"),
                UpdatedInSap: ToSapDateTime(rs, "UpdateDate", "UpdateTS")),
            mapLine: rs => new AutoHubSalesOrderLineRow(
                DocEntry:    ToInt(rs, "DocEntry"),
                LineNum:     ToInt(rs, "LineNum"),
                ItemCode:    Str(rs, "ItemCode")!,
                Description: Str(rs, "Dscription"),
                Quantity:    ToDec(rs, "Quantity"),
                OpenQty:     ToDec(rs, "OpenQty"),
                Price:       ToDec(rs, "Price"),
                LineTotal:   ToDec(rs, "LineTotal"),
                WhsCode:     Str(rs, "WhsCode")));

    // ── AR Invoices ───────────────────────────────────────────────────────────

    public (List<AutoHubInvoiceRow> Headers, List<AutoHubInvoiceLineRow> Lines)
        ReadInvoices(string sinceDate) =>
        ExecuteDocQuery(
            sinceDate,
            headerTable: "OINV",
            linesTable:  "INV1",
            headerSql: $@"
SELECT DocEntry, DocNum, CardCode, CardName, DocDate, DocStatus, DocTotal, VatSum,
       PaidToDate, CANCELED, Comments, UpdateDate, UpdateTS
FROM OINV
WHERE UpdateDate >= '{sinceDate}'
ORDER BY DocEntry",
            linesSql: $@"
SELECT l.DocEntry, l.LineNum, l.ItemCode, l.Dscription, l.Quantity,
       l.Price, l.LineTotal, l.WhsCode
FROM INV1 l
INNER JOIN OINV h ON h.DocEntry = l.DocEntry
WHERE h.UpdateDate >= '{sinceDate}'
ORDER BY l.DocEntry, l.LineNum",
            mapHeader: rs => new AutoHubInvoiceRow(
                DocEntry:     ToInt(rs, "DocEntry"),
                DocNum:       ToInt(rs, "DocNum"),
                CardCode:     Str(rs, "CardCode")!,
                CardName:     Str(rs, "CardName"),
                DocDate:      ToDate(rs, "DocDate"),
                DocStatus:    Str(rs, "DocStatus") ?? "O",
                DocTotal:     ToDec(rs, "DocTotal"),
                VatSum:       ToDec(rs, "VatSum"),
                PaidToDate:   ToDec(rs, "PaidToDate"),
                IsCancelled:  Str(rs, "CANCELED") == "Y",
                Comments:     Str(rs, "Comments"),
                UpdatedInSap: ToSapDateTime(rs, "UpdateDate", "UpdateTS")),
            mapLine: rs => new AutoHubInvoiceLineRow(
                DocEntry:    ToInt(rs, "DocEntry"),
                LineNum:     ToInt(rs, "LineNum"),
                ItemCode:    Str(rs, "ItemCode")!,
                Description: Str(rs, "Dscription"),
                Quantity:    ToDec(rs, "Quantity"),
                Price:       ToDec(rs, "Price"),
                LineTotal:   ToDec(rs, "LineTotal"),
                WhsCode:     Str(rs, "WhsCode")));

    // ── Goods Receipts (OIGN) ─────────────────────────────────────────────────

    public (List<AutoHubGoodsReceiptRow> Headers, List<AutoHubGoodsReceiptLineRow> Lines)
        ReadGoodsReceipts(string sinceDate) =>
        ExecuteDocQuery(
            sinceDate,
            headerTable: "OIGN",
            linesTable:  "IGN1",
            headerSql: $@"
SELECT DocEntry, DocNum, DocDate, Comments, UpdateDate, UpdateTS
FROM OIGN
WHERE UpdateDate >= '{sinceDate}'
ORDER BY DocEntry",
            linesSql: $@"
SELECT l.DocEntry, l.LineNum, l.ItemCode, l.Dscription, l.Quantity, l.WhsCode
FROM IGN1 l
INNER JOIN OIGN h ON h.DocEntry = l.DocEntry
WHERE h.UpdateDate >= '{sinceDate}'
ORDER BY l.DocEntry, l.LineNum",
            mapHeader: rs => new AutoHubGoodsReceiptRow(
                DocEntry:     ToInt(rs, "DocEntry"),
                DocNum:       ToInt(rs, "DocNum"),
                DocDate:      ToDate(rs, "DocDate"),
                Comments:     Str(rs, "Comments"),
                UpdatedInSap: ToSapDateTime(rs, "UpdateDate", "UpdateTS")),
            mapLine: rs => new AutoHubGoodsReceiptLineRow(
                DocEntry:    ToInt(rs, "DocEntry"),
                LineNum:     ToInt(rs, "LineNum"),
                ItemCode:    Str(rs, "ItemCode")!,
                Description: Str(rs, "Dscription"),
                Quantity:    ToDec(rs, "Quantity"),
                WhsCode:     Str(rs, "WhsCode")));

    // ── Stock Transfers (OWTR) ────────────────────────────────────────────────

    public (List<AutoHubStockTransferRow> Headers, List<AutoHubStockTransferLineRow> Lines)
        ReadStockTransfers(string sinceDate) =>
        ExecuteDocQuery(
            sinceDate,
            headerTable: "OWTR",
            linesTable:  "WTR1",
            headerSql: $@"
SELECT DocEntry, DocNum, DocDate, Comments, UpdateDate, UpdateTS
FROM OWTR
WHERE UpdateDate >= '{sinceDate}'
ORDER BY DocEntry",
            linesSql: $@"
SELECT l.DocEntry, l.LineNum, l.ItemCode, l.Dscription, l.Quantity, l.FromWhsCod, l.WhsCode
FROM WTR1 l
INNER JOIN OWTR h ON h.DocEntry = l.DocEntry
WHERE h.UpdateDate >= '{sinceDate}'
ORDER BY l.DocEntry, l.LineNum",
            mapHeader: rs => new AutoHubStockTransferRow(
                DocEntry:     ToInt(rs, "DocEntry"),
                DocNum:       ToInt(rs, "DocNum"),
                DocDate:      ToDate(rs, "DocDate"),
                Comments:     Str(rs, "Comments"),
                UpdatedInSap: ToSapDateTime(rs, "UpdateDate", "UpdateTS")),
            mapLine: rs => new AutoHubStockTransferLineRow(
                DocEntry:     ToInt(rs, "DocEntry"),
                LineNum:      ToInt(rs, "LineNum"),
                ItemCode:     Str(rs, "ItemCode")!,
                Description:  Str(rs, "Dscription"),
                Quantity:     ToDec(rs, "Quantity"),
                FromWhsCode:  Str(rs, "FromWhsCod"),
                ToWhsCode:    Str(rs, "WhsCode")));

    // ── Inventory Counting (OINC) ─────────────────────────────────────────────

    public (List<AutoHubInventoryCountingRow> Headers, List<AutoHubInventoryCountingLineRow> Lines)
        ReadInventoryCountings(string sinceDate) =>
        ExecuteDocQuery(
            sinceDate,
            headerTable: "OINC",
            linesTable:  "INC1",
            headerSql: $@"
SELECT DocEntry, DocNum, Remarks, UpdateDate, UpdateTS
FROM OINC
WHERE UpdateDate >= '{sinceDate}'
ORDER BY DocEntry",
            linesSql: $@"
SELECT l.DocEntry, l.LineNum, l.ItemCode, l.WhsCode, l.CountQty
FROM INC1 l
INNER JOIN OINC h ON h.DocEntry = l.DocEntry
WHERE h.UpdateDate >= '{sinceDate}'
ORDER BY l.DocEntry, l.LineNum",
            mapHeader: rs => new AutoHubInventoryCountingRow(
                DocEntry:     ToInt(rs, "DocEntry"),
                DocNum:       ToInt(rs, "DocNum"),
                CountDate:    ToDate(rs, "UpdateDate"),
                Remarks:      Str(rs, "Remarks"),
                UpdatedInSap: ToSapDateTime(rs, "UpdateDate", "UpdateTS")),
            mapLine: rs => new AutoHubInventoryCountingLineRow(
                DocEntry:   ToInt(rs, "DocEntry"),
                LineNum:    ToInt(rs, "LineNum"),
                ItemCode:   Str(rs, "ItemCode")!,
                WhsCode:    Str(rs, "WhsCode"),
                CountedQty: ToDec(rs, "CountQty")));

    // ── Purchase Orders (OPOR) ────────────────────────────────────────────────

    public (List<AutoHubPurchaseOrderRow> Headers, List<AutoHubPurchaseOrderLineRow> Lines)
        ReadPurchaseOrders(string sinceDate) =>
        ExecuteDocQuery(
            sinceDate,
            headerTable: "OPOR",
            linesTable:  "POR1",
            headerSql: $@"
SELECT DocEntry, DocNum, CardCode, CardName, DocDate, DocDueDate, DocStatus, DocTotal,
       CANCELED, Comments, UpdateDate, UpdateTS
FROM OPOR
WHERE UpdateDate >= '{sinceDate}'
ORDER BY DocEntry",
            linesSql: $@"
SELECT l.DocEntry, l.LineNum, l.ItemCode, l.Dscription, l.Quantity, l.OpenQty,
       l.Price, l.LineTotal, l.WhsCode
FROM POR1 l
INNER JOIN OPOR h ON h.DocEntry = l.DocEntry
WHERE h.UpdateDate >= '{sinceDate}'
ORDER BY l.DocEntry, l.LineNum",
            mapHeader: rs => new AutoHubPurchaseOrderRow(
                DocEntry:     ToInt(rs, "DocEntry"),
                DocNum:       ToInt(rs, "DocNum"),
                CardCode:     Str(rs, "CardCode")!,
                CardName:     Str(rs, "CardName"),
                DocDate:      ToDate(rs, "DocDate"),
                DocDueDate:   ToNullDate(rs, "DocDueDate"),
                DocStatus:    Str(rs, "DocStatus") ?? "O",
                DocTotal:     ToDec(rs, "DocTotal"),
                IsCancelled:  Str(rs, "CANCELED") == "Y",
                Comments:     Str(rs, "Comments"),
                UpdatedInSap: ToSapDateTime(rs, "UpdateDate", "UpdateTS")),
            mapLine: rs => new AutoHubPurchaseOrderLineRow(
                DocEntry:    ToInt(rs, "DocEntry"),
                LineNum:     ToInt(rs, "LineNum"),
                ItemCode:    Str(rs, "ItemCode")!,
                Description: Str(rs, "Dscription"),
                Quantity:    ToDec(rs, "Quantity"),
                OpenQty:     ToDec(rs, "OpenQty"),
                Price:       ToDec(rs, "Price"),
                LineTotal:   ToDec(rs, "LineTotal"),
                WhsCode:     Str(rs, "WhsCode")));

    // ── UoMs (OUOM — full read, no delta) ─────────────────────────────────────

    public List<AutoHubUoMRow> ReadUoMs()
    {
        if (!_profiles.Profiles.TryGetValue(ProfileKey, out var profile))
            throw new InvalidOperationException($"Integration profile '{ProfileKey}' is not configured.");

        var results = new List<AutoHubUoMRow>();
        Exception? threadEx = null;

        var thread = new Thread(() =>
        {
            Company?   company = null;
            Recordset? rs      = null;
            try
            {
                company = Connect(profile.Sap);
                rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
                rs.DoQuery(@"
SELECT UomEntry, UomCode, UomName
FROM OUOM
ORDER BY UomEntry");
                while (!rs.EoF)
                {
                    results.Add(new AutoHubUoMRow(
                        UomEntry:   ToInt(rs, "UomEntry"),
                        UomCode:    Str(rs, "UomCode") ?? "",
                        UomName:    Str(rs, "UomName"),
                        GroupEntry: null));
                    rs.MoveNext();
                }
            }
            catch (Exception ex) { threadEx = ex; }
            finally
            {
                if (rs      != null) Marshal.ReleaseComObject(rs);
                if (company is { Connected: true }) company.Disconnect();
                if (company != null) Marshal.ReleaseComObject(company);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null) throw threadEx;

        _logger.LogInformation("SapAutoHubDocumentReader.ReadUoMs: {Count} rows", results.Count);
        return results;
    }

    // ── Generic two-query document executor ──────────────────────────────────

    private (List<TH>, List<TL>) ExecuteDocQuery<TH, TL>(
        string sinceDate,
        string headerTable,
        string linesTable,
        string headerSql,
        string linesSql,
        Func<Recordset, TH?> mapHeader,
        Func<Recordset, TL?> mapLine)
    {
        if (!_profiles.Profiles.TryGetValue(ProfileKey, out var profile))
            throw new InvalidOperationException($"Integration profile '{ProfileKey}' is not configured.");

        var headers   = new List<TH>();
        var lines     = new List<TL>();
        Exception? threadEx = null;

        var thread = new Thread(() =>
        {
            Company?   company = null;
            Recordset? rs      = null;
            try
            {
                company = Connect(profile.Sap);
                rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

                rs.DoQuery(headerSql);
                while (!rs.EoF)
                {
                    var row = mapHeader(rs);
                    if (row != null) headers.Add(row);
                    rs.MoveNext();
                }

                rs.DoQuery(linesSql);
                while (!rs.EoF)
                {
                    var row = mapLine(rs);
                    if (row != null) lines.Add(row);
                    rs.MoveNext();
                }
            }
            catch (Exception ex) { threadEx = ex; }
            finally
            {
                if (rs      != null) Marshal.ReleaseComObject(rs);
                if (company is { Connected: true }) company.Disconnect();
                if (company != null) Marshal.ReleaseComObject(company);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null) throw threadEx;

        _logger.LogInformation(
            "SapAutoHubDocumentReader: {Table} since={Since} | Headers={H} Lines={L}",
            headerTable, sinceDate, headers.Count, lines.Count);

        return (headers, lines);
    }

    // ── SAP connection helper ─────────────────────────────────────────────────

    internal static Company Connect(SapSettings sap)
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
            throw new Exception($"SapAutoHubDocumentReader: SAP connect failed ({code}): {msg}");
        }

        return company;
    }

    // ── Field read helpers ────────────────────────────────────────────────────

    private static string?   Str(Recordset rs, string col)    => rs.Fields.Item(col).Value?.ToString();
    private static int       ToInt(Recordset rs, string col)  => Convert.ToInt32(rs.Fields.Item(col).Value ?? 0);
    private static int?      ToNullInt(Recordset rs, string col)
    {
        var v = rs.Fields.Item(col).Value;
        return v == null || v is DBNull ? null : Convert.ToInt32(v);
    }
    private static decimal   ToDec(Recordset rs, string col)  => Convert.ToDecimal(rs.Fields.Item(col).Value ?? 0m);

    private static DateTime ToDate(Recordset rs, string col)
    {
        var v = rs.Fields.Item(col).Value;
        if (v == null || v is DBNull) return DateTime.MinValue;
        var dt = Convert.ToDateTime(v);
        return DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);
    }

    private static DateTime? ToNullDate(Recordset rs, string col)
    {
        var v = rs.Fields.Item(col).Value;
        if (v == null || v is DBNull) return null;
        var dt = Convert.ToDateTime(v);
        if (dt == DateTime.MinValue || dt.Year < 1900) return null;
        return DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);
    }

    private static DateTime? ToSapDateTime(Recordset rs, string dateCol, string timeCol)
    {
        var v = rs.Fields.Item(dateCol).Value;
        if (v == null || v is DBNull) return null;
        var dt = Convert.ToDateTime(v);
        if (dt.Year < 1900) return null;

        var timeVal = Convert.ToInt32(rs.Fields.Item(timeCol).Value ?? 0);
        var h = timeVal / 10000;
        var m = (timeVal / 100) % 100;
        var s = timeVal % 100;

        h = Math.Clamp(h, 0, 23);
        m = Math.Clamp(m, 0, 59);
        s = Math.Clamp(s, 0, 59);

        return DateTime.SpecifyKind(
            new DateTime(dt.Year, dt.Month, dt.Day, h, m, s),
            DateTimeKind.Utc);
    }
}
