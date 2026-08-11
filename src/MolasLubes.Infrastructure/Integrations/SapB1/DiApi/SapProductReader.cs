#pragma warning disable CA1416 // Possible null argument

using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;
using MolasLubes.Infrastructure.Integrations.SapB1.Udfs;
using SAPbobsCOM;
using System.Threading;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapProductReader
{
    private readonly SapDiApiConnection _connection;
    private readonly ILogger<SapProductReader> _logger;

    public SapProductReader(
        SapDiApiConnection connection,
        ILogger<SapProductReader> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    // =====================================================
    // READ ALL PRODUCTS (READ-ONLY)
    // =====================================================
    public List<SapProductDto> ReadAllProducts()
    {
        _logger.LogInformation("📦 SAP Product read started (STA thread)");

        var results = new List<SapProductDto>();
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            Company? company = null;
            Recordset? rs = null;

            try
            {
                company = _connection.CreateNewCompany();

                if (company.Connect() != 0)
                {
                    company.GetLastError(out var code, out var msg);
                    throw new Exception($"SAP connect failed {code}: {msg}");
                }

                rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

                rs.DoQuery(@"
SELECT
    T0.ItemCode,
    T0.ItemName,
    T1.WhsCode,
    T1.OnHand,

    MAX(T3.BcdCode) AS Barcode,

    T0.U_Odoo_Product_ID,
    T0.U_Odoo_Status,
    T0.U_Odoo_LastSync,
    T0.U_Odoo_SyncDir,

    MAX(CASE WHEN T2.PriceList = 1 THEN T2.Price END) AS PriceList_1,
    MAX(CASE WHEN T2.PriceList = 2 THEN T2.Price END) AS PriceList_2,
    MAX(CASE WHEN T2.PriceList = 3 THEN T2.Price END) AS PriceList_3

FROM OITM T0
INNER JOIN OITW T1
    ON T0.ItemCode = T1.ItemCode

LEFT JOIN ITM1 T2
    ON T0.ItemCode = T2.ItemCode

LEFT JOIN OBCD T3
    ON T0.ItemCode = T3.ItemCode

WHERE T0.frozenFor = 'N'

GROUP BY
    T0.ItemCode,
    T0.ItemName,
    T1.WhsCode,
    T1.OnHand,
    T0.U_Odoo_Product_ID,
    T0.U_Odoo_Status,
    T0.U_Odoo_LastSync,
    T0.U_Odoo_SyncDir

ORDER BY T0.ItemCode, T1.WhsCode
");

                while (!rs.EoF)
                {
                    var dto = new SapProductDto
                    {
                        ItemCode = rs.Fields.Item("ItemCode").Value?.ToString() ?? "",
                        ItemName = rs.Fields.Item("ItemName").Value?.ToString() ?? "",
                        WarehouseCode = rs.Fields.Item("WhsCode").Value?.ToString() ?? "",
                        OnHand = Convert.ToDecimal(rs.Fields.Item("OnHand").Value ?? 0),

                        Barcode = rs.Fields.Item("Barcode").Value?.ToString(),

                        OdooProductId = rs.Fields.Item(OdooUdfs.ProductId).Value?.ToString(),
                        OdooStatus = rs.Fields.Item(OdooUdfs.Status).Value?.ToString(),
                        OdooSyncDir = rs.Fields.Item(OdooUdfs.SyncDir).Value?.ToString(),
                        OdooLastSync = rs.Fields.Item(OdooUdfs.LastSync).Value as DateTime?
                    };

                    dto.PriceLists[1] = Convert.ToDecimal(rs.Fields.Item("PriceList_1").Value ?? 0);
                    dto.PriceLists[2] = Convert.ToDecimal(rs.Fields.Item("PriceList_2").Value ?? 0);
                    dto.PriceLists[3] = Convert.ToDecimal(rs.Fields.Item("PriceList_3").Value ?? 0);

                    results.Add(dto);
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
            "📦 SAP Product read completed | Count={Count}",
            results.Count);

        return results;
    }

    // =====================================================
    // BARCODE DIAGNOSTIC — UoM group + barcodes for one item
    // =====================================================
    public SapItemBarcodeInfo? ReadItemBarcodeInfo(string itemCode)
    {
        SapItemBarcodeInfo? result = null;
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            Company?   company = null;
            Recordset? rs      = null;
            try
            {
                company = _connection.CreateNewCompany();
                if (company.Connect() != 0)
                {
                    company.GetLastError(out var code, out var msg);
                    throw new Exception($"SAP connect failed {code}: {msg}");
                }

                rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

                // 1. Item header + UoM group rows — one row per UoM in the group,
                //    including whether a barcode exists for that UoM.
                rs.DoQuery($@"
SELECT
    I.ItemCode,
    I.ItemName,
    I.UgpEntry,
    I.InvntItem,
    I.frozenFor,
    G.UomEntry,
    U.UomCode,
    U.UomName,
    G.BaseQty,
    (SELECT TOP 1 BcdCode
     FROM OBCD B2
     WHERE B2.ItemCode = I.ItemCode
       AND B2.UomEntry = G.UomEntry
       AND ISNULL(B2.BcdCode,'') <> '') AS BcdCode
FROM OITM I
INNER JOIN UGP1 G ON G.UgpEntry = I.UgpEntry
INNER JOIN OUOM U ON U.UomEntry = G.UomEntry
WHERE I.ItemCode = '{itemCode}'
ORDER BY G.BaseQty");

                string? itemName = null;
                int? ugpEntry = null;
                bool? isInventory = null;
                bool? isFrozen = null;
                var uomRows = new List<SapItemUomRow>();

                while (!rs.EoF)
                {
                    if (itemName == null)
                    {
                        itemName    = rs.Fields.Item("ItemName").Value?.ToString();
                        ugpEntry    = ToNullInt(rs, "UgpEntry");
                        isInventory = rs.Fields.Item("InvntItem").Value?.ToString() == "Y";
                        isFrozen    = rs.Fields.Item("frozenFor").Value?.ToString() == "Y";
                    }

                    uomRows.Add(new SapItemUomRow(
                        UomEntry:   ToNullInt(rs, "UomEntry"),
                        UomCode:    rs.Fields.Item("UomCode").Value?.ToString() ?? "",
                        UomName:    rs.Fields.Item("UomName").Value?.ToString(),
                        BaseQty:    ToNullDec(rs, "BaseQty"),
                        BcdCode:    rs.Fields.Item("BcdCode").Value?.ToString()));

                    rs.MoveNext();
                }

                // 2. All barcodes for the item (including any UoM not in the group — edge case)
                rs.DoQuery($@"
SELECT B.BcdCode, B.UomEntry, U.UomCode, U.UomName
FROM OBCD B
LEFT JOIN OUOM U ON U.UomEntry = B.UomEntry
WHERE B.ItemCode = '{itemCode}'
ORDER BY U.UomCode");

                var extraBarcodes = new List<SapItemBarcodeRow>();
                while (!rs.EoF)
                {
                    var bcdCode = rs.Fields.Item("BcdCode").Value?.ToString();
                    if (!string.IsNullOrEmpty(bcdCode))
                    {
                        extraBarcodes.Add(new SapItemBarcodeRow(
                            BcdCode:  bcdCode,
                            UomEntry: ToNullInt(rs, "UomEntry"),
                            UomCode:  rs.Fields.Item("UomCode").Value?.ToString(),
                            UomName:  rs.Fields.Item("UomName").Value?.ToString()));
                    }
                    rs.MoveNext();
                }

                if (itemName != null)
                {
                    result = new SapItemBarcodeInfo(
                        ItemCode:    itemCode,
                        ItemName:    itemName,
                        UgpEntry:    ugpEntry,
                        IsInventory: isInventory ?? false,
                        IsFrozen:    isFrozen ?? false,
                        UomGroup:    uomRows,
                        AllBarcodes: extraBarcodes);
                }
            }
            catch (Exception ex) { threadException = ex; }
            finally
            {
                if (rs      != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(rs);
                if (company != null && company.Connected) company.Disconnect();
                if (company != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(company);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null) throw threadException;
        return result;
    }

    // =====================================================
    // PACKAGING UoM LOOKUP
    // Returns itemCode → smallest non-Unit UoM code from UGP1.
    // Used by the barcode-fill job to know which UoM to write
    // the packaging EAN against for MANWHSE items.
    // =====================================================
    public Dictionary<string, string> ReadPackagingUomCodes(IEnumerable<string> itemCodes)
    {
        var codes  = itemCodes.Distinct().ToList();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (codes.Count == 0) return result;

        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            Company?   company = null;
            Recordset? rs      = null;
            try
            {
                company = _connection.CreateNewCompany();
                if (company.Connect() != 0)
                {
                    company.GetLastError(out var code, out var msg);
                    throw new Exception($"SAP connect failed {code}: {msg}");
                }

                rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

                var inList = string.Join(",", codes.Select(c => $"'{c.Replace("'", "''")}'"));

                // One row per item: the smallest non-Unit UoM (by BaseQty) in the UoM group.
                rs.DoQuery($@"
SELECT I.ItemCode, U.UomCode, G.BaseQty
FROM OITM I
INNER JOIN UGP1 G ON G.UgpEntry = I.UgpEntry
INNER JOIN OUOM U ON U.UomEntry = G.UomEntry
WHERE I.ItemCode IN ({inList})
  AND U.UomCode <> 'Unit'
  AND G.BaseQty > 1
ORDER BY I.ItemCode, G.BaseQty");

                while (!rs.EoF)
                {
                    var itemCode = rs.Fields.Item("ItemCode").Value?.ToString() ?? "";
                    var uomCode  = rs.Fields.Item("UomCode").Value?.ToString() ?? "";

                    // First row per item = smallest BaseQty → packaging UoM
                    if (!string.IsNullOrEmpty(itemCode) && !string.IsNullOrEmpty(uomCode)
                        && !result.ContainsKey(itemCode))
                    {
                        result[itemCode] = uomCode;
                    }
                    rs.MoveNext();
                }
            }
            catch (Exception ex) { threadException = ex; }
            finally
            {
                if (rs      != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(rs);
                if (company != null && company.Connected) company.Disconnect();
                if (company != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(company);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null) throw threadException;
        return result;
    }

    // =====================================================
    // MISSING BARCODE REPORT
    // Returns all active, inventory-tracked items in MANWHSE
    // that lack a packaging barcode, and all in COCWHSE that
    // lack a unit barcode.
    // =====================================================
    public List<SapMissingBarcodeRow> ReadMissingBarcodeReport()
    {
        var results = new List<SapMissingBarcodeRow>();
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            Company?   company = null;
            Recordset? rs      = null;
            try
            {
                company = _connection.CreateNewCompany();
                if (company.Connect() != 0)
                {
                    company.GetLastError(out var code, out var msg);
                    throw new Exception($"SAP connect failed {code}: {msg}");
                }

                rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
                rs.DoQuery(@"
SELECT W.ItemCode, I.ItemName, 'MANWHSE' AS WhsCode, 'Missing packaging barcode' AS Issue
FROM OITW W
INNER JOIN OITM I ON I.ItemCode = W.ItemCode
WHERE W.WhsCode = 'MANWHSE'
  AND I.frozenFor = 'N'
  AND I.InvntItem = 'Y'
  AND NOT EXISTS (
      SELECT 1 FROM OBCD B
      INNER JOIN OUOM U ON U.UomEntry = B.UomEntry
      WHERE B.ItemCode = W.ItemCode AND U.UomCode <> 'Unit'
  )
UNION ALL
SELECT W.ItemCode, I.ItemName, 'COCWHSE' AS WhsCode, 'Missing unit barcode' AS Issue
FROM OITW W
INNER JOIN OITM I ON I.ItemCode = W.ItemCode
WHERE W.WhsCode = 'COCWHSE'
  AND I.frozenFor = 'N'
  AND I.InvntItem = 'Y'
  AND NOT EXISTS (
      SELECT 1 FROM OBCD B
      INNER JOIN OUOM U ON U.UomEntry = B.UomEntry
      WHERE B.ItemCode = W.ItemCode AND U.UomCode = 'Unit'
  )
ORDER BY WhsCode, ItemCode");

                while (!rs.EoF)
                {
                    results.Add(new SapMissingBarcodeRow(
                        ItemCode: rs.Fields.Item("ItemCode").Value?.ToString() ?? "",
                        ItemName: rs.Fields.Item("ItemName").Value?.ToString() ?? "",
                        WhsCode:  rs.Fields.Item("WhsCode").Value?.ToString()  ?? "",
                        Issue:    rs.Fields.Item("Issue").Value?.ToString()     ?? ""));
                    rs.MoveNext();
                }
            }
            catch (Exception ex) { threadException = ex; }
            finally
            {
                if (rs      != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(rs);
                if (company != null && company.Connected) company.Disconnect();
                if (company != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(company);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null) throw threadException;
        return results;
    }

    // =====================================================
    // LEGACY CODEBARS BACKFILL SOURCE
    // Returns COCWHSE items where OITM.CodeBars has a value
    // but OBCD has no matching Unit barcode yet.
    // Used by fill-from-codebars endpoint and as a fallback
    // pass in SapBarcodeFillJob after the web scraper runs.
    // =====================================================
    public List<SapLegacyCodeBarsRow> ReadCocwhseItemsWithLegacyCodeBars()
    {
        var results = new List<SapLegacyCodeBarsRow>();
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            Company?   company = null;
            Recordset? rs      = null;
            try
            {
                company = _connection.CreateNewCompany();
                if (company.Connect() != 0)
                {
                    company.GetLastError(out var code, out var msg);
                    throw new Exception($"SAP connect failed {code}: {msg}");
                }

                rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
                rs.DoQuery(@"
SELECT W.ItemCode, I.ItemName, I.CodeBars
FROM OITW W
INNER JOIN OITM I ON I.ItemCode = W.ItemCode
WHERE W.WhsCode = 'COCWHSE'
  AND I.frozenFor = 'N'
  AND I.InvntItem = 'Y'
  AND ISNULL(I.CodeBars, '') <> ''
  AND NOT EXISTS (
      SELECT 1 FROM OBCD B
      INNER JOIN OUOM U ON U.UomEntry = B.UomEntry
      WHERE B.ItemCode = W.ItemCode AND U.UomCode = 'Unit'
  )
ORDER BY W.ItemCode");

                while (!rs.EoF)
                {
                    var code = rs.Fields.Item("CodeBars").Value?.ToString() ?? "";
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        results.Add(new SapLegacyCodeBarsRow(
                            ItemCode: rs.Fields.Item("ItemCode").Value?.ToString() ?? "",
                            ItemName: rs.Fields.Item("ItemName").Value?.ToString() ?? "",
                            CodeBars: code));
                    }
                    rs.MoveNext();
                }
            }
            catch (Exception ex) { threadException = ex; }
            finally
            {
                if (rs      != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(rs);
                if (company != null && company.Connected) company.Disconnect();
                if (company != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(company);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null) throw threadException;
        return results;
    }

    // =====================================================
    // NO-BARCODE-ANYWHERE REPORT
    // Returns all active inventory items that have neither
    // OITM.CodeBars nor any OBCD entry (unit or packaging).
    // =====================================================
    public List<SapNoBarcodeRow> ReadItemsWithNoBarcodeAnywhere()
    {
        var results = new List<SapNoBarcodeRow>();
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            Company?   company = null;
            Recordset? rs      = null;
            try
            {
                company = _connection.CreateNewCompany();
                if (company.Connect() != 0)
                {
                    company.GetLastError(out var code, out var msg);
                    throw new Exception($"SAP connect failed {code}: {msg}");
                }

                rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
                rs.DoQuery(@"
SELECT I.ItemCode, I.ItemName
FROM OITM I
WHERE I.frozenFor = 'N'
  AND I.InvntItem = 'Y'
  AND ISNULL(I.CodeBars, '') = ''
  AND NOT EXISTS (
      SELECT 1 FROM OBCD B WHERE B.ItemCode = I.ItemCode
  )
ORDER BY I.ItemCode");

                while (!rs.EoF)
                {
                    results.Add(new SapNoBarcodeRow(
                        ItemCode: rs.Fields.Item("ItemCode").Value?.ToString() ?? "",
                        ItemName: rs.Fields.Item("ItemName").Value?.ToString() ?? ""));
                    rs.MoveNext();
                }
            }
            catch (Exception ex) { threadException = ex; }
            finally
            {
                if (rs      != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(rs);
                if (company != null && company.Connected) company.Disconnect();
                if (company != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(company);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null) throw threadException;
        return results;
    }

    private static int?     ToNullInt(Recordset rs, string col) { var v = rs.Fields.Item(col).Value; return v == null || v is DBNull ? null : Convert.ToInt32(v); }
    private static decimal? ToNullDec(Recordset rs, string col) { var v = rs.Fields.Item(col).Value; return v == null || v is DBNull ? null : Convert.ToDecimal(v); }
}

// ── Result types ─────────────────────────────────────────────────────────────

public record SapItemUomRow(
    int?    UomEntry,
    string  UomCode,
    string? UomName,
    decimal? BaseQty,
    string? BcdCode);

public record SapItemBarcodeRow(
    string  BcdCode,
    int?    UomEntry,
    string? UomCode,
    string? UomName);

public record SapItemBarcodeInfo(
    string ItemCode,
    string ItemName,
    int?   UgpEntry,
    bool   IsInventory,
    bool   IsFrozen,
    List<SapItemUomRow>     UomGroup,
    List<SapItemBarcodeRow> AllBarcodes);

public record SapMissingBarcodeRow(
    string ItemCode,
    string ItemName,
    string WhsCode,
    string Issue);

public record SapLegacyCodeBarsRow(
    string ItemCode,
    string ItemName,
    string CodeBars);

public record SapNoBarcodeRow(
    string ItemCode,
    string ItemName);