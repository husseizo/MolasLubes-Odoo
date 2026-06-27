#pragma warning disable CA1416

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using MolasLubes.Infrastructure.Persistence;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Provides near-live inventory stock snapshots and movement timeline for Liqui Moly mobile/admin views.
/// Data source is SAP B1 (OITW/OITM/OWHS + document line tables), with an in-memory versioned snapshot.
/// </summary>
public class SapLiquiMolyInventoryReader
{
    private const decimal DefaultLowStockThreshold = 5m;
    private const decimal DefaultOutOfStockThreshold = 0m;

    private readonly IntegrationProfilesOptions _profiles;
    private readonly MolasCacheDbContext _cacheDb;
    private readonly ILogger<SapLiquiMolyInventoryReader> _logger;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(10);
    private readonly TimeSpan _deliveryAggregateRefreshInterval = TimeSpan.FromSeconds(45);
    private long _versionCounter = 0;

    private readonly ConcurrentDictionary<string, SnapshotState> _states =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DeliveryAggregateSnapshotState> _deliveryAggregateStates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _deliveryAggregateLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public SapLiquiMolyInventoryReader(
        IOptions<IntegrationProfilesOptions> profileOptions,
        MolasCacheDbContext cacheDb,
        ILogger<SapLiquiMolyInventoryReader> logger)
    {
        _profiles = profileOptions.Value;
        _cacheDb = cacheDb;
        _logger = logger;
    }

    public InventoryStockSnapshotResponse GetStock(
        string profileKey,
        string? brand,
        string? search,
        string? warehouseCode,
        int skip,
        int take,
        bool includeZero,
        bool onlyLiquiMoly)
    {
        var scope = ResolveScope(profileKey, brand, onlyLiquiMoly);
        var state = EnsureSnapshot(scope, includeZero);

        IEnumerable<InventoryStockRow> query = state.Rows;
        query = ApplyFilters(query, search, warehouseCode);

        var total = query.Count();
        var rows = query
            .OrderBy(x => x.ItemCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.WarehouseCode, StringComparer.OrdinalIgnoreCase)
            .Skip(skip)
            .Take(take)
            .ToList();

        return new InventoryStockSnapshotResponse
        {
            AsOfUtc = state.AsOfUtc,
            Version = state.Version,
            Total = total,
            Rows = rows
        };
    }

