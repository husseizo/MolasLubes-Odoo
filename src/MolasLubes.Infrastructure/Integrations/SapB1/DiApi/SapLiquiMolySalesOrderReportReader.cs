#pragma warning disable CA1416

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapLiquiMolySalesOrderReportReader
{
    private readonly IntegrationProfilesOptions _profiles;
    private readonly ILogger<SapLiquiMolySalesOrderReportReader> _logger;

    public SapLiquiMolySalesOrderReportReader(
        IOptions<IntegrationProfilesOptions> profileOptions,
        ILogger<SapLiquiMolySalesOrderReportReader> logger)
    {
        _profiles = profileOptions.Value;
        _logger = logger;
    }

    public SalesOrderLineReportResponse GetSalesOrderLines(
        string profileKey,
        string? brand,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string dateField = "orderDate",
        string lineStatus = "open",
        string fulfillmentStatus = "notDelivered",
        string? warehouse = null,
        string? customerCode = null,
        string? salesPersonCode = null,
        string? search = null,
        int skip = 0,
        int take = 50,
        string sort = "orderDate",
        string sortDirection = "desc")
    {
        var resolvedProfileKey = ResolveProfileKey(profileKey, brand);
        if (!_profiles.Profiles.TryGetValue(resolvedProfileKey, out var profile))
            throw new InvalidOperationException($"Profile '{resolvedProfileKey}' not configured.");

        var isAutoHub = resolvedProfileKey.Equals("AutoHub", StringComparison.OrdinalIgnoreCase);
        var fromSql = dateFrom.HasValue ? FormatDate(dateFrom.Value) : null;
        var toSql = dateTo.HasValue ? FormatDate(dateTo.Value) : null;

        var items = new List<SalesOrderLineReportRow>();
        int totalCount = 0;
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            SapDiApiCriticalSection.Run(() =>
            {
                Company? company = null;
                Recordset? rs = null;

                try
                {
                    company = CreateAndConnect(profile.Sap);
                    rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

                    var sql = BuildSalesOrderLinesSql(
                        isAutoHub, fromSql, toSql, dateField, lineStatus, fulfillmentStatus,
                        warehouse, customerCode, salesPersonCode, search, sort, sortDirection, skip, take);

                    _logger.LogDebug("SapLiquiMolySalesOrderReportReader: executing SO line report | Profile={Profile} | DateField={DateField} | Skip={Skip} | Take={Take}",
                        resolvedProfileKey, dateField, skip, take);

                    rs.DoQuery(sql);

                    while (!rs.EoF)
                    {
                        if (totalCount == 0)
                            totalCount = ReadInt(rs, "TotalCount");

                        items.Add(ReadRow(rs));
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
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw threadException;

        _logger.LogInformation(
            "SapLiquiMolySalesOrderReportReader: SO line report completed | Profile={Profile} | Total={Total} | Returned={Returned}",
            resolvedProfileKey, totalCount, items.Count);

        return new SalesOrderLineReportResponse
        {
            AsOfUtc = DateTime.UtcNow,
            Count = totalCount,
            HasMore = skip + items.Count < totalCount,
            Items = items
        };
    }

    public SalesOrderLineSummaryResponse GetSalesOrderLinesSummary(
        string profileKey,
        string? brand,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string dateField = "orderDate",
        string? warehouse = null,
        string? customerCode = null,
        string? salesPersonCode = null,
        string? search = null)
    {
        var resolvedProfileKey = ResolveProfileKey(profileKey, brand);
        if (!_profiles.Profiles.TryGetValue(resolvedProfileKey, out var profile))
            throw new InvalidOperationException($"Profile '{resolvedProfileKey}' not configured.");

        var isAutoHub = resolvedProfileKey.Equals("AutoHub", StringComparison.OrdinalIgnoreCase);
        var fromSql = dateFrom.HasValue ? FormatDate(dateFrom.Value) : null;
        var toSql = dateTo.HasValue ? FormatDate(dateTo.Value) : null;

        SalesOrderLineSummaryResponse? result = null;
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            SapDiApiCriticalSection.Run(() =>
            {
                Company? company = null;
                Recordset? rs = null;

                try
                {
                    company = CreateAndConnect(profile.Sap);
                    rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

                    var sql = BuildSummaryQuerySql(
                        isAutoHub, fromSql, toSql, dateField,
                        warehouse, customerCode, salesPersonCode, search);

                    rs.DoQuery(sql);

                    if (!rs.EoF)
                    {
                        result = new SalesOrderLineSummaryResponse
                        {
                            AsOfUtc = DateTime.UtcNow,
                            OpenLineCount = ReadInt(rs, "OpenLineCount"),
                            NotDeliveredLineCount = ReadInt(rs, "NotDeliveredLineCount"),
                            PartiallyDeliveredLineCount = ReadInt(rs, "PartiallyDeliveredLineCount"),
                            FullyDeliveredLineCount = ReadInt(rs, "FullyDeliveredLineCount"),
                            CanceledLineCount = ReadInt(rs, "CanceledLineCount"),
                            OrderedQty = ReadDecimal(rs, "OrderedQty"),
                            DeliveredQty = ReadDecimal(rs, "DeliveredQty"),
                            PendingQty = ReadDecimal(rs, "PendingQty"),
                            CanceledQty = ReadDecimal(rs, "CanceledQty")
                        };
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
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw threadException;

        return result ?? new SalesOrderLineSummaryResponse { AsOfUtc = DateTime.UtcNow };
    }

    // ── SQL builders ─────────────────────────────────────────────────────────

    private static string BuildSalesOrderLinesSql(
        bool isAutoHub,
        string? fromSql, string? toSql,
        string dateField, string lineStatus, string fulfillmentStatus,
        string? warehouse, string? customerCode, string? salesPersonCode, string? search,
        string sort, string sortDirection,
        int skip, int take)
    {
        var metaSql = BuildMetadataSql(isAutoHub);
        var dateFilter = BuildDateFilterSql(dateField, fromSql, toSql);
        var lineStatusFilter = BuildLineStatusFilterSql(lineStatus);
        var warehouseFilter = BuildEqFilter("l.WhsCode", warehouse);
        var customerFilter = BuildEqFilter("h.CardCode", customerCode);
        var salesPersonFilter = BuildSalesPersonFilterSql(salesPersonCode);
        var searchFilter = BuildSearchFilterSql(search, isAutoHub);
        var fulfillmentFilter = BuildFulfillmentFilterSql(fulfillmentStatus);
        var sortExpr = BuildSortSql(sort, sortDirection);

        return $@"
WITH DeliveryTotals AS
(
    SELECT
        dl.BaseEntry                                                    AS SoDocEntry,
        dl.BaseLine                                                     AS SoLineNum,
        CONVERT(DECIMAL(19,6), SUM(ISNULL(dl.Quantity, 0)))            AS DeliveredQty,
        MAX(dh.DocEntry)                                                AS LastDeliveryDocEntry,
        MAX(dh.DocDate)                                                 AS LastDeliveryDate
    FROM DLN1 dl
    INNER JOIN ODLN dh ON dh.DocEntry = dl.DocEntry
    WHERE dl.BaseType = 17
      AND ISNULL(dh.CANCELED, 'N') <> 'Y'
    GROUP BY dl.BaseEntry, dl.BaseLine
),
Lines AS
(
    SELECT
        h.DocEntry                                                      AS DocEntry,
        CAST(h.DocNum AS NVARCHAR(50))                                  AS DocNum,
        l.LineNum                                                       AS LineNum,
        h.DocDate                                                       AS DocDate,
        COALESCE(l.ShipDate, h.DocDueDate)                             AS DueDate,
        h.UpdateDate                                                    AS LastModifiedDate,
        CASE
            WHEN ISNULL(h.CANCELED, 'N') = 'Y' THEN 'CANCELLED'
            WHEN h.DocStatus = 'C'              THEN 'CLOSED'
            ELSE 'OPEN'
        END                                                             AS DocumentStatus,
        CASE
            WHEN ISNULL(l.LineStatus, 'O') = 'C' THEN 'CLOSED'
            ELSE 'OPEN'
        END                                                             AS LineStatus,
        CAST(CASE WHEN ISNULL(h.CANCELED, 'N') = 'Y' THEN 1 ELSE 0 END AS BIT) AS IsCanceled,
        CONVERT(DECIMAL(19,6), ISNULL(l.Quantity, 0))                  AS OrderedQty,
        CONVERT(DECIMAL(19,6), ISNULL(dt.DeliveredQty, 0))             AS DeliveredQty,
        CONVERT(DECIMAL(19,6), ISNULL(l.OpenQty, 0))                   AS OpenQty,
        CONVERT(DECIMAL(19,6),
            CASE WHEN ISNULL(l.Quantity, 0) - ISNULL(dt.DeliveredQty, 0) < 0
                 THEN 0
                 ELSE ISNULL(l.Quantity, 0) - ISNULL(dt.DeliveredQty, 0)
            END)                                                        AS PendingQty,
        CONVERT(DECIMAL(19,6),
            CASE WHEN ISNULL(l.LineStatus, 'O') = 'C'
                      AND ISNULL(dt.DeliveredQty, 0) < ISNULL(l.Quantity, 0)
                 THEN ISNULL(l.Quantity, 0) - ISNULL(dt.DeliveredQty, 0)
                 ELSE 0
            END)                                                        AS CanceledQty,
        CASE
            WHEN ISNULL(h.CANCELED, 'N') = 'Y'                         THEN 'canceled'
            WHEN ISNULL(l.LineStatus, 'O') = 'C'
             AND ISNULL(dt.DeliveredQty, 0) = 0                         THEN 'canceled'
            WHEN ISNULL(dt.DeliveredQty, 0) = 0                         THEN 'notDelivered'
            WHEN ISNULL(l.OpenQty, 0) > 0                               THEN 'partiallyDelivered'
            ELSE 'fullyDelivered'
        END                                                             AS FulfillmentStatus,
        h.CardCode                                                      AS CustomerCode,
        h.CardName                                                      AS CustomerName,
        CASE WHEN ISNULL(h.SlpCode, -1) = -1 THEN NULL
             ELSE CAST(h.SlpCode AS NVARCHAR(10))
        END                                                             AS SalesPersonCode,
        COALESCE(NULLIF(sl.SlpName, ''), NULL)                          AS SalesPersonName,
        l.ItemCode                                                      AS ItemCode,
        COALESCE(NULLIF(l.Dscription, ''), i.ItemName)                  AS ItemName,
        l.WhsCode                                                       AS Warehouse,
        wh.WhsName                                                      AS WarehouseName,
        CONVERT(DECIMAL(19,6), ISNULL(l.Price, 0))                      AS UnitPrice,
        CONVERT(DECIMAL(19,6), ISNULL(l.LineTotal, 0))                  AS LineTotal,
        NULLIF(l.unitMsr, '')                                           AS UnitOfMeasure,
        dt.LastDeliveryDocEntry                                         AS LastDeliveryDocEntry,
        CAST(lastDlv.DocNum AS NVARCHAR(50))                            AS LastDeliveryDocNum,
        dt.LastDeliveryDate                                             AS LastDeliveryDate,
        {metaSql}
    FROM ORDR h
    INNER JOIN RDR1 l ON l.DocEntry = h.DocEntry
    LEFT JOIN DeliveryTotals dt ON dt.SoDocEntry = h.DocEntry AND dt.SoLineNum = l.LineNum
    LEFT JOIN ODLN lastDlv ON lastDlv.DocEntry = dt.LastDeliveryDocEntry
    LEFT JOIN OITM i ON i.ItemCode = l.ItemCode
    LEFT JOIN OSLP sl ON sl.SlpCode = h.SlpCode
    LEFT JOIN OWHS wh ON wh.WhsCode = l.WhsCode
    WHERE 1=1
      {dateFilter}
      {lineStatusFilter}
      {warehouseFilter}
      {customerFilter}
      {salesPersonFilter}
      {searchFilter}
)
SELECT
    DocEntry, DocNum, LineNum, DocDate, DueDate, LastModifiedDate,
    DocumentStatus, LineStatus, IsCanceled,
    OrderedQty, DeliveredQty, OpenQty, PendingQty, CanceledQty,
    FulfillmentStatus,
    CustomerCode, CustomerName, SalesPersonCode, SalesPersonName,
    ItemCode, ItemName, Warehouse, WarehouseName,
    UnitPrice, LineTotal, UnitOfMeasure,
    LastDeliveryDocEntry, LastDeliveryDocNum, LastDeliveryDate,
    Brand, ArticleNumber, TanNumber, ProductionPartNumber,
    COUNT(*) OVER () AS TotalCount
FROM Lines
WHERE 1=1
  {fulfillmentFilter}
ORDER BY {sortExpr}
OFFSET {skip} ROWS FETCH NEXT {take} ROWS ONLY";
    }

    private static string BuildSummaryQuerySql(
        bool isAutoHub,
        string? fromSql, string? toSql, string dateField,
        string? warehouse, string? customerCode, string? salesPersonCode, string? search)
    {
        var metaSql = BuildMetadataSql(isAutoHub);
        var dateFilter = BuildDateFilterSql(dateField, fromSql, toSql);
        var warehouseFilter = BuildEqFilter("l.WhsCode", warehouse);
        var customerFilter = BuildEqFilter("h.CardCode", customerCode);
        var salesPersonFilter = BuildSalesPersonFilterSql(salesPersonCode);
        var searchFilter = BuildSearchFilterSql(search, isAutoHub);

        return $@"
WITH DeliveryTotals AS
(
    SELECT
        dl.BaseEntry                                                    AS SoDocEntry,
        dl.BaseLine                                                     AS SoLineNum,
        CONVERT(DECIMAL(19,6), SUM(ISNULL(dl.Quantity, 0)))            AS DeliveredQty,
        MAX(dh.DocEntry)                                                AS LastDeliveryDocEntry,
        MAX(dh.DocDate)                                                 AS LastDeliveryDate
    FROM DLN1 dl
    INNER JOIN ODLN dh ON dh.DocEntry = dl.DocEntry
    WHERE dl.BaseType = 17
      AND ISNULL(dh.CANCELED, 'N') <> 'Y'
    GROUP BY dl.BaseEntry, dl.BaseLine
),
Lines AS
(
    SELECT
        CONVERT(DECIMAL(19,6), ISNULL(l.Quantity, 0))                  AS OrderedQty,
        CONVERT(DECIMAL(19,6), ISNULL(dt.DeliveredQty, 0))             AS DeliveredQty,
        CONVERT(DECIMAL(19,6),
            CASE WHEN ISNULL(l.Quantity,0) - ISNULL(dt.DeliveredQty,0) < 0
                 THEN 0 ELSE ISNULL(l.Quantity,0) - ISNULL(dt.DeliveredQty,0) END) AS PendingQty,
        CONVERT(DECIMAL(19,6),
            CASE WHEN ISNULL(l.LineStatus,'O') = 'C'
                      AND ISNULL(dt.DeliveredQty,0) < ISNULL(l.Quantity,0)
                 THEN ISNULL(l.Quantity,0) - ISNULL(dt.DeliveredQty,0)
                 ELSE 0 END)                                            AS CanceledQty,
        CASE
            WHEN ISNULL(l.LineStatus,'O') = 'C'
             AND ISNULL(dt.DeliveredQty,0) = 0                          THEN 'CLOSED'
            ELSE 'OPEN'
        END                                                             AS LineStatus,
        CASE
            WHEN ISNULL(h.CANCELED,'N') = 'Y'                          THEN 'canceled'
            WHEN ISNULL(l.LineStatus,'O') = 'C'
             AND ISNULL(dt.DeliveredQty,0) = 0                          THEN 'canceled'
            WHEN ISNULL(dt.DeliveredQty,0) = 0                          THEN 'notDelivered'
            WHEN ISNULL(l.OpenQty,0) > 0                                THEN 'partiallyDelivered'
            ELSE 'fullyDelivered'
        END                                                             AS FulfillmentStatus,
        {metaSql}
    FROM ORDR h
    INNER JOIN RDR1 l ON l.DocEntry = h.DocEntry
    LEFT JOIN DeliveryTotals dt ON dt.SoDocEntry = h.DocEntry AND dt.SoLineNum = l.LineNum
    LEFT JOIN OITM i ON i.ItemCode = l.ItemCode
    WHERE 1=1
      {dateFilter}
      AND ISNULL(l.LineStatus,'O') = 'O'
      AND ISNULL(h.CANCELED,'N') <> 'Y'
      {warehouseFilter}
      {customerFilter}
      {salesPersonFilter}
      {searchFilter}
)
SELECT
    COUNT(*)                                                            AS OpenLineCount,
    SUM(CASE WHEN FulfillmentStatus = 'notDelivered'       THEN 1 ELSE 0 END) AS NotDeliveredLineCount,
    SUM(CASE WHEN FulfillmentStatus = 'partiallyDelivered' THEN 1 ELSE 0 END) AS PartiallyDeliveredLineCount,
    SUM(CASE WHEN FulfillmentStatus = 'fullyDelivered'     THEN 1 ELSE 0 END) AS FullyDeliveredLineCount,
    SUM(CASE WHEN FulfillmentStatus = 'canceled'           THEN 1 ELSE 0 END) AS CanceledLineCount,
    ISNULL(SUM(OrderedQty),  0)                                        AS OrderedQty,
    ISNULL(SUM(DeliveredQty),0)                                        AS DeliveredQty,
    ISNULL(SUM(PendingQty),  0)                                        AS PendingQty,
    ISNULL(SUM(CanceledQty), 0)                                        AS CanceledQty
FROM Lines";
    }

    private static string BuildMetadataSql(bool isAutoHub) => isAutoHub
        ? @"COALESCE(NULLIF(i.U_MdlTEST, ''), NULL)          AS Brand,
        COALESCE(NULLIF(i.U_Article_No, ''), l.ItemCode)  AS ArticleNumber,
        NULLIF(i.U_Engine_Code, '')                        AS TanNumber,
        NULLIF(i.U_PT_No_Inproduction, '')                 AS ProductionPartNumber"
        : @"CAST('Liqui Moly' AS NVARCHAR(100))               AS Brand,
        CAST(l.ItemCode AS NVARCHAR(100))                  AS ArticleNumber,
        CAST(NULL AS NVARCHAR(100))                        AS TanNumber,
        CAST(NULL AS NVARCHAR(100))                        AS ProductionPartNumber";

    private static string BuildDateFilterSql(string dateField, string? fromSql, string? toSql)
    {
        if (fromSql == null && toSql == null) return "";

        var from = fromSql != null ? $"'{fromSql}'" : null;
        var to = toSql != null ? $"'{toSql} 23:59:59.997'" : null;

        return dateField.ToLowerInvariant() switch
        {
            "duedate" => BuildRangeClause("COALESCE(l.ShipDate, h.DocDueDate)", from, to),
            "deliverydate" => BuildDeliveryDateExistsClause(fromSql, toSql),
            "lastmodifieddate" => BuildRangeClause("h.UpdateDate", from, to),
            "canceldate" => $"{BuildRangeClause("h.UpdateDate", from, to)} AND ISNULL(h.CANCELED,'N') = 'Y'",
            _ => BuildRangeClause("h.DocDate", from, to) // orderDate default
        };
    }

    private static string BuildRangeClause(string column, string? from, string? to)
    {
        if (from != null && to != null) return $"AND {column} >= {from} AND {column} <= {to}";
        if (from != null) return $"AND {column} >= {from}";
        return $"AND {column} <= {to}";
    }

    private static string BuildDeliveryDateExistsClause(string? fromSql, string? toSql)
    {
        var parts = new List<string>
        {
            "AND EXISTS (SELECT 1 FROM DLN1 dl2",
            "INNER JOIN ODLN dh2 ON dh2.DocEntry = dl2.DocEntry",
            "WHERE dl2.BaseType = 17",
            "  AND dl2.BaseEntry = h.DocEntry",
            "  AND dl2.BaseLine  = l.LineNum",
            "  AND ISNULL(dh2.CANCELED,'N') <> 'Y'"
        };
        if (fromSql != null) parts.Add($"  AND dh2.DocDate >= '{fromSql}'");
        if (toSql != null)   parts.Add($"  AND dh2.DocDate <= '{toSql} 23:59:59.997'");
        parts.Add(")");
        return string.Join("\n      ", parts);
    }

    private static string BuildLineStatusFilterSql(string lineStatus) =>
        lineStatus.ToLowerInvariant() switch
        {
            "open"     => "AND ISNULL(l.LineStatus,'O') = 'O' AND ISNULL(h.CANCELED,'N') <> 'Y'",
            "closed"   => "AND (l.LineStatus = 'C' OR h.DocStatus = 'C')",
            "canceled" => "AND ISNULL(h.CANCELED,'N') = 'Y'",
            _          => "" // all
        };

    private static string BuildFulfillmentFilterSql(string fulfillmentStatus) =>
        fulfillmentStatus.ToLowerInvariant() switch
        {
            "notdelivered"       => "AND FulfillmentStatus = 'notDelivered'",
            "partiallydelivered" => "AND FulfillmentStatus = 'partiallyDelivered'",
            "fullydelivered"     => "AND FulfillmentStatus = 'fullyDelivered'",
            "open"               => "AND FulfillmentStatus IN ('notDelivered','partiallyDelivered')",
            "canceled"           => "AND FulfillmentStatus = 'canceled'",
            _                    => "" // all
        };

    private static string BuildEqFilter(string column, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var safe = value.Trim().Replace("'", "''");
        return $"AND {column} = '{safe}'";
    }

    private static string BuildSalesPersonFilterSql(string? salesPersonCode)
    {
        if (string.IsNullOrWhiteSpace(salesPersonCode)) return "";
        var safe = salesPersonCode.Trim().Replace("'", "''");
        return $"AND CAST(h.SlpCode AS NVARCHAR(10)) = '{safe}'";
    }

    private static string BuildSearchFilterSql(string? search, bool isAutoHub)
    {
        if (string.IsNullOrWhiteSpace(search)) return "";
        var safe = search.Trim().Replace("'", "''");
        var extra = isAutoHub
            ? $"OR i.U_Article_No LIKE '%{safe}%' OR i.U_Engine_Code LIKE '%{safe}%'"
            : "";
        return $@"AND (
        l.ItemCode  LIKE '%{safe}%'
        OR i.ItemName   LIKE '%{safe}%'
        OR h.CardCode   LIKE '%{safe}%'
        OR h.CardName   LIKE '%{safe}%'
        {extra}
    )";
    }

    private static string BuildSortSql(string sort, string sortDirection)
    {
        var dir = sortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        var col = sort.ToLowerInvariant() switch
        {
            "duedate"      => "DueDate",
            "pendingqty"   => "PendingQty",
            "customername" => "CustomerName",
            "itemcode"     => "ItemCode",
            _              => "DocDate"
        };
        return $"{col} {dir}, DocEntry {dir}, LineNum ASC";
    }

    // ── Row reader ────────────────────────────────────────────────────────────

    private static SalesOrderLineReportRow ReadRow(Recordset rs) => new()
    {
        DocEntry = ReadInt(rs, "DocEntry"),
        DocNum = ReadString(rs, "DocNum") ?? "",
        LineNum = ReadInt(rs, "LineNum"),
        DocDate = ReadDate(rs, "DocDate"),
        DueDate = ReadDate(rs, "DueDate"),
        LastModifiedDate = ReadDate(rs, "LastModifiedDate"),
        DocumentStatus = ReadString(rs, "DocumentStatus"),
        LineStatus = ReadString(rs, "LineStatus"),
        IsCanceled = ReadBool(rs, "IsCanceled"),
        FulfillmentStatus = ReadString(rs, "FulfillmentStatus") ?? "notDelivered",
        CustomerCode = ReadString(rs, "CustomerCode"),
        CustomerName = ReadString(rs, "CustomerName"),
        SalesPersonCode = ReadString(rs, "SalesPersonCode"),
        SalesPersonName = ReadString(rs, "SalesPersonName"),
        ItemCode = ReadString(rs, "ItemCode") ?? "",
        ItemName = ReadString(rs, "ItemName"),
        Brand = ReadString(rs, "Brand"),
        ArticleNumber = ReadString(rs, "ArticleNumber"),
        TanNumber = ReadString(rs, "TanNumber"),
        ProductionPartNumber = ReadString(rs, "ProductionPartNumber"),
        Warehouse = ReadString(rs, "Warehouse"),
        WarehouseName = ReadString(rs, "WarehouseName"),
        UnitOfMeasure = ReadString(rs, "UnitOfMeasure"),
        UnitPrice = ReadDecimal(rs, "UnitPrice"),
        LineTotal = ReadDecimal(rs, "LineTotal"),
        OrderedQty = ReadDecimal(rs, "OrderedQty"),
        DeliveredQty = ReadDecimal(rs, "DeliveredQty"),
        OpenQty = ReadDecimal(rs, "OpenQty"),
        CanceledQty = ReadDecimal(rs, "CanceledQty"),
        PendingQty = ReadDecimal(rs, "PendingQty"),
        LastDeliveryDocEntry = ReadNullableInt(rs, "LastDeliveryDocEntry"),
        LastDeliveryDocNum = ReadString(rs, "LastDeliveryDocNum"),
        LastDeliveryDate = ReadDate(rs, "LastDeliveryDate")
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ResolveProfileKey(string profileKey, string? brand)
    {
        if (!string.IsNullOrWhiteSpace(brand))
        {
            return brand.Trim().Replace(" ", "").Replace("-", "").ToUpperInvariant() switch
            {
                "AUTOHUB" => "AutoHub",
                "LIQUIMOLY" => "MolasLubes",
                _ => profileKey
            };
        }
        return profileKey;
    }

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd");

    private static Company CreateAndConnect(SapSettings sap)
    {
        var company = new Company
        {
            Server = sap.Server,
            CompanyDB = sap.CompanyDB,
            UserName = sap.UserName,
            Password = sap.Password,
            DbServerType = Enum.Parse<BoDataServerTypes>($"dst_{sap.DbServerType}"),
            language = BoSuppLangs.ln_English,
            UseTrusted = false,
            LicenseServer = sap.LicenseServer,
            SLDServer = sap.SLDServer
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
        try { if (company.Connected) company.Disconnect(); } catch { }
        Marshal.ReleaseComObject(company);
    }

    private static string? ReadString(Recordset rs, string field)
    {
        var v = rs.Fields.Item(field).Value?.ToString();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static int ReadInt(Recordset rs, string field) =>
        Convert.ToInt32(rs.Fields.Item(field).Value ?? 0);

    private static int? ReadNullableInt(Recordset rs, string field)
    {
        var v = rs.Fields.Item(field).Value;
        return v == null || v is DBNull ? null : Convert.ToInt32(v);
    }

    private static decimal ReadDecimal(Recordset rs, string field) =>
        Convert.ToDecimal(rs.Fields.Item(field).Value ?? 0m);

    private static bool ReadBool(Recordset rs, string field)
    {
        var v = rs.Fields.Item(field).Value;
        if (v == null || v is DBNull) return false;
        return Convert.ToBoolean(v);
    }

    private static DateTime? ReadDate(Recordset rs, string field)
    {
        var v = rs.Fields.Item(field).Value;
        if (v == null || v is DBNull) return null;
        if (v is DateTime dt) return dt;
        return DateTime.TryParse(v.ToString(), out DateTime parsed) ? parsed : null;
    }
}

// ── Response types ────────────────────────────────────────────────────────────

public class SalesOrderLineReportResponse
{
    public DateTime AsOfUtc { get; init; }
    public int Count { get; init; }
    public bool HasMore { get; init; }
    public List<SalesOrderLineReportRow> Items { get; init; } = new();
}

public class SalesOrderLineReportRow
{
    public int DocEntry { get; init; }
    public string DocNum { get; init; } = string.Empty;
    public int LineNum { get; init; }
    public DateTime? DocDate { get; init; }
    public DateTime? DueDate { get; init; }
    public DateTime? LastModifiedDate { get; init; }
    public string? DocumentStatus { get; init; }
    public string? LineStatus { get; init; }
    public bool IsCanceled { get; init; }
    public DateTime? CanceledAt => null; // SAP has no direct cancel timestamp; clients infer from LastModifiedDate when IsCanceled=true
    public string? CancelReason => null;
    public string? FulfillmentStatus { get; init; }
    public string? CustomerCode { get; init; }
    public string? CustomerName { get; init; }
    public string? SalesPersonCode { get; init; }
    public string? SalesPersonName { get; init; }
    public string ItemCode { get; init; } = string.Empty;
    public string SapItemCode => ItemCode;
    public string? ItemName { get; init; }
    public string? Brand { get; init; }
    public string? ArticleNumber { get; init; }
    public string? TanNumber { get; init; }
    public string? ProductionPartNumber { get; init; }
    public string? Warehouse { get; init; }
    public string? WarehouseName { get; init; }
    public string? UnitOfMeasure { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal LineTotal { get; init; }
    public decimal OrderedQty { get; init; }
    public decimal DeliveredQty { get; init; }
    public decimal OpenQty { get; init; }
    public decimal CanceledQty { get; init; }
    public decimal PendingQty { get; init; }
    public int? LastDeliveryDocEntry { get; init; }
    public string? LastDeliveryDocNum { get; init; }
    public DateTime? LastDeliveryDate { get; init; }
}

public class SalesOrderLineSummaryResponse
{
    public DateTime AsOfUtc { get; init; }
    public int OpenLineCount { get; init; }
    public int NotDeliveredLineCount { get; init; }
    public int PartiallyDeliveredLineCount { get; init; }
    public int FullyDeliveredLineCount { get; init; }
    public int CanceledLineCount { get; init; }
    public decimal OrderedQty { get; init; }
    public decimal DeliveredQty { get; init; }
    public decimal PendingQty { get; init; }
    public decimal CanceledQty { get; init; }
}
