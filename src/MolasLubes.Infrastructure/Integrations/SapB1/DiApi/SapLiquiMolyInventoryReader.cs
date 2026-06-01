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
    private readonly IntegrationProfilesOptions _profiles;
    private readonly MolasCacheDbContext _cacheDb;
    private readonly ILogger<SapLiquiMolyInventoryReader> _logger;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(10);
    private long _versionCounter = 0;

    private readonly ConcurrentDictionary<string, SnapshotState> _states =
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
        string? search,
        string? warehouseCode,
        int skip,
        int take,
        bool includeZero,
        bool onlyLiquiMoly)
    {
        var state = EnsureSnapshot(profileKey, includeZero, onlyLiquiMoly);

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
        string itemCode,
        string? warehouseCode)
    {
        var state = EnsureSnapshot(profileKey, includeZero: true, onlyLiquiMoly: false);

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
        long sinceVersion,
        string? search,
        string? warehouseCode,
        bool includeZero,
        bool onlyLiquiMoly)
    {
        var state = EnsureSnapshot(profileKey, includeZero, onlyLiquiMoly);

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
        string? warehouseCode,
        bool includeZero,
        bool onlyLiquiMoly)
    {
        var state = EnsureSnapshot(profileKey, includeZero, onlyLiquiMoly);

        var rows = state.Rows.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(warehouseCode))
            rows = rows.Where(x => x.WarehouseCode.Equals(warehouseCode, StringComparison.OrdinalIgnoreCase));

        return new InventoryStockSummaryResponse
        {
            AsOfUtc = state.AsOfUtc,
            Version = state.Version,
            ItemWarehouseCount = rows.Count(),
            ItemCount = rows.Select(x => x.ItemCode).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            WarehouseCount = rows.Select(x => x.WarehouseCode).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            TotalOnHand = rows.Sum(x => x.OnHand),
            TotalCommitted = rows.Sum(x => x.Committed),
            TotalOrdered = rows.Sum(x => x.Ordered),
            TotalAvailable = rows.Sum(x => x.Available)
        };
    }

    public InventoryMovementResponse GetMovements(
        string profileKey,
        string itemCode,
        string? warehouseCode,
        int take)
    {
        if (!_profiles.Profiles.TryGetValue(profileKey, out var profile))
            throw new InvalidOperationException($"Profile '{profileKey}' not configured.");

        var rows = new List<InventoryMovementRow>();
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

                    var sql = $@"
SELECT TOP {take}
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
        h.CardName AS PartnerName
    FROM ORDR h
    INNER JOIN RDR1 l ON h.DocEntry = l.DocEntry
    LEFT JOIN OITM i ON i.ItemCode = l.ItemCode
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
        h.CardName AS PartnerName
    FROM ODLN h
    INNER JOIN DLN1 l ON h.DocEntry = l.DocEntry
    LEFT JOIN OITM i ON i.ItemCode = l.ItemCode
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
        CAST(NULL AS NVARCHAR(100)) AS PartnerName
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
        CAST(NULL AS NVARCHAR(100)) AS PartnerName
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
        CAST(NULL AS NVARCHAR(100)) AS PartnerName
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
        CAST(NULL AS NVARCHAR(100)) AS PartnerName
    FROM OIGE h
    INNER JOIN IGE1 l ON h.DocEntry = l.DocEntry
    LEFT JOIN OITM i ON i.ItemCode = l.ItemCode
    WHERE l.ItemCode = '{safeItem}'
) M
WHERE 1=1 {whClause}
ORDER BY M.MovementDate DESC, M.DocEntry DESC, M.LineNum DESC";

                    rs.DoQuery(sql);

                    while (!rs.EoF)
                    {
                        rows.Add(new InventoryMovementRow
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
                            PartnerName = ReadString(rs, "PartnerName")
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

        return new InventoryMovementResponse
        {
            ItemCode = itemCode,
            WarehouseCode = warehouseCode,
            AsOfUtc = DateTime.UtcNow,
            Rows = rows
        };
    }

    private SnapshotState EnsureSnapshot(string profileKey, bool includeZero, bool onlyLiquiMoly)
    {
        var stateKey = BuildStateKey(profileKey, includeZero, onlyLiquiMoly);

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

            var refreshed = RefreshSnapshot(profileKey, includeZero, onlyLiquiMoly);
            _states[stateKey] = refreshed;
            return refreshed;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private SnapshotState RefreshSnapshot(string profileKey, bool includeZero, bool onlyLiquiMoly)
    {
        if (!_profiles.Profiles.TryGetValue(profileKey, out var profile))
            throw new InvalidOperationException($"Profile '{profileKey}' not configured.");

        var cacheMeta = _cacheDb.CacheLiquiMolyProducts
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new CacheMeta
            {
                ArticleNumber = x.ArticleNumber,
                Name = x.Name,
                PrimaryBarcode = x.PrimaryBarcode
            })
            .ToList()
            .ToDictionary(x => x.ArticleNumber, StringComparer.OrdinalIgnoreCase);

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

                    rs.DoQuery($@"
SELECT
    w.ItemCode AS ItemCode,
    i.ItemName AS ItemName,
    w.WhsCode AS WarehouseCode,
    h.WhsName AS WarehouseName,
    CONVERT(DECIMAL(19, 6), ISNULL(w.OnHand, 0)) AS OnHand,
    CONVERT(DECIMAL(19, 6), ISNULL(w.IsCommited, 0)) AS Committed,
    CONVERT(DECIMAL(19, 6), ISNULL(w.OnOrder, 0)) AS Ordered
FROM OITW w
INNER JOIN OITM i ON i.ItemCode = w.ItemCode
LEFT JOIN OWHS h ON h.WhsCode = w.WhsCode
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

                        if (onlyLiquiMoly && !cacheMeta.ContainsKey(itemCode))
                        {
                            rs.MoveNext();
                            continue;
                        }

                        cacheMeta.TryGetValue(itemCode, out var meta);

                        rows.Add(new InventoryStockRow
                        {
                            Key = $"{itemCode}|{warehouseCode}",
                            ItemCode = itemCode,
                            ItemName = itemName,
                            ArticleNumber = meta?.ArticleNumber ?? itemCode,
                            PrimaryBarcode = meta?.PrimaryBarcode,
                            WarehouseCode = warehouseCode,
                            WarehouseName = warehouseName,
                            OnHand = onHand,
                            Committed = committed,
                            Ordered = ordered,
                            Available = onHand - committed + ordered,
                            IsDeleted = false
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

        _states.TryGetValue(BuildStateKey(profileKey, includeZero, onlyLiquiMoly), out var previousState);
        var history = previousState?.History ?? new List<ChangeBatch>();
        var changedRows = BuildChanges(previousState?.Rows, rows);

        history.Add(new ChangeBatch(version, now, changedRows));
        const int maxHistory = 120; // Keep roughly last 20 minutes at 10s refresh cadence.
        if (history.Count > maxHistory)
            history = history.Skip(history.Count - maxHistory).ToList();

        _logger.LogInformation(
            "SapLiquiMolyInventoryReader: snapshot refreshed | Profile={Profile} | Rows={Rows} | Changed={Changed} | Version={Version}",
            profileKey, rows.Count, changedRows.Count, version);

        return new SnapshotState
        {
            Version = version,
            AsOfUtc = now,
            Rows = rows,
            History = history
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
                || !string.Equals(old.PrimaryBarcode, row.PrimaryBarcode, StringComparison.Ordinal))
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
                ArticleNumber = old.ArticleNumber,
                PrimaryBarcode = old.PrimaryBarcode,
                WarehouseCode = old.WarehouseCode,
                WarehouseName = old.WarehouseName,
                OnHand = 0,
                Committed = 0,
                Ordered = 0,
                Available = 0,
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
                || (!string.IsNullOrWhiteSpace(x.PrimaryBarcode)
                    && x.PrimaryBarcode.Contains(needle, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(x.ItemName)
                    && x.ItemName.Contains(needle, StringComparison.OrdinalIgnoreCase)));
        }

        return query;
    }

    private static string BuildStateKey(string profileKey, bool includeZero, bool onlyLiquiMoly) =>
        $"{profileKey}|{includeZero}|{onlyLiquiMoly}";

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
        var value = rs.Fields.Item(fieldName).Value;
        var text = value?.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static int ReadInt(Recordset rs, string fieldName) =>
        Convert.ToInt32(rs.Fields.Item(fieldName).Value);

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

    private sealed class CacheMeta
    {
        public string ArticleNumber { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? PrimaryBarcode { get; init; }
    }

    private sealed class SnapshotState
    {
        public long Version { get; init; }
        public DateTime AsOfUtc { get; init; }
        public List<InventoryStockRow> Rows { get; init; } = new();
        public List<ChangeBatch> History { get; init; } = new();
    }

    private sealed record ChangeBatch(long Version, DateTime AsOfUtc, List<InventoryStockRow> Rows);
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
}

public class InventoryStockRow
{
    public string Key { get; init; } = string.Empty;
    public string ItemCode { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public string ArticleNumber { get; init; } = string.Empty;
    public string? PrimaryBarcode { get; init; }
    public string WarehouseCode { get; init; } = string.Empty;
    public string? WarehouseName { get; init; }
    public decimal OnHand { get; init; }
    public decimal Committed { get; init; }
    public decimal Ordered { get; init; }
    public decimal Available { get; init; }
    public bool IsDeleted { get; init; }
}

public class InventoryMovementResponse
{
    public string ItemCode { get; init; } = string.Empty;
    public string? WarehouseCode { get; init; }
    public DateTime AsOfUtc { get; init; }
    public List<InventoryMovementRow> Rows { get; init; } = new();
}

public class InventoryMovementRow
{
    public string SourceType { get; init; } = string.Empty; // SO, DLV, TRQ, TRF, GR, GI
    public string DocType { get; init; } = string.Empty; // Alias for frontend drilldown routing.
    public int DocEntry { get; init; }
    public string DocNum { get; init; } = string.Empty;
    public DateTime? MovementDate { get; init; }
    public int LineNum { get; init; }
    public string ItemCode { get; init; } = string.Empty;
    public string? ItemName { get; init; }
    public string? MovementWarehouse { get; init; }
    public string? SourceWarehouse { get; init; }
    public string? TargetWarehouse { get; init; }
    public decimal Quantity { get; init; }
    public string Direction { get; init; } = string.Empty; // IN, OUT
    public string? DocumentStatus { get; init; }
    public string? PartnerCode { get; init; }
    public string? PartnerName { get; init; }
}