    public InventoryStockSnapshotResponse GetStockForItem(
        string profileKey,
        string? brand,
        string itemCode,
        string? warehouseCode)
    {
        var scope = ResolveScope(profileKey, brand, onlyLiquiMoly: true);
        var state = EnsureSnapshot(scope, includeZero: true);

        IEnumerable<InventoryStockRow> query = state.Rows
            .Where(x => x.ItemCode.Equals(itemCode, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(warehouseCode))
            query = query.Where(x => x.WarehouseCode.Equals(warehouseCode, StringComparison.OrdinalIgnoreCase));

        var rows = query
            .OrderBy(x => x.WarehouseCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new InventoryStockSnapshotResponse
        {
            AsOfUtc = state.AsOfUtc,
            Version = state.Version,
            Total = rows.Count,
            Rows = rows
        };
    }

    public InventoryStockChangesResponse GetChanges(
        string profileKey,
        string? brand,
        long sinceVersion,
        string? search,
        string? warehouseCode,
        bool includeZero,
        bool onlyLiquiMoly)
    {
        var scope = ResolveScope(profileKey, brand, onlyLiquiMoly);
        var state = EnsureSnapshot(scope, includeZero);

        var oldestVersion = state.History.Count == 0
            ? state.Version
            : state.History[0].Version;

        var resetRequired = sinceVersion < oldestVersion;
        List<InventoryStockRow> changed;

        if (resetRequired)
        {
            changed = ApplyFilters(state.Rows, search, warehouseCode).ToList();
        }
        else
        {
            changed = state.History
                .Where(h => h.Version > sinceVersion)
                .SelectMany(h => h.Rows)
                .GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .ToList();

            changed = ApplyFilters(changed, search, warehouseCode).ToList();
        }

        return new InventoryStockChangesResponse
        {
            AsOfUtc = state.AsOfUtc,
            Version = state.Version,
            SinceVersion = sinceVersion,
            ResetRequired = resetRequired,
            Rows = changed
        };
    }

    public InventoryStockSummaryResponse GetSummary(
        string profileKey,
        string? brand,
        string? warehouseCode,
        bool includeZero,
        bool onlyLiquiMoly)
    {
        var scope = ResolveScope(profileKey, brand, onlyLiquiMoly);
        var state = EnsureSnapshot(scope, includeZero);

        var rows = state.Rows.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(warehouseCode))
            rows = rows.Where(x => x.WarehouseCode.Equals(warehouseCode, StringComparison.OrdinalIgnoreCase));

        var materializedRows = rows.ToList();
        var totalOnHand = materializedRows.Sum(x => x.OnHand);
        var totalCommitted = materializedRows.Sum(x => x.Committed);
        var totalOrdered = materializedRows.Sum(x => x.Ordered);
        var totalNetAvailable = materializedRows.Sum(x => x.Available);

        return new InventoryStockSummaryResponse
        {
            AsOfUtc = state.AsOfUtc,
            Version = state.Version,
            ItemWarehouseCount = materializedRows.Count,
            ItemCount = materializedRows.Select(x => x.ItemCode).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            WarehouseCount = materializedRows.Select(x => x.WarehouseCode).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            TotalOnHand = totalOnHand,
            TotalCommitted = totalCommitted,
            TotalOrdered = totalOrdered,
            // Keep TotalAvailable aligned with the frontend's "current stock" card expectation.
            TotalAvailable = totalOnHand,
            TotalNetAvailable = totalNetAvailable
        };
    }

    public InventoryDeliveryAggregateResponse GetDeliveryAggregates(
        string profileKey,
        string? brand,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? warehouseCode,
        string? search,
        int skip,
        int take)
    {
        var scope = ResolveScope(profileKey, brand, onlyLiquiMoly: true);
        var range = ResolveRequiredDateRange(dateFrom, dateTo, defaultToBusinessToday: true);
        var state = EnsureDeliveryAggregateSnapshot(scope, range.From, range.To);

        IEnumerable<InventoryDeliveryAggregateRow> query = state.Rows;
        query = ApplyDeliveryFilters(query, search, warehouseCode);

        var total = query.Count();
        var rows = query
            .OrderByDescending(x => x.LastDeliveredAt ?? DateTime.MinValue)
            .ThenByDescending(x => x.LastDeliveryDocEntry)
            .ThenBy(x => x.ItemCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Warehouse, StringComparer.OrdinalIgnoreCase)
            .Skip(skip)
            .Take(take)
            .ToList();

        return new InventoryDeliveryAggregateResponse
        {
            DateFrom = range.From,
            DateTo = range.To,
            AsOfUtc = state.AsOfUtc,
            Version = state.Version,
            Total = total,
            Rows = rows
        };
    }

    public InventoryTodayDeliveryResponse GetTodayDeliveries(string profileKey, string? brand)
    {
        var scope = ResolveScope(profileKey, brand, onlyLiquiMoly: true);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var state = EnsureDeliveryAggregateSnapshot(scope, today, today);

        var byItem = state.Rows
            .GroupBy(r => r.ItemCode, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                return new InventoryTodayDeliveryItem
                {
                    ItemCode = g.Key,
                    ItemName = first.ItemName,
                    Brand = first.Brand,
                    ArticleNumber = first.ArticleNumber,
                    TanNumber = first.TanNumber,
                    DeliveredQty = g.Sum(r => r.DeliveredQty),
                    DeliveryCount = g.Sum(r => r.DeliveryCount),
                    LastDeliveredAt = g.Max(r => r.LastDeliveredAt),
                    LastDeliveryDocEntry = g.OrderByDescending(r => r.LastDeliveryDocEntry ?? 0).First().LastDeliveryDocEntry,
                    LastDeliveryDocNum = g.OrderByDescending(r => r.LastDeliveryDocEntry ?? 0).First().LastDeliveryDocNum,
                    Warehouses = g.Select(r => r.Warehouse).Where(w => !string.IsNullOrWhiteSpace(w)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                };
            })
            .OrderByDescending(x => x.LastDeliveredAt ?? DateTime.MinValue)
            .ThenByDescending(x => x.LastDeliveryDocEntry ?? 0)
            .ThenBy(x => x.ItemCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new InventoryTodayDeliveryResponse
        {
            AsOfUtc = state.AsOfUtc,
            Version = state.Version,
            Date = today,
            Total = byItem.Count,
            Items = byItem
        };
    }

    public InventoryMovementResponse GetMovements(
        string profileKey,
        string? brand,
        string itemCode,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? warehouseCode,
        string? movementTypes,
        int skip,
        int take,
        string? salesPersonCode = null)
    {
        var scope = ResolveScope(profileKey, brand, onlyLiquiMoly: true);

        if (!_profiles.Profiles.TryGetValue(scope.ProfileKey, out var profile))
            throw new InvalidOperationException($"Profile '{profileKey}' not configured.");

        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 1000);

        var resolvedRange = ResolveOptionalDateRange(dateFrom, dateTo);
        var normalizedMovementTypes = NormalizeMovementTypes(movementTypes);
        var salesPersonClause = !string.IsNullOrWhiteSpace(salesPersonCode)
            ? $" AND SalesPersonCode = '{salesPersonCode.Replace("'", "''")}'"
            : "";

        var filteredRows = new List<InventoryMovementRow>();
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

                    var safeItem = itemCode.Replace("'", "''");
                    var safeWhs = warehouseCode?.Replace("'", "''");
                    var whClause = string.IsNullOrWhiteSpace(safeWhs)
                        ? ""
                        : $" AND MovementWarehouse = '{safeWhs}'";
                    var dateClause = BuildOptionalDateClause("MovementDate", resolvedRange);
                    var movementTypeClause = BuildMovementTypeClause(normalizedMovementTypes);

                    var sql = $@"
SELECT
    *
FROM
(
    SELECT
        'SO' AS SourceType,
        h.DocEntry AS DocEntry,
        CAST(h.DocNum AS NVARCHAR(50)) AS DocNum,
        h.DocDate AS MovementDate,
        l.LineNum AS LineNum,
        l.ItemCode AS ItemCode,
        COALESCE(NULLIF(l.Dscription, ''), i.ItemName) AS ItemName,
        l.WhsCode AS MovementWarehouse,
        CAST(NULL AS NVARCHAR(8)) AS SourceWarehouse,
        CAST(NULL AS NVARCHAR(8)) AS TargetWarehouse,
        CONVERT(DECIMAL(19, 6), ISNULL(l.Quantity, 0)) AS Quantity,
        'OUT' AS Direction,
        CASE
            WHEN ISNULL(h.CANCELED, 'N') = 'Y' THEN 'CANCELLED'
            WHEN h.DocStatus = 'C' THEN 'CLOSED'
            ELSE 'OPEN'
        END AS DocumentStatus,
        h.CardCode AS PartnerCode,
        h.CardName AS PartnerName,
        h.CardCode AS CustomerCode,
        h.CardName AS CustomerName,
        CAST(NULL AS NVARCHAR(50)) AS VendorCode,
        CAST(NULL AS NVARCHAR(100)) AS VendorName,
        CAST(NULL AS NVARCHAR(10)) AS LinkedDocType,
        CAST(NULL AS INT) AS LinkedDocEntry,
        CAST(NULL AS NVARCHAR(50)) AS LinkedDocNum,
        CAST(NULL AS INT) AS LinkedLineNum,
        CASE WHEN ISNULL(h.SlpCode, -1) = -1 THEN NULL ELSE CAST(h.SlpCode AS NVARCHAR(10)) END AS SalesPersonCode,
        COALESCE(NULLIF(slp.SlpName, ''), NULL) AS SalesPersonName
    FROM ORDR h
    INNER JOIN RDR1 l ON h.DocEntry = l.DocEntry
    LEFT JOIN OITM i ON i.ItemCode = l.ItemCode
    LEFT JOIN OSLP slp ON slp.SlpCode = h.SlpCode
    WHERE l.ItemCode = '{safeItem}'

    UNION ALL

    SELECT
        'DLV' AS SourceType,
        h.DocEntry AS DocEntry,
        CAST(h.DocNum AS NVARCHAR(50)) AS DocNum,
        h.DocDate AS MovementDate,
        l.LineNum AS LineNum,
        l.ItemCode AS ItemCode,
        COALESCE(NULLIF(l.Dscription, ''), i.ItemName) AS ItemName,
        l.WhsCode AS MovementWarehouse,
        CAST(NULL AS NVARCHAR(8)) AS SourceWarehouse,
        CAST(NULL AS NVARCHAR(8)) AS TargetWarehouse,
        CONVERT(DECIMAL(19, 6), ISNULL(l.Quantity, 0)) AS Quantity,
        'OUT' AS Direction,
        CASE
            WHEN ISNULL(h.CANCELED, 'N') = 'Y' THEN 'CANCELLED'
            WHEN h.DocStatus = 'C' THEN 'CLOSED'
            ELSE 'OPEN'
        END AS DocumentStatus,
        h.CardCode AS PartnerCode,
        h.CardName AS PartnerName,
        h.CardCode AS CustomerCode,
        h.CardName AS CustomerName,
        CAST(NULL AS NVARCHAR(50)) AS VendorCode,
        CAST(NULL AS NVARCHAR(100)) AS VendorName,
        CAST(NULL AS NVARCHAR(10)) AS LinkedDocType,
        CAST(NULL AS INT) AS LinkedDocEntry,
        CAST(NULL AS NVARCHAR(50)) AS LinkedDocNum,
        CAST(NULL AS INT) AS LinkedLineNum,
        CASE WHEN ISNULL(h.SlpCode, -1) = -1 THEN NULL ELSE CAST(h.SlpCode AS NVARCHAR(10)) END AS SalesPersonCode,
        COALESCE(NULLIF(slp.SlpName, ''), NULL) AS SalesPersonName
    FROM ODLN h
    INNER JOIN DLN1 l ON h.DocEntry = l.DocEntry
    LEFT JOIN OITM i ON i.ItemCode = l.ItemCode
    LEFT JOIN OSLP slp ON slp.SlpCode = h.SlpCode
    WHERE l.ItemCode = '{safeItem}'

    UNION ALL

    SELECT
        'TRQ' AS SourceType,
        h.DocEntry AS DocEntry,
        CAST(h.DocNum AS NVARCHAR(50)) AS DocNum,
        h.DocDate AS MovementDate,
        l.LineNum AS LineNum,
        l.ItemCode AS ItemCode,
        COALESCE(NULLIF(l.Dscription, ''), i.ItemName) AS ItemName,
        l.WhsCode AS MovementWarehouse,
        l.FromWhsCod AS SourceWarehouse,
        l.WhsCode AS TargetWarehouse,
        CONVERT(DECIMAL(19, 6), ISNULL(l.Quantity, 0)) AS Quantity,
        'IN' AS Direction,
        CASE
            WHEN ISNULL(h.CANCELED, 'N') = 'Y' THEN 'CANCELLED'
            WHEN h.DocStatus = 'C' THEN 'CLOSED'
            ELSE 'OPEN'
        END AS DocumentStatus,
        CAST(NULL AS NVARCHAR(50)) AS PartnerCode,
        CAST(NULL AS NVARCHAR(100)) AS PartnerName,
        CAST(NULL AS NVARCHAR(50)) AS CustomerCode,
        CAST(NULL AS NVARCHAR(100)) AS CustomerName,
        CAST(NULL AS NVARCHAR(50)) AS VendorCode,
        CAST(NULL AS NVARCHAR(100)) AS VendorName,
        CAST(NULL AS NVARCHAR(10)) AS LinkedDocType,
        CAST(NULL AS INT) AS LinkedDocEntry,
        CAST(NULL AS NVARCHAR(50)) AS LinkedDocNum,
        CAST(NULL AS INT) AS LinkedLineNum,
        CAST(NULL AS NVARCHAR(10)) AS SalesPersonCode,
        CAST(NULL AS NVARCHAR(100)) AS SalesPersonName
    FROM OWTQ h
    INNER JOIN WTQ1 l ON h.DocEntry = l.DocEntry
    LEFT JOIN OITM i ON i.ItemCode = l.ItemCode
    WHERE l.ItemCode = '{safeItem}'

    UNION ALL

    SELECT
        'TRF' AS SourceType,
        h.DocEntry AS DocEntry,
        CAST(h.DocNum AS NVARCHAR(50)) AS DocNum,
        h.DocDate AS MovementDate,
        l.LineNum AS LineNum,
        l.ItemCode AS ItemCode,
        COALESCE(NULLIF(l.Dscription, ''), i.ItemName) AS ItemName,
        l.WhsCode AS MovementWarehouse,
        l.FromWhsCod AS SourceWarehouse,
        l.WhsCode AS TargetWarehouse,
        CONVERT(DECIMAL(19, 6), ISNULL(l.Quantity, 0)) AS Quantity,
        'IN' AS Direction,
        CASE
            WHEN ISNULL(h.CANCELED, 'N') = 'Y' THEN 'CANCELLED'
            WHEN h.DocStatus = 'C' THEN 'CLOSED'
            ELSE 'OPEN'
        END AS DocumentStatus,
        CAST(NULL AS NVARCHAR(50)) AS PartnerCode,
        CAST(NULL AS NVARCHAR(100)) AS PartnerName,
        CAST(NULL AS NVARCHAR(50)) AS CustomerCode,
        CAST(NULL AS NVARCHAR(100)) AS CustomerName,
        CAST(NULL AS NVARCHAR(50)) AS VendorCode,
        CAST(NULL AS NVARCHAR(100)) AS VendorName,
        CAST(NULL AS NVARCHAR(10)) AS LinkedDocType,
        CAST(NULL AS INT) AS LinkedDocEntry,
        CAST(NULL AS NVARCHAR(50)) AS LinkedDocNum,
        CAST(NULL AS INT) AS LinkedLineNum,
        CAST(NULL AS NVARCHAR(10)) AS SalesPersonCode,
        CAST(NULL AS NVARCHAR(100)) AS SalesPersonName
    FROM OWTR h
    INNER JOIN WTR1 l ON h.DocEntry = l.DocEntry
    LEFT JOIN OITM i ON i.ItemCode = l.ItemCode
    WHERE l.ItemCode = '{safeItem}'

    UNION ALL

    SELECT
        'GR' AS SourceType,
        h.DocEntry AS DocEntry,
        CAST(h.DocNum AS NVARCHAR(50)) AS DocNum,
        h.DocDate AS MovementDate,
        l.LineNum AS LineNum,
        l.ItemCode AS ItemCode,
        COALESCE(NULLIF(l.Dscription, ''), i.ItemName) AS ItemName,
        l.WhsCode AS MovementWarehouse,
        CAST(NULL AS NVARCHAR(8)) AS SourceWarehouse,
        CAST(NULL AS NVARCHAR(8)) AS TargetWarehouse,
        CONVERT(DECIMAL(19, 6), ISNULL(l.Quantity, 0)) AS Quantity,
        'IN' AS Direction,
        CASE
            WHEN ISNULL(h.CANCELED, 'N') = 'Y' THEN 'CANCELLED'
            ELSE 'POSTED'
        END AS DocumentStatus,
        CAST(NULL AS NVARCHAR(50)) AS PartnerCode,
        CAST(NULL AS NVARCHAR(100)) AS PartnerName,
        CAST(NULL AS NVARCHAR(50)) AS CustomerCode,
        CAST(NULL AS NVARCHAR(100)) AS CustomerName,
        CAST(NULL AS NVARCHAR(50)) AS VendorCode,
        CAST(NULL AS NVARCHAR(100)) AS VendorName,
        CAST(NULL AS NVARCHAR(10)) AS LinkedDocType,
        CAST(NULL AS INT) AS LinkedDocEntry,
        CAST(NULL AS NVARCHAR(50)) AS LinkedDocNum,
        CAST(NULL AS INT) AS LinkedLineNum,
        CAST(NULL AS NVARCHAR(10)) AS SalesPersonCode,
        CAST(NULL AS NVARCHAR(100)) AS SalesPersonName
    FROM OIGN h
    INNER JOIN IGN1 l ON h.DocEntry = l.DocEntry
    LEFT JOIN OITM i ON i.ItemCode = l.ItemCode
    WHERE l.ItemCode = '{safeItem}'

    UNION ALL

    SELECT
        'GI' AS SourceType,
        h.DocEntry AS DocEntry,
        CAST(h.DocNum AS NVARCHAR(50)) AS DocNum,
        h.DocDate AS MovementDate,
        l.LineNum AS LineNum,
        l.ItemCode AS ItemCode,
        COALESCE(NULLIF(l.Dscription, ''), i.ItemName) AS ItemName,
        l.WhsCode AS MovementWarehouse,
        CAST(NULL AS NVARCHAR(8)) AS SourceWarehouse,
        CAST(NULL AS NVARCHAR(8)) AS TargetWarehouse,
        CONVERT(DECIMAL(19, 6), ISNULL(l.Quantity, 0)) AS Quantity,
        'OUT' AS Direction,
        CASE
            WHEN ISNULL(h.CANCELED, 'N') = 'Y' THEN 'CANCELLED'
            ELSE 'POSTED'
        END AS DocumentStatus,
        CAST(NULL AS NVARCHAR(50)) AS PartnerCode,
        CAST(NULL AS NVARCHAR(100)) AS PartnerName,
        CAST(NULL AS NVARCHAR(50)) AS CustomerCode,
        CAST(NULL AS NVARCHAR(100)) AS CustomerName,
        CAST(NULL AS NVARCHAR(50)) AS VendorCode,
        CAST(NULL AS NVARCHAR(100)) AS VendorName,
        CAST(NULL AS NVARCHAR(10)) AS LinkedDocType,
        CAST(NULL AS INT) AS LinkedDocEntry,
        CAST(NULL AS NVARCHAR(50)) AS LinkedDocNum,
        CAST(NULL AS INT) AS LinkedLineNum,
        CAST(NULL AS NVARCHAR(10)) AS SalesPersonCode,
        CAST(NULL AS NVARCHAR(100)) AS SalesPersonName
    FROM OIGE h
    INNER JOIN IGE1 l ON h.DocEntry = l.DocEntry
    LEFT JOIN OITM i ON i.ItemCode = l.ItemCode
    WHERE l.ItemCode = '{safeItem}'

    UNION ALL

    SELECT
        'INC' AS SourceType,
        h.DocEntry AS DocEntry,
        CAST(h.DocNum AS NVARCHAR(50)) AS DocNum,
        COALESCE(l.CountDate, h.CountDate, h.PostDate, h.CreateDate) AS MovementDate,
        l.LineNum AS LineNum,
        l.ItemCode AS ItemCode,
        COALESCE(NULLIF(l.ItemDesc, ''), i.ItemName) AS ItemName,
        l.WhsCode AS MovementWarehouse,
        CAST(NULL AS NVARCHAR(8)) AS SourceWarehouse,
        CAST(NULL AS NVARCHAR(8)) AS TargetWarehouse,
        CONVERT(DECIMAL(19, 6), ISNULL(l.CountQty, 0)) AS Quantity,
        CASE
            WHEN ISNULL(l.Difference, 0) > 0 THEN 'IN'
            WHEN ISNULL(l.Difference, 0) < 0 THEN 'OUT'
            ELSE 'COUNT'
        END AS Direction,
        CASE
            WHEN h.Status = 'C' THEN 'CLOSED'
            WHEN h.Status = 'O' THEN 'OPEN'
            ELSE ISNULL(NULLIF(h.Status, ''), 'POSTED')
        END AS DocumentStatus,
        CAST(NULL AS NVARCHAR(50)) AS PartnerCode,
        CAST(NULL AS NVARCHAR(100)) AS PartnerName,
        CAST(NULL AS NVARCHAR(50)) AS CustomerCode,
        CAST(NULL AS NVARCHAR(100)) AS CustomerName,
        CAST(NULL AS NVARCHAR(50)) AS VendorCode,
        CAST(NULL AS NVARCHAR(100)) AS VendorName,
        CASE WHEN l.TargetType = 10000071 AND l.TargetEntr IS NOT NULL THEN 'IP' ELSE NULL END AS LinkedDocType,
        TRY_CONVERT(INT, l.TargetEntr) AS LinkedDocEntry,
        CAST(post.DocNum AS NVARCHAR(50)) AS LinkedDocNum,
        TRY_CONVERT(INT, l.TargetLine) AS LinkedLineNum,
        CAST(NULL AS NVARCHAR(10)) AS SalesPersonCode,
        CAST(NULL AS NVARCHAR(100)) AS SalesPersonName
    FROM OINC h
    INNER JOIN INC1 l ON h.DocEntry = l.DocEntry
    LEFT JOIN OITM i ON i.ItemCode = l.ItemCode
    LEFT JOIN OIQR post ON post.DocEntry = l.TargetEntr
    WHERE l.ItemCode = '{safeItem}'

    UNION ALL

    SELECT
        'IP' AS SourceType,
        h.DocEntry AS DocEntry,
        CAST(h.DocNum AS NVARCHAR(50)) AS DocNum,
        COALESCE(h.DocDate, h.CountDate, h.CreateDate) AS MovementDate,
        l.DocLineNum AS LineNum,
        l.ItemCode AS ItemCode,
        COALESCE(NULLIF(l.ItemName, ''), i.ItemName) AS ItemName,
        l.WhsCode AS MovementWarehouse,
        CASE WHEN ISNULL(l.Quantity, 0) < 0 THEN l.WhsCode ELSE CAST(NULL AS NVARCHAR(8)) END AS SourceWarehouse,
        CASE WHEN ISNULL(l.Quantity, 0) > 0 THEN l.WhsCode ELSE CAST(NULL AS NVARCHAR(8)) END AS TargetWarehouse,
        CONVERT(DECIMAL(19, 6), ABS(ISNULL(l.Quantity, 0))) AS Quantity,
        CASE
            WHEN ISNULL(l.Quantity, 0) > 0 THEN 'IN'
            WHEN ISNULL(l.Quantity, 0) < 0 THEN 'OUT'
            ELSE 'COUNT'
        END AS Direction,
        CASE
            WHEN h.Status = 'C' THEN 'CLOSED'
            WHEN h.Status = 'O' THEN 'OPEN'
            ELSE ISNULL(NULLIF(h.Status, ''), 'POSTED')
        END AS DocumentStatus,
        CAST(NULL AS NVARCHAR(50)) AS PartnerCode,
        CAST(NULL AS NVARCHAR(100)) AS PartnerName,
        CAST(NULL AS NVARCHAR(50)) AS CustomerCode,
        CAST(NULL AS NVARCHAR(100)) AS CustomerName,
        CAST(NULL AS NVARCHAR(50)) AS VendorCode,
        CAST(NULL AS NVARCHAR(100)) AS VendorName,
        CASE WHEN l.BaseType = 1470000065 AND l.BaseEntry IS NOT NULL THEN 'INC' ELSE NULL END AS LinkedDocType,
        TRY_CONVERT(INT, l.BaseEntry) AS LinkedDocEntry,
        CAST(countDoc.DocNum AS NVARCHAR(50)) AS LinkedDocNum,
        TRY_CONVERT(INT, l.BaseLine) AS LinkedLineNum,
        CAST(NULL AS NVARCHAR(10)) AS SalesPersonCode,
        CAST(NULL AS NVARCHAR(100)) AS SalesPersonName
    FROM OIQR h
    INNER JOIN IQR1 l ON h.DocEntry = l.DocEntry
    LEFT JOIN OITM i ON i.ItemCode = l.ItemCode
    LEFT JOIN OINC countDoc ON countDoc.DocEntry = l.BaseEntry
    WHERE l.ItemCode = '{safeItem}'
) M
WHERE 1=1 {whClause} {dateClause} {movementTypeClause} {salesPersonClause}
ORDER BY M.MovementDate DESC, M.DocEntry DESC, M.LineNum DESC";

                    rs.DoQuery(sql);

                    while (!rs.EoF)
                    {
                        filteredRows.Add(new InventoryMovementRow
                        {
                            SourceType = ReadString(rs, "SourceType") ?? string.Empty,
                            DocType = ReadString(rs, "SourceType") ?? string.Empty,
                            DocEntry = ReadInt(rs, "DocEntry"),
                            DocNum = ReadString(rs, "DocNum") ?? string.Empty,
                            MovementDate = ReadDate(rs, "MovementDate"),
                            LineNum = ReadInt(rs, "LineNum"),
                            ItemCode = ReadString(rs, "ItemCode") ?? string.Empty,
                            ItemName = ReadString(rs, "ItemName"),
                            MovementWarehouse = ReadString(rs, "MovementWarehouse"),
                            SourceWarehouse = ReadString(rs, "SourceWarehouse"),
                            TargetWarehouse = ReadString(rs, "TargetWarehouse"),
                            Quantity = ReadDecimal(rs, "Quantity"),
                            Direction = ReadString(rs, "Direction") ?? string.Empty,
                            DocumentStatus = ReadString(rs, "DocumentStatus"),
                            PartnerCode = ReadString(rs, "PartnerCode"),
                            PartnerName = ReadString(rs, "PartnerName"),
                            CustomerCode = ReadString(rs, "CustomerCode"),
                            CustomerName = ReadString(rs, "CustomerName"),
                            VendorCode = ReadString(rs, "VendorCode"),
                            VendorName = ReadString(rs, "VendorName"),
                            LinkedDocType = ReadString(rs, "LinkedDocType"),
                            LinkedDocEntry = ReadNullableInt(rs, "LinkedDocEntry"),
                            LinkedDocNum = ReadString(rs, "LinkedDocNum"),
                            LinkedLineNum = ReadNullableInt(rs, "LinkedLineNum"),
                            SalesPersonCode = ReadString(rs, "SalesPersonCode"),
                            SalesPersonName = ReadString(rs, "SalesPersonName")
                        });
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

        var itemMeta = LoadItemMetadata(scope, itemCode);
        filteredRows = filteredRows
            .Select(row => EnrichMovementRow(row, itemMeta))
            .ToList();

        var salesPeople = filteredRows
            .Where(r => r.SalesPersonCode != null)
            .Select(r => new SalesPersonInfo { SalesPersonCode = r.SalesPersonCode, SalesPersonName = r.SalesPersonName })
            .DistinctBy(r => r.SalesPersonCode)
            .OrderBy(r => r.SalesPersonName)
            .ToList();

        var total = filteredRows.Count;
        var rows = filteredRows
            .Skip(skip)
            .Take(take)
            .ToList();

        return new InventoryMovementResponse
        {
            ItemCode = itemCode,
            Brand = itemMeta.Brand,
            ItemBrand = itemMeta.Brand,
            ItemName = itemMeta.ItemName,
            ArticleNumber = itemMeta.ArticleNumber,
            TanNumber = itemMeta.TanNumber,
            EngineCode = itemMeta.TanNumber,
            ProductionPartNumber = itemMeta.ProductionPartNumber,
            PartNumberInProduction = itemMeta.ProductionPartNumber,
            PrimaryBarcode = itemMeta.PrimaryBarcode,
            WarehouseCode = warehouseCode,
            DateFrom = resolvedRange.From,
            DateTo = resolvedRange.To,
            MovementTypes = normalizedMovementTypes?.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            AsOfUtc = DateTime.UtcNow,
            Total = total,
            SalesPeople = salesPeople,
            Rows = rows
        };
    }

    private SnapshotState EnsureSnapshot(InventoryScope scope, bool includeZero)
    {
        var stateKey = BuildStateKey(scope, includeZero);

        if (_states.TryGetValue(stateKey, out var existing))
        {
            if (DateTime.UtcNow - existing.AsOfUtc <= _refreshInterval)
                return existing;
        }

        _refreshLock.Wait();
        try
        {
            if (_states.TryGetValue(stateKey, out existing))
            {
                if (DateTime.UtcNow - existing.AsOfUtc <= _refreshInterval)
                    return existing;
            }

            var refreshed = RefreshSnapshot(scope, includeZero);
            _states[stateKey] = refreshed;
            return refreshed;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private DeliveryAggregateSnapshotState EnsureDeliveryAggregateSnapshot(
        InventoryScope scope,
        DateOnly dateFrom,
        DateOnly dateTo)
    {
        var stateKey = BuildDeliveryAggregateStateKey(scope, dateFrom, dateTo);

        if (_deliveryAggregateStates.TryGetValue(stateKey, out var existing))
        {
            if (DateTime.UtcNow - existing.AsOfUtc <= _deliveryAggregateRefreshInterval)
                return existing;
        }

        var cacheLock = _deliveryAggregateLocks.GetOrAdd(stateKey, _ => new SemaphoreSlim(1, 1));
        cacheLock.Wait();
        try
        {
            if (_deliveryAggregateStates.TryGetValue(stateKey, out existing))
            {
                if (DateTime.UtcNow - existing.AsOfUtc <= _deliveryAggregateRefreshInterval)
                    return existing;
            }

            var refreshed = RefreshDeliveryAggregateSnapshot(scope, dateFrom, dateTo);
            _deliveryAggregateStates[stateKey] = refreshed;
            return refreshed;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private SnapshotState RefreshSnapshot(InventoryScope scope, bool includeZero)
    {
        if (!_profiles.Profiles.TryGetValue(scope.ProfileKey, out var profile))
            throw new InvalidOperationException($"Profile '{scope.ProfileKey}' not configured.");

        var liquiMolyMetaMap = scope.MetadataMode == InventoryMetadataMode.LiquiMoly
            ? LoadLiquiMolyItemMetaMap()
            : null;

        var rows = new List<InventoryStockRow>();
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

                    var includeZeroClause = includeZero
                        ? ""
                        : "AND (ISNULL(w.OnHand,0) <> 0 OR ISNULL(w.IsCommited,0) <> 0 OR ISNULL(w.OnOrder,0) <> 0)";
                    var metadataSelect = BuildStockMetadataSelect(scope, "w.ItemCode");

                    rs.DoQuery($@"
SELECT
    w.ItemCode AS ItemCode,
    i.ItemName AS ItemName,
    w.WhsCode AS WarehouseCode,
    h.WhsName AS WarehouseName,
    CONVERT(DECIMAL(19, 6), ISNULL(w.OnHand, 0)) AS OnHand,
    CONVERT(DECIMAL(19, 6), ISNULL(w.IsCommited, 0)) AS Committed,
    CONVERT(DECIMAL(19, 6), ISNULL(w.OnOrder, 0)) AS Ordered,
    {metadataSelect}
FROM OITW w
INNER JOIN OITM i ON i.ItemCode = w.ItemCode
LEFT JOIN OWHS h ON h.WhsCode = w.WhsCode
LEFT JOIN OITB g ON g.ItmsGrpCod = i.ItmsGrpCod
WHERE i.frozenFor = 'N'
  {includeZeroClause}
ORDER BY w.ItemCode, w.WhsCode");

                    while (!rs.EoF)
                    {
                        var itemCode = ReadString(rs, "ItemCode") ?? string.Empty;
                        var itemName = ReadString(rs, "ItemName") ?? string.Empty;
                        var warehouseCode = ReadString(rs, "WarehouseCode") ?? string.Empty;
                        var warehouseName = ReadString(rs, "WarehouseName");
                        var onHand = ReadDecimal(rs, "OnHand");
                        var committed = ReadDecimal(rs, "Committed");
                        var ordered = ReadDecimal(rs, "Ordered");

                        if (string.IsNullOrWhiteSpace(itemCode) || string.IsNullOrWhiteSpace(warehouseCode))
                        {
                            rs.MoveNext();
                            continue;
                        }

                        if (scope.FilterToLiquiMolyCatalog
                            && (liquiMolyMetaMap == null || !liquiMolyMetaMap.ContainsKey(itemCode)))
                        {
                            rs.MoveNext();
                            continue;
                        }

                        ItemMetadata? liquiMolyMeta = null;
                        liquiMolyMetaMap?.TryGetValue(itemCode, out liquiMolyMeta);
                        var itemMeta = ResolveItemMetadata(scope, rs, itemCode, itemName, liquiMolyMeta);

                        rows.Add(CreateStockRow(
                            itemCode,
                            warehouseCode,
                            warehouseName,
                            onHand,
                            committed,
                            ordered,
                            itemMeta,
                            isDeleted: false));

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

        var now = DateTime.UtcNow;
        var version = Interlocked.Increment(ref _versionCounter);

        _states.TryGetValue(BuildStateKey(scope, includeZero), out var previousState);
        var history = previousState?.History ?? new List<ChangeBatch>();
        var changedRows = BuildChanges(previousState?.Rows, rows);

        history.Add(new ChangeBatch(version, now, changedRows));
        const int maxHistory = 120; // Keep roughly last 20 minutes at 10s refresh cadence.
        if (history.Count > maxHistory)
            history = history.Skip(history.Count - maxHistory).ToList();

        _logger.LogInformation(
            "SapLiquiMolyInventoryReader: snapshot refreshed | Profile={Profile} | Scope={Scope} | Rows={Rows} | Changed={Changed} | Version={Version}",
            scope.ProfileKey, scope.ScopeKey, rows.Count, changedRows.Count, version);

        return new SnapshotState
        {
            Version = version,
            AsOfUtc = now,
            Rows = rows,
            History = history
        };
    }

    private DeliveryAggregateSnapshotState RefreshDeliveryAggregateSnapshot(
        InventoryScope scope,
        DateOnly dateFrom,
        DateOnly dateTo)
    {
        if (!_profiles.Profiles.TryGetValue(scope.ProfileKey, out var profile))
            throw new InvalidOperationException($"Profile '{scope.ProfileKey}' not configured.");

        var liquiMolyMetaMap = scope.MetadataMode == InventoryMetadataMode.LiquiMoly
            ? LoadLiquiMolyItemMetaMap()
            : null;
        var rows = new List<InventoryDeliveryAggregateRow>();
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

                    var fromSql = FormatSqlDate(dateFrom);
                    var toSql = FormatSqlDate(dateTo);
                    var metadataSelect = BuildDeliveryMetadataSelect(scope, "a.ItemCode");

                    rs.DoQuery($@"
WITH Agg AS
(
    SELECT
        l.ItemCode AS ItemCode,
        l.WhsCode AS WarehouseCode,
        CONVERT(DECIMAL(19, 6), SUM(ISNULL(l.Quantity, 0))) AS DeliveredQty,
        COUNT(DISTINCT h.DocEntry) AS DeliveryCount,
        MAX(h.DocEntry) AS LastDocEntry
    FROM ODLN h
    INNER JOIN DLN1 l ON h.DocEntry = l.DocEntry
    WHERE ISNULL(h.CANCELED, 'N') <> 'Y'
      AND h.DocDate >= '{fromSql}'
      AND h.DocDate <= '{toSql}'
    GROUP BY l.ItemCode, l.WhsCode
)
SELECT
    a.ItemCode AS ItemCode,
    COALESCE(NULLIF(i.ItemName, ''), a.ItemCode) AS ItemName,
    a.WarehouseCode AS WarehouseCode,
    a.DeliveredQty AS DeliveredQty,
    a.DeliveryCount AS DeliveryCount,
    lastDoc.DocEntry AS LastDeliveryDocEntry,
    CAST(lastDoc.DocNum AS NVARCHAR(50)) AS LastDeliveryDocNum,
    lastDoc.DocDate AS LastDeliveredAt,
    lastDoc.CardCode AS CustomerCode,
    lastDoc.CardName AS CustomerName,
    CASE WHEN ISNULL(lastDoc.SlpCode, -1) = -1 THEN NULL ELSE CAST(lastDoc.SlpCode AS NVARCHAR(10)) END AS SalesPersonCode,
    COALESCE(NULLIF(slp.SlpName, ''), NULL) AS SalesPersonName,
    {metadataSelect}
FROM Agg a
LEFT JOIN ODLN lastDoc ON lastDoc.DocEntry = a.LastDocEntry
LEFT JOIN OITM i ON i.ItemCode = a.ItemCode
LEFT JOIN OSLP slp ON slp.SlpCode = lastDoc.SlpCode
ORDER BY lastDoc.DocDate DESC, a.LastDocEntry DESC, a.ItemCode, a.WarehouseCode");

                    while (!rs.EoF)
                    {
                        var itemCode = ReadString(rs, "ItemCode") ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(itemCode))
                        {
                            rs.MoveNext();
                            continue;
                        }

                        if (scope.FilterToLiquiMolyCatalog
                            && (liquiMolyMetaMap == null || !liquiMolyMetaMap.ContainsKey(itemCode)))
                        {
                            rs.MoveNext();
                            continue;
                        }

                        ItemMetadata? liquiMolyMeta = null;
                        liquiMolyMetaMap?.TryGetValue(itemCode, out liquiMolyMeta);
                        var itemMeta = ResolveItemMetadata(
                            scope,
                            rs,
                            itemCode,
                            ReadString(rs, "ItemName") ?? itemCode,
                            liquiMolyMeta);

                        rows.Add(new InventoryDeliveryAggregateRow
                        {
                            ItemCode = itemCode,
                            Brand = itemMeta.Brand,
                            ItemBrand = itemMeta.Brand,
                            ArticleNumber = itemMeta.ArticleNumber ?? itemCode,
                            PrimaryBarcode = itemMeta.PrimaryBarcode,
                            ItemName = itemMeta.ItemName ?? itemCode,
                            TanNumber = itemMeta.TanNumber,
                            EngineCode = itemMeta.TanNumber,
                            ProductionPartNumber = itemMeta.ProductionPartNumber,
                            PartNumberInProduction = itemMeta.ProductionPartNumber,
                            Warehouse = ReadString(rs, "WarehouseCode") ?? string.Empty,
                            DeliveredQty = ReadDecimal(rs, "DeliveredQty"),
                            DeliveryCount = ReadInt(rs, "DeliveryCount"),
                            LastDeliveryDocEntry = ReadNullableInt(rs, "LastDeliveryDocEntry"),
                            LastDeliveryDocNum = ReadString(rs, "LastDeliveryDocNum"),
                            LastDeliveredAt = ReadDate(rs, "LastDeliveredAt"),
                            CustomerCode = ReadString(rs, "CustomerCode"),
                            CustomerName = ReadString(rs, "CustomerName"),
                            SalesPersonCode = ReadString(rs, "SalesPersonCode"),
                            SalesPersonName = ReadString(rs, "SalesPersonName")
                        });

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

        var now = DateTime.UtcNow;
        var version = Interlocked.Increment(ref _versionCounter);
        foreach (var row in rows)
        {
            row.AsOfUtc = now;
            row.Version = version;
        }

        _logger.LogInformation(
            "SapLiquiMolyInventoryReader: delivery aggregates refreshed | Profile={Profile} | Scope={Scope} | DateFrom={DateFrom} | DateTo={DateTo} | Rows={Rows} | Version={Version}",
            scope.ProfileKey, scope.ScopeKey, dateFrom, dateTo, rows.Count, version);

        return new DeliveryAggregateSnapshotState
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            AsOfUtc = now,
            Version = version,
            Rows = rows
        };
    }

    private static List<InventoryStockRow> BuildChanges(
        List<InventoryStockRow>? oldRows,
        List<InventoryStockRow> newRows)
    {
        if (oldRows == null || oldRows.Count == 0)
            return newRows.ToList();

        var oldMap = oldRows.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        var newMap = newRows.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

        var changed = new List<InventoryStockRow>();

        foreach (var row in newRows)
        {
            if (!oldMap.TryGetValue(row.Key, out var old))
            {
                changed.Add(row);
                continue;
            }

            if (old.OnHand != row.OnHand
                || old.Committed != row.Committed
                || old.Ordered != row.Ordered
                || !string.Equals(old.ItemName, row.ItemName, StringComparison.Ordinal)
                || !string.Equals(old.ArticleNumber, row.ArticleNumber, StringComparison.Ordinal)
                || !string.Equals(old.PrimaryBarcode, row.PrimaryBarcode, StringComparison.Ordinal)
                || !string.Equals(old.Brand, row.Brand, StringComparison.Ordinal)
                || !string.Equals(old.TanNumber, row.TanNumber, StringComparison.Ordinal)
                || !string.Equals(old.ProductionPartNumber, row.ProductionPartNumber, StringComparison.Ordinal)
                || !string.Equals(old.ItemGroup, row.ItemGroup, StringComparison.Ordinal))
            {
                changed.Add(row);
            }
        }

        foreach (var old in oldRows)
        {
            if (newMap.ContainsKey(old.Key))
                continue;

            changed.Add(new InventoryStockRow
            {
                Key = old.Key,
                ItemCode = old.ItemCode,
                ItemName = old.ItemName,
                Brand = old.Brand,
                ItemBrand = old.ItemBrand,
                ArticleNumber = old.ArticleNumber,
                PrimaryBarcode = old.PrimaryBarcode,
                TanNumber = old.TanNumber,
                EngineCode = old.EngineCode,
                ProductionPartNumber = old.ProductionPartNumber,
                PartNumberInProduction = old.PartNumberInProduction,
                ItemGroup = old.ItemGroup,
                WarehouseCode = old.WarehouseCode,
                WarehouseName = old.WarehouseName,
                OnHand = 0,
                Committed = 0,
                Ordered = 0,
                Available = 0,
                StockStatus = ResolveStockStatus(0, 0),
                LowStockThreshold = DefaultLowStockThreshold,
                OutOfStockThreshold = DefaultOutOfStockThreshold,
                IsDeleted = true
            });
        }

        return changed;
    }

    private static IEnumerable<InventoryStockRow> ApplyFilters(
        IEnumerable<InventoryStockRow> rows,
        string? search,
        string? warehouseCode)
    {
        var query = rows;

        if (!string.IsNullOrWhiteSpace(warehouseCode))
            query = query.Where(x => x.WarehouseCode.Equals(warehouseCode, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim();
            query = query.Where(x =>
                x.ItemCode.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || x.ArticleNumber.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(x.Brand)
                    && x.Brand.Contains(needle, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(x.TanNumber)
                    && x.TanNumber.Contains(needle, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(x.ProductionPartNumber)
                    && x.ProductionPartNumber.Contains(needle, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(x.PrimaryBarcode)
                    && x.PrimaryBarcode.Contains(needle, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(x.ItemName)
                    && x.ItemName.Contains(needle, StringComparison.OrdinalIgnoreCase))
                || x.WarehouseCode.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(x.WarehouseName)
                    && x.WarehouseName.Contains(needle, StringComparison.OrdinalIgnoreCase)));
        }

        return query;
    }

    private static IEnumerable<InventoryDeliveryAggregateRow> ApplyDeliveryFilters(
        IEnumerable<InventoryDeliveryAggregateRow> rows,
        string? search,
        string? warehouseCode)
    {
        var query = rows;

        if (!string.IsNullOrWhiteSpace(warehouseCode))
            query = query.Where(x => x.Warehouse.Equals(warehouseCode, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim();
            query = query.Where(x =>
                x.ItemCode.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || x.ArticleNumber.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(x.Brand)
                    && x.Brand.Contains(needle, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(x.TanNumber)
                    && x.TanNumber.Contains(needle, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(x.ProductionPartNumber)
                    && x.ProductionPartNumber.Contains(needle, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(x.PrimaryBarcode)
                    && x.PrimaryBarcode.Contains(needle, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(x.ItemName)
                    && x.ItemName.Contains(needle, StringComparison.OrdinalIgnoreCase))
                || x.Warehouse.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        return query;
    }

    private static string BuildStateKey(InventoryScope scope, bool includeZero) =>
        $"{scope.ProfileKey}|{scope.ScopeKey}|{includeZero}";

    private static string BuildDeliveryAggregateStateKey(
        InventoryScope scope,
        DateOnly dateFrom,
        DateOnly dateTo) =>
        $"{scope.ProfileKey}|{scope.ScopeKey}|DLV|{dateFrom:yyyyMMdd}|{dateTo:yyyyMMdd}";

    private Dictionary<string, ItemMetadata> LoadLiquiMolyItemMetaMap() =>
        _cacheDb.CacheLiquiMolyProducts
            .AsNoTracking()
            .Select(x => new ItemMetadata
            {
                Brand = "Liqui Moly",
                ArticleNumber = x.ArticleNumber,
                ItemName = x.Name,
                PrimaryBarcode = x.PrimaryBarcode,
                IsActive = x.IsActive
            })
            .ToList()
            .GroupBy(x => x.ArticleNumber, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(x => x.IsActive)
                .ThenByDescending(x => !string.IsNullOrWhiteSpace(x.PrimaryBarcode))
                .ThenByDescending(x => !string.IsNullOrWhiteSpace(x.ItemName))
                .First())
            .ToDictionary(x => x.ArticleNumber, StringComparer.OrdinalIgnoreCase);

    private static InventoryStockRow CreateStockRow(
        string itemCode,
        string warehouseCode,
        string? warehouseName,
        decimal onHand,
        decimal committed,
        decimal ordered,
        ItemMetadata itemMeta,
        bool isDeleted)
    {
        var available = onHand - committed + ordered;
        return new InventoryStockRow
        {
            Key = $"{itemCode}|{warehouseCode}",
            ItemCode = itemCode,
            Brand = itemMeta.Brand,
            ItemBrand = itemMeta.Brand,
            ItemName = itemMeta.ItemName ?? itemCode,
            ArticleNumber = itemMeta.ArticleNumber ?? itemCode,
            PrimaryBarcode = itemMeta.PrimaryBarcode,
            TanNumber = itemMeta.TanNumber,
            EngineCode = itemMeta.TanNumber,
            ProductionPartNumber = itemMeta.ProductionPartNumber,
            PartNumberInProduction = itemMeta.ProductionPartNumber,
            ItemGroup = itemMeta.ItemGroupName,
            WarehouseCode = warehouseCode,
            WarehouseName = warehouseName,
            OnHand = onHand,
            Committed = committed,
            Ordered = ordered,
            Available = available,
            StockStatus = ResolveStockStatus(onHand, available),
            LowStockThreshold = DefaultLowStockThreshold,
            OutOfStockThreshold = DefaultOutOfStockThreshold,
            IsDeleted = isDeleted
        };
    }

    private static string ResolveStockStatus(decimal onHand, decimal available)
    {
        if (onHand <= DefaultOutOfStockThreshold || available <= DefaultOutOfStockThreshold)
            return "OUT_OF_STOCK";

        if (onHand <= DefaultLowStockThreshold || available <= DefaultLowStockThreshold)
            return "LOW_STOCK";

        return "IN_STOCK";
    }

    private static RequiredDateRange ResolveRequiredDateRange(
        DateOnly? dateFrom,
        DateOnly? dateTo,
        bool defaultToBusinessToday)
    {
        if (!dateFrom.HasValue && !dateTo.HasValue)
        {
            if (!defaultToBusinessToday)
                throw new ArgumentException("dateFrom or dateTo is required.");

            var today = GetDarEsSalaamBusinessDate();
            return new RequiredDateRange(today, today);
        }

        var from = dateFrom ?? dateTo!.Value;
        var to = dateTo ?? dateFrom!.Value;

        if (from > to)
            throw new ArgumentException("dateFrom must be less than or equal to dateTo.");

        return new RequiredDateRange(from, to);
    }

    private static OptionalDateRange ResolveOptionalDateRange(DateOnly? dateFrom, DateOnly? dateTo)
    {
        if (!dateFrom.HasValue && !dateTo.HasValue)
            return new OptionalDateRange(null, null);

        var from = dateFrom ?? dateTo!.Value;
        var to = dateTo ?? dateFrom!.Value;

        if (from > to)
            throw new ArgumentException("dateFrom must be less than or equal to dateTo.");

        return new OptionalDateRange(from, to);
    }

    private static string BuildOptionalDateClause(string fieldName, OptionalDateRange range)
    {
        var clauses = new List<string>();
        if (range.From.HasValue)
            clauses.Add($"AND {fieldName} >= '{FormatSqlDate(range.From.Value)}'");
        if (range.To.HasValue)
            clauses.Add($"AND {fieldName} <= '{FormatSqlDate(range.To.Value)}'");

        return clauses.Count == 0
            ? string.Empty
            : " " + string.Join(" ", clauses);
    }

    private static string BuildMovementTypeClause(HashSet<string>? movementTypes)
    {
        if (movementTypes == null || movementTypes.Count == 0)
            return string.Empty;

        var values = string.Join(", ", movementTypes
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"'{x}'"));

        return $" AND SourceType IN ({values})";
    }

    private static HashSet<string>? NormalizeMovementTypes(string? movementTypes)
    {
        if (string.IsNullOrWhiteSpace(movementTypes))
            return null;

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SO", "DLV", "TRQ", "TRF", "GR", "GI", "INC", "IP"
        };

        var values = movementTypes
            .Split([',', ';', '|', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var invalid = values.Where(x => !allowed.Contains(x)).ToList();
        if (invalid.Count > 0)
            throw new ArgumentException($"Unsupported movementTypes: {string.Join(", ", invalid)}.");

        return values.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static DateOnly GetDarEsSalaamBusinessDate()
    {
        var utcNow = DateTimeOffset.UtcNow;
        var tz = GetDarEsSalaamTimeZone();
        var localNow = TimeZoneInfo.ConvertTime(utcNow, tz);
        return DateOnly.FromDateTime(localNow.DateTime);
    }

    private static TimeZoneInfo GetDarEsSalaamTimeZone()
    {
        foreach (var id in new[] { "Africa/Dar_es_Salaam", "E. Africa Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }

    private InventoryScope ResolveScope(string profileKey, string? brand, bool onlyLiquiMoly)
    {
        var requestedProfile = string.IsNullOrWhiteSpace(profileKey)
            ? "MolasLubes"
            : profileKey.Trim();
        var normalizedBrand = NormalizeScopeBrand(brand);

        if (string.Equals(normalizedBrand, "AUTOHUB", StringComparison.Ordinal))
        {
            return new InventoryScope(
                "AutoHub",
                "AutoHub",
                InventoryMetadataMode.AutoHub,
                FilterToLiquiMolyCatalog: false,
                DefaultBrandLabel: "AutoHub");
        }

        if (string.Equals(requestedProfile, "AutoHub", StringComparison.OrdinalIgnoreCase))
        {
            return new InventoryScope(
                "AutoHub",
                "AutoHub",
                InventoryMetadataMode.AutoHub,
                FilterToLiquiMolyCatalog: false,
                DefaultBrandLabel: "AutoHub");
        }

        if (string.Equals(normalizedBrand, "LIQUIMOLY", StringComparison.Ordinal)
            || string.Equals(requestedProfile, "MolasLubes", StringComparison.OrdinalIgnoreCase)
            || onlyLiquiMoly)
        {
            return new InventoryScope(
                "MolasLubes",
                "LiquiMoly",
                InventoryMetadataMode.LiquiMoly,
                FilterToLiquiMolyCatalog: true,
                DefaultBrandLabel: "Liqui Moly");
        }

        return new InventoryScope(
            requestedProfile,
            requestedProfile,
            InventoryMetadataMode.Generic,
            FilterToLiquiMolyCatalog: false,
            DefaultBrandLabel: requestedProfile);
    }

    private static string? NormalizeScopeBrand(string? brand)
    {
        if (string.IsNullOrWhiteSpace(brand))
            return null;

        return brand.Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
    }

    private static string BuildStockMetadataSelect(InventoryScope scope, string itemCodeSql) =>
        scope.MetadataMode switch
        {
            InventoryMetadataMode.AutoHub => $@"
    NULLIF(i.U_MdlTEST, '') AS ItemBrand,
    COALESCE(NULLIF(i.U_Item_Name, ''), NULLIF(i.ItemName, ''), {itemCodeSql}) AS MappedItemName,
    COALESCE(NULLIF(i.U_Article_No, ''), {itemCodeSql}) AS MappedArticleNumber,
    NULLIF(i.U_Engine_Code, '') AS TanNumber,
    NULLIF(i.U_PT_No_Inproduction, '') AS ProductionPartNumber,
    NULLIF(i.CodeBars, '') AS PrimaryBarcode,
    COALESCE(NULLIF(g.ItmsGrpNam, ''), NULL) AS ItemGroupName",
            InventoryMetadataMode.LiquiMoly => $@"
    CAST('Liqui Moly' AS NVARCHAR(100)) AS ItemBrand,
    COALESCE(NULLIF(i.ItemName, ''), {itemCodeSql}) AS MappedItemName,
    {itemCodeSql} AS MappedArticleNumber,
    CAST(NULL AS NVARCHAR(100)) AS TanNumber,
    CAST(NULL AS NVARCHAR(100)) AS ProductionPartNumber,
    NULLIF(i.CodeBars, '') AS PrimaryBarcode,
    COALESCE(NULLIF(g.ItmsGrpNam, ''), NULL) AS ItemGroupName",
            _ => $@"
    CAST(NULL AS NVARCHAR(100)) AS ItemBrand,
    COALESCE(NULLIF(i.ItemName, ''), {itemCodeSql}) AS MappedItemName,
    {itemCodeSql} AS MappedArticleNumber,
    CAST(NULL AS NVARCHAR(100)) AS TanNumber,
    CAST(NULL AS NVARCHAR(100)) AS ProductionPartNumber,
    NULLIF(i.CodeBars, '') AS PrimaryBarcode,
    COALESCE(NULLIF(g.ItmsGrpNam, ''), NULL) AS ItemGroupName"
        };

    private static string BuildDeliveryMetadataSelect(InventoryScope scope, string itemCodeSql) =>
        scope.MetadataMode switch
        {
            InventoryMetadataMode.AutoHub => $@"
    NULLIF(i.U_MdlTEST, '') AS ItemBrand,
    COALESCE(NULLIF(i.U_Item_Name, ''), NULLIF(i.ItemName, ''), {itemCodeSql}) AS ItemName,
    COALESCE(NULLIF(i.U_Article_No, ''), {itemCodeSql}) AS ArticleNumber,
    NULLIF(i.U_Engine_Code, '') AS TanNumber,
    NULLIF(i.U_PT_No_Inproduction, '') AS ProductionPartNumber,
    NULLIF(i.CodeBars, '') AS PrimaryBarcode",
            InventoryMetadataMode.LiquiMoly => $@"
    CAST('Liqui Moly' AS NVARCHAR(100)) AS ItemBrand,
    COALESCE(NULLIF(i.ItemName, ''), {itemCodeSql}) AS ItemName,
    {itemCodeSql} AS ArticleNumber,
    CAST(NULL AS NVARCHAR(100)) AS TanNumber,
    CAST(NULL AS NVARCHAR(100)) AS ProductionPartNumber,
    NULLIF(i.CodeBars, '') AS PrimaryBarcode",
            _ => $@"
    CAST(NULL AS NVARCHAR(100)) AS ItemBrand,
    COALESCE(NULLIF(i.ItemName, ''), {itemCodeSql}) AS ItemName,
    {itemCodeSql} AS ArticleNumber,
    CAST(NULL AS NVARCHAR(100)) AS TanNumber,
    CAST(NULL AS NVARCHAR(100)) AS ProductionPartNumber,
    NULLIF(i.CodeBars, '') AS PrimaryBarcode"
        };

    private static ItemMetadata ResolveItemMetadata(
        InventoryScope scope,
        Recordset rs,
        string itemCode,
        string fallbackItemName,
        ItemMetadata? liquiMolyMeta)
    {
        var rowMeta = new ItemMetadata
        {
            Brand = ReadString(rs, "ItemBrand") ?? scope.DefaultBrandLabel,
            ItemName = ReadString(rs, "MappedItemName") ?? ReadString(rs, "ItemName") ?? fallbackItemName,
            ArticleNumber = ReadString(rs, "MappedArticleNumber") ?? ReadString(rs, "ArticleNumber") ?? itemCode,
            TanNumber = ReadString(rs, "TanNumber"),
            ProductionPartNumber = ReadString(rs, "ProductionPartNumber"),
            PrimaryBarcode = ReadString(rs, "PrimaryBarcode"),
            ItemGroupName = ReadString(rs, "ItemGroupName")
        };

        if (scope.MetadataMode != InventoryMetadataMode.LiquiMoly || liquiMolyMeta == null)
            return NormalizeItemMetadata(itemCode, rowMeta, scope.DefaultBrandLabel);

        return NormalizeItemMetadata(itemCode, new ItemMetadata
        {
            Brand = liquiMolyMeta.Brand ?? rowMeta.Brand ?? scope.DefaultBrandLabel,
            ItemName = liquiMolyMeta.ItemName ?? rowMeta.ItemName ?? fallbackItemName,
            ArticleNumber = liquiMolyMeta.ArticleNumber ?? rowMeta.ArticleNumber ?? itemCode,
            PrimaryBarcode = liquiMolyMeta.PrimaryBarcode ?? rowMeta.PrimaryBarcode,
            TanNumber = rowMeta.TanNumber,
            ProductionPartNumber = rowMeta.ProductionPartNumber,
            ItemGroupName = rowMeta.ItemGroupName,
            IsActive = liquiMolyMeta.IsActive
        }, scope.DefaultBrandLabel);
    }

    private ItemMetadata LoadItemMetadata(InventoryScope scope, string itemCode)
    {
        if (!_profiles.Profiles.TryGetValue(scope.ProfileKey, out var profile))
            throw new InvalidOperationException($"Profile '{scope.ProfileKey}' not configured.");

        if (scope.MetadataMode == InventoryMetadataMode.LiquiMoly)
        {
            var liquiMolyMetaMap = LoadLiquiMolyItemMetaMap();
            if (liquiMolyMetaMap.TryGetValue(itemCode, out var liquiMolyMeta))
                return NormalizeItemMetadata(itemCode, liquiMolyMeta, scope.DefaultBrandLabel);
        }

        ItemMetadata? itemMeta = null;
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
                    var safeItem = itemCode.Replace("'", "''");
                    var metadataSelect = BuildStockMetadataSelect(scope, "i.ItemCode");

                    rs.DoQuery($@"
SELECT TOP 1
    i.ItemCode AS ItemCode,
    {metadataSelect}
FROM OITM i
LEFT JOIN OITB g ON g.ItmsGrpCod = i.ItmsGrpCod
WHERE i.ItemCode = '{safeItem}'");

                    if (!rs.EoF)
                        itemMeta = ResolveItemMetadata(scope, rs, itemCode, itemCode, liquiMolyMeta: null);
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

        return itemMeta ?? NormalizeItemMetadata(itemCode, new ItemMetadata
        {
            Brand = scope.DefaultBrandLabel,
            ItemName = itemCode,
            ArticleNumber = itemCode
        }, scope.DefaultBrandLabel);
    }

    private static ItemMetadata NormalizeItemMetadata(
        string itemCode,
        ItemMetadata itemMeta,
        string? defaultBrandLabel)
    {
        return new ItemMetadata
        {
            Brand = string.IsNullOrWhiteSpace(itemMeta.Brand) ? defaultBrandLabel : itemMeta.Brand,
            ItemName = string.IsNullOrWhiteSpace(itemMeta.ItemName) ? itemCode : itemMeta.ItemName,
            ArticleNumber = string.IsNullOrWhiteSpace(itemMeta.ArticleNumber) ? itemCode : itemMeta.ArticleNumber,
            TanNumber = itemMeta.TanNumber,
            ProductionPartNumber = itemMeta.ProductionPartNumber,
            PrimaryBarcode = itemMeta.PrimaryBarcode,
            ItemGroupName = itemMeta.ItemGroupName,
            IsActive = itemMeta.IsActive
        };
    }

    private static InventoryMovementRow EnrichMovementRow(InventoryMovementRow row, ItemMetadata itemMeta) =>
        new()
        {
            SourceType = row.SourceType,
            DocType = row.DocType,
            DocEntry = row.DocEntry,
            DocNum = row.DocNum,
            MovementDate = row.MovementDate,
            LineNum = row.LineNum,
            ItemCode = row.ItemCode,
            Brand = itemMeta.Brand,
            ItemBrand = itemMeta.Brand,
            ItemName = string.IsNullOrWhiteSpace(itemMeta.ItemName) ? row.ItemName : itemMeta.ItemName,
            ArticleNumber = itemMeta.ArticleNumber,
            PrimaryBarcode = itemMeta.PrimaryBarcode,
            Barcode = itemMeta.PrimaryBarcode,
            TanNumber = itemMeta.TanNumber,
            EngineCode = itemMeta.TanNumber,
            ProductionPartNumber = itemMeta.ProductionPartNumber,
            PartNumberInProduction = itemMeta.ProductionPartNumber,
            MovementWarehouse = row.MovementWarehouse,
            SourceWarehouse = row.SourceWarehouse,
            TargetWarehouse = row.TargetWarehouse,
            Quantity = row.Quantity,
            Direction = row.Direction,
            DocumentStatus = row.DocumentStatus,
            PartnerCode = row.PartnerCode,
            PartnerName = row.PartnerName,
            CustomerCode = row.CustomerCode,
            CustomerName = row.CustomerName,
            VendorCode = row.VendorCode,
            VendorName = row.VendorName,
            LinkedDocType = row.LinkedDocType,
            LinkedDocEntry = row.LinkedDocEntry,
            LinkedDocNum = row.LinkedDocNum,
            LinkedLineNum = row.LinkedLineNum,
            SalesPersonCode = row.SalesPersonCode,
            SalesPersonName = row.SalesPersonName
        };

    private static string FormatSqlDate(DateOnly date) =>
        date.ToString("yyyyMMdd");

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

        try
        {
            if (company.Connected)
                company.Disconnect();
        }
        catch
        {
        }

        Marshal.ReleaseComObject(company);
    }

    private static string? ReadString(Recordset rs, string fieldName)
    {
        try
        {
            var value = rs.Fields.Item(fieldName).Value;
            var text = value?.ToString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    private static int ReadInt(Recordset rs, string fieldName) =>
        Convert.ToInt32(rs.Fields.Item(fieldName).Value);

    private static int? ReadNullableInt(Recordset rs, string fieldName)
    {
        var value = rs.Fields.Item(fieldName).Value;
        if (value == null || value is DBNull)
            return null;

        return Convert.ToInt32(value);
    }

    private static decimal ReadDecimal(Recordset rs, string fieldName) =>
        Convert.ToDecimal(rs.Fields.Item(fieldName).Value ?? 0m);

    private static DateTime? ReadDate(Recordset rs, string fieldName)
    {
        var value = rs.Fields.Item(fieldName).Value;
        if (value == null || value is DBNull)
            return null;

        if (value is DateTime dateTime)
            return dateTime;

        return DateTime.TryParse(value.ToString(), out DateTime parsed)
            ? parsed
            : null;
    }

    private sealed class ItemMetadata
    {
        public string? Brand { get; init; }
        public string? ItemName { get; init; }
        public string? ArticleNumber { get; init; }
        public string? TanNumber { get; init; }
        public string? ProductionPartNumber { get; init; }
        public string? PrimaryBarcode { get; init; }
        public string? ItemGroupName { get; init; }
        public bool IsActive { get; init; }
    }

    private sealed class SnapshotState
    {
        public long Version { get; init; }
        public DateTime AsOfUtc { get; init; }
        public List<InventoryStockRow> Rows { get; init; } = new();
        public List<ChangeBatch> History { get; init; } = new();
    }

    private sealed class DeliveryAggregateSnapshotState
    {
        public DateOnly DateFrom { get; init; }
        public DateOnly DateTo { get; init; }
        public long Version { get; init; }
        public DateTime AsOfUtc { get; init; }
        public List<InventoryDeliveryAggregateRow> Rows { get; init; } = new();
    }

    private sealed record ChangeBatch(long Version, DateTime AsOfUtc, List<InventoryStockRow> Rows);
    private sealed record RequiredDateRange(DateOnly From, DateOnly To);
    private sealed record OptionalDateRange(DateOnly? From, DateOnly? To);
    private sealed record InventoryScope(
        string ProfileKey,
        string ScopeKey,
        InventoryMetadataMode MetadataMode,
        bool FilterToLiquiMolyCatalog,
        string DefaultBrandLabel);
    private enum InventoryMetadataMode
    {
        Generic,
        LiquiMoly,
        AutoHub
    }
}

public class InventoryStockSnapshotResponse
{
    public DateTime AsOfUtc { get; init; }
    public long Version { get; init; }
    public int Total { get; init; }
    public List<InventoryStockRow> Rows { get; init; } = new();
}

public class InventoryStockChangesResponse
{
    public DateTime AsOfUtc { get; init; }
    public long Version { get; init; }
    public long SinceVersion { get; init; }
    public bool ResetRequired { get; init; }
    public List<InventoryStockRow> Rows { get; init; } = new();
}

public class InventoryStockSummaryResponse
{
    public DateTime AsOfUtc { get; init; }
    public long Version { get; init; }
    public int ItemWarehouseCount { get; init; }
    public int ItemCount { get; init; }
    public int WarehouseCount { get; init; }
    public decimal TotalOnHand { get; init; }
    public decimal TotalCommitted { get; init; }
    public decimal TotalOrdered { get; init; }
    public decimal TotalAvailable { get; init; }
    public decimal TotalNetAvailable { get; init; }
}

public class InventoryStockRow
{
    public string Key { get; init; } = string.Empty;
    public string ItemCode { get; init; } = string.Empty;
    public string SapItemCode => ItemCode;
    public string? Brand { get; init; }
    public string? ItemBrand { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public string ArticleNumber { get; init; } = string.Empty;
    public string? PrimaryBarcode { get; init; }
    public string? Barcode => PrimaryBarcode;
    public string? TanNumber { get; init; }
    public string? EngineCode { get; init; }
    public string? ProductionPartNumber { get; init; }
    public string? PartNumberInProduction { get; init; }
    public string? ItemGroup { get; init; }
    public string WarehouseCode { get; init; } = string.Empty;
    public string? WarehouseName { get; init; }
    public decimal OnHand { get; init; }
    public decimal Committed { get; init; }
    public decimal Ordered { get; init; }
    public decimal Available { get; init; }
    public string StockStatus { get; init; } = "UNKNOWN";
    public decimal LowStockThreshold { get; init; }
    public decimal OutOfStockThreshold { get; init; }
    public bool IsDeleted { get; init; }
}

public class InventoryMovementResponse
{
    public string ItemCode { get; init; } = string.Empty;
    public string SapItemCode => ItemCode;
    public string? Brand { get; init; }
    public string? ItemBrand { get; init; }
    public string? ItemName { get; init; }
    public string? ArticleNumber { get; init; }
    public string? PrimaryBarcode { get; init; }
    public string? Barcode => PrimaryBarcode;
    public string? TanNumber { get; init; }
    public string? EngineCode { get; init; }
    public string? ProductionPartNumber { get; init; }
    public string? PartNumberInProduction { get; init; }
    public string? WarehouseCode { get; init; }
    public DateOnly? DateFrom { get; init; }
    public DateOnly? DateTo { get; init; }
    public List<string>? MovementTypes { get; init; }
    public DateTime AsOfUtc { get; init; }
    public int Total { get; init; }
    public List<SalesPersonInfo> SalesPeople { get; init; } = new();
    public List<InventoryMovementRow> Rows { get; init; } = new();
}

public class SalesPersonInfo
{
    public string? SalesPersonCode { get; init; }
    public string? SalesPersonName { get; init; }
}

public class InventoryMovementRow
{
    public string SourceType { get; init; } = string.Empty; // SO, DLV, TRQ, TRF, GR, GI, INC, IP
    public string DocType { get; init; } = string.Empty; // Alias for frontend drilldown routing.
    public int DocEntry { get; init; }
    public string DocNum { get; init; } = string.Empty;
    public DateTime? MovementDate { get; init; }
    public int LineNum { get; init; }
    public string ItemCode { get; init; } = string.Empty;
    public string SapItemCode => ItemCode;
    public string? Brand { get; init; }
    public string? ItemBrand { get; init; }
    public string? ItemName { get; init; }
    public string? ArticleNumber { get; init; }
    public string? PrimaryBarcode { get; init; }
    public string? Barcode { get; init; }
    public string? TanNumber { get; init; }
    public string? EngineCode { get; init; }
    public string? ProductionPartNumber { get; init; }
    public string? PartNumberInProduction { get; init; }
    public string? MovementWarehouse { get; init; }
    public string? SourceWarehouse { get; init; }
    public string? TargetWarehouse { get; init; }
    public string? FromWarehouse => SourceWarehouse;
    public string? ToWarehouse => TargetWarehouse;
    public decimal Quantity { get; init; }
    public string Direction { get; init; } = string.Empty; // IN, OUT, COUNT
    public string? DocumentStatus { get; init; }
    public string? PartnerCode { get; init; }
    public string? PartnerName { get; init; }
    public string? CustomerCode { get; init; }
    public string? CustomerName { get; init; }
    public string? VendorCode { get; init; }
    public string? VendorName { get; init; }
    public string? LinkedDocType { get; init; }
    public int? LinkedDocEntry { get; init; }
    public string? LinkedDocNum { get; init; }
    public int? LinkedLineNum { get; init; }
    public string? SalesPersonCode { get; init; }
    public string? SalesPersonName { get; init; }
}

public class InventoryDeliveryAggregateResponse
{
    public DateOnly DateFrom { get; init; }
    public DateOnly DateTo { get; init; }
    public DateTime AsOfUtc { get; init; }
    public long Version { get; init; }
    public int Total { get; init; }
    public List<InventoryDeliveryAggregateRow> Rows { get; init; } = new();
}

public class InventoryTodayDeliveryResponse
{
    public DateOnly Date { get; init; }
    public DateTime AsOfUtc { get; init; }
    public long Version { get; init; }
    public int Total { get; init; }
    public List<InventoryTodayDeliveryItem> Items { get; init; } = new();
}

public class InventoryTodayDeliveryItem
{
    public string ItemCode { get; init; } = string.Empty;
    public string? ItemName { get; init; }
    public string? Brand { get; init; }
    public string? ArticleNumber { get; init; }
    public string? TanNumber { get; init; }
    public decimal DeliveredQty { get; init; }
    public int DeliveryCount { get; init; }
    public DateTime? LastDeliveredAt { get; init; }
    public int? LastDeliveryDocEntry { get; init; }
    public string? LastDeliveryDocNum { get; init; }
    public List<string> Warehouses { get; init; } = new();
}

public class InventoryDeliveryAggregateRow
{
    public string ItemCode { get; init; } = string.Empty;
    public string SapItemCode => ItemCode;
    public string? Brand { get; init; }
    public string? ItemBrand { get; init; }
    public string ArticleNumber { get; init; } = string.Empty;
    public string? PrimaryBarcode { get; init; }
    public string? Barcode => PrimaryBarcode;
    public string ItemName { get; init; } = string.Empty;
    public string? TanNumber { get; init; }
    public string? EngineCode { get; init; }
    public string? ProductionPartNumber { get; init; }
    public string? PartNumberInProduction { get; init; }
    public string Warehouse { get; init; } = string.Empty;
    public decimal DeliveredQty { get; init; }
    public int DeliveryCount { get; init; }
    public int? LastDeliveryDocEntry { get; init; }
    public string? LastDeliveryDocNum { get; init; }
    public DateTime? LastDeliveredAt { get; init; }
    public string? CustomerCode { get; init; }
    public string? CustomerName { get; init; }
    public string? SalesPersonCode { get; init; }
    public string? SalesPersonName { get; init; }
    public DateTime AsOfUtc { get; set; }
    public long Version { get; set; }
}
