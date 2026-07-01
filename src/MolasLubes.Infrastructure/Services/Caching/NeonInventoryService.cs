using Microsoft.EntityFrameworkCore;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Caching;

/// <summary>
/// Serves MolasLubes inventory stock and delivery endpoints from Neon PostgreSQL.
/// Stock is aggregated per-item (no per-warehouse breakdown — NeonProduct is item-level).
/// Delivery lines carry no WhsCode so Warehouse is left empty.
/// AutoHub stock is handled by AutoHubNeonInventoryService.
/// </summary>
public class NeonInventoryService
{
    private const decimal LowStockThreshold   = 5m;
    private const decimal OutOfStockThreshold = 0m;

    private readonly NeonDbContext _db;

    public NeonInventoryService(NeonDbContext db) => _db = db;

    // ── /inventory/stock ──────────────────────────────────────────────────

    public async Task<InventoryStockSnapshotResponse> GetStockAsync(
        string? search,
        string? warehouseCode,
        int skip,
        int take,
        bool includeZero,
        CancellationToken ct = default)
    {
        var q = BuildStockQuery(search, warehouseCode, includeZero);

        var total   = await q.CountAsync(ct);
        var asOfUtc = await MaxSyncedAtAsync(ct);
        var version = ToVersion(asOfUtc);

        var items = await q
            .OrderBy(p => p.ItemCode)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return new InventoryStockSnapshotResponse
        {
            AsOfUtc = asOfUtc,
            Version = version,
            Total   = total,
            Rows    = items.Select(ToStockRow).ToList()
        };
    }

    // ── /inventory/stock/summary ──────────────────────────────────────────

    public async Task<InventoryStockSummaryResponse> GetSummaryAsync(
        string? warehouseCode,
        bool includeZero,
        CancellationToken ct = default)
    {
        var q       = BuildStockQuery(search: null, warehouseCode, includeZero);
        var asOfUtc = await MaxSyncedAtAsync(ct);
        var version = ToVersion(asOfUtc);

        var totals = await q
            .GroupBy(_ => 1)
            .Select(g => new
            {
                ItemCount   = g.Count(),
                TotalOnHand = (decimal?)g.Sum(p => p.OnHandSap) ?? 0m,
                TotalAvail  = (decimal?)g.Sum(p => p.AvailableCache) ?? 0m
            })
            .FirstOrDefaultAsync(ct);

        return new InventoryStockSummaryResponse
        {
            AsOfUtc            = asOfUtc,
            Version            = version,
            ItemWarehouseCount = totals?.ItemCount ?? 0,
            ItemCount          = totals?.ItemCount ?? 0,
            WarehouseCount     = 1,
            TotalOnHand        = totals?.TotalOnHand ?? 0m,
            TotalCommitted     = 0m,
            TotalOrdered       = 0m,
            TotalAvailable     = totals?.TotalOnHand ?? 0m,
            TotalNetAvailable  = totals?.TotalAvail ?? 0m
        };
    }

    // ── /inventory/stock/changes ──────────────────────────────────────────

    public async Task<InventoryStockChangesResponse> GetChangesAsync(
        string? search,
        string? warehouseCode,
        long sinceVersion,
        bool includeZero,
        CancellationToken ct = default)
    {
        var asOfUtc = await MaxSyncedAtAsync(ct);
        var version = ToVersion(asOfUtc);
        bool reset  = sinceVersion == 0;

        List<InventoryStockRow> rows;

        if (reset)
        {
            var q     = BuildStockQuery(search, warehouseCode, includeZero);
            var items = await q.OrderBy(p => p.ItemCode).ToListAsync(ct);
            rows = items.Select(ToStockRow).ToList();
        }
        else
        {
            var sinceTime = DateTimeOffset.FromUnixTimeMilliseconds(sinceVersion).UtcDateTime;

            var q = _db.Products
                .AsNoTracking()
                .Where(p => p.SyncedAt > sinceTime);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var needle = search.Trim();
                q = q.Where(p =>
                    p.ItemCode.Contains(needle) ||
                    p.ItemName.Contains(needle) ||
                    (p.Barcode != null && p.Barcode.Contains(needle)));
            }

            var items = await q.OrderBy(p => p.ItemCode).ToListAsync(ct);
            rows = items
                .Where(p => includeZero || p.OnHandSap > 0)
                .Select(ToStockRow)
                .ToList();
        }

        return new InventoryStockChangesResponse
        {
            AsOfUtc       = asOfUtc,
            Version       = version,
            SinceVersion  = sinceVersion,
            ResetRequired = reset,
            Rows          = rows
        };
    }

    // ── /inventory/deliveries ─────────────────────────────────────────────

    public async Task<InventoryDeliveryAggregateResponse> GetDeliveriesAsync(
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? search,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        var (resolvedFrom, resolvedTo) = ResolveDeliveryDateRange(dateFrom, dateTo);

        var rows = await BuildDeliveryAggregatesAsync(resolvedFrom, resolvedTo, search, ct);

        var paged = rows
            .OrderByDescending(r => r.LastDeliveredAt ?? DateTime.MinValue)
            .ThenByDescending(r => r.LastDeliveryDocEntry)
            .ThenBy(r => r.ItemCode, StringComparer.OrdinalIgnoreCase)
            .Skip(skip)
            .Take(take)
            .ToList();

        return new InventoryDeliveryAggregateResponse
        {
            DateFrom = resolvedFrom,
            DateTo   = resolvedTo,
            AsOfUtc  = DateTime.UtcNow,
            Version  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Total    = rows.Count,
            Rows     = paged
        };
    }

    // ── /inventory/deliveries/today-summary ──────────────────────────────

    public async Task<InventoryTodayDeliveryResponse> GetTodayDeliveriesAsync(
        CancellationToken ct = default)
    {
        var today   = GetDarEsSalaamBusinessDate();
        var rows    = await BuildDeliveryAggregatesAsync(today, today, search: null, ct);
        var asOfUtc = DateTime.UtcNow;

        var byItem = rows
            .GroupBy(r => r.ItemCode, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                return new InventoryTodayDeliveryItem
                {
                    ItemCode             = g.Key,
                    ItemName             = first.ItemName,
                    Brand                = first.Brand,
                    ArticleNumber        = first.ArticleNumber,
                    TanNumber            = first.TanNumber,
                    DeliveredQty         = g.Sum(r => r.DeliveredQty),
                    DeliveryCount        = g.Sum(r => r.DeliveryCount),
                    LastDeliveredAt      = g.Max(r => r.LastDeliveredAt),
                    LastDeliveryDocEntry = g.OrderByDescending(r => r.LastDeliveryDocEntry ?? 0).First().LastDeliveryDocEntry,
                    LastDeliveryDocNum   = g.OrderByDescending(r => r.LastDeliveryDocEntry ?? 0).First().LastDeliveryDocNum,
                    Warehouses           = g.Select(r => r.Warehouse).Where(w => !string.IsNullOrWhiteSpace(w)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                };
            })
            .OrderByDescending(x => x.LastDeliveredAt ?? DateTime.MinValue)
            .ThenByDescending(x => x.LastDeliveryDocEntry ?? 0)
            .ThenBy(x => x.ItemCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new InventoryTodayDeliveryResponse
        {
            AsOfUtc = asOfUtc,
            Version = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Date    = today,
            Total   = byItem.Count,
            Items   = byItem
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private IQueryable<MolasLubes.Domain.Entities.Neon.NeonProduct> BuildStockQuery(
        string? search,
        string? warehouseCode,
        bool includeZero)
    {
        var q = _db.Products.AsNoTracking();

        if (!includeZero)
            q = q.Where(p => p.OnHandSap > 0);

        if (!string.IsNullOrWhiteSpace(warehouseCode))
            q = q.Where(p => p.DefaultWarehouse != null &&
                              p.DefaultWarehouse.ToUpper() == warehouseCode.ToUpper());

        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim().ToLower();
            q = q.Where(p =>
                p.ItemCode.ToLower().Contains(needle) ||
                p.ItemName.ToLower().Contains(needle) ||
                (p.Barcode != null && p.Barcode.ToLower().Contains(needle)));
        }

        return q;
    }

    private static InventoryStockRow ToStockRow(MolasLubes.Domain.Entities.Neon.NeonProduct p) =>
        new()
        {
            Key                 = $"{p.ItemCode}|{p.DefaultWarehouse ?? ""}",
            ItemCode            = p.ItemCode,
            ItemName            = p.ItemName,
            Brand               = p.Brand,
            ItemBrand           = p.Brand,
            PrimaryBarcode      = p.Barcode,
            ItemGroup           = p.ItemGroupName,
            WarehouseCode       = p.DefaultWarehouse ?? "",
            WarehouseName       = null,
            OnHand              = p.OnHandSap,
            Committed           = 0m,
            Ordered             = 0m,
            Available           = p.AvailableCache,
            StockStatus         = ResolveStockStatus(p.OnHandSap, p.AvailableCache),
            LowStockThreshold   = LowStockThreshold,
            OutOfStockThreshold = OutOfStockThreshold,
            IsDeleted           = false
        };

    private async Task<List<InventoryDeliveryAggregateRow>> BuildDeliveryAggregatesAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? search,
        CancellationToken ct)
    {
        var tz      = GetDarEsSalaamTimeZone();
        var utcFrom = TimeZoneInfo.ConvertTimeToUtc(dateFrom.ToDateTime(TimeOnly.MinValue), tz);
        var utcTo   = TimeZoneInfo.ConvertTimeToUtc(dateTo.ToDateTime(TimeOnly.MaxValue), tz);

        var linesQuery = _db.DeliveryLines
            .AsNoTracking()
            .Where(l =>
                !l.Delivery!.IsCancelled &&
                l.Delivery.DeliveryDate >= utcFrom &&
                l.Delivery.DeliveryDate <= utcTo);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim().ToLower();
            linesQuery = linesQuery.Where(l =>
                l.ItemCode.ToLower().Contains(needle) ||
                l.Description.ToLower().Contains(needle));
        }

        var lines = await linesQuery
            .Select(l => new
            {
                l.ItemCode,
                l.Description,
                l.Quantity,
                l.Delivery!.CardCode,
                l.Delivery.DeliveryDate,
                l.Delivery.SapDocEntry,
                l.Delivery.SapDocNum
            })
            .ToListAsync(ct);

        if (lines.Count == 0)
            return new List<InventoryDeliveryAggregateRow>();

        var cardCodes = lines.Select(l => l.CardCode).Distinct().ToList();
        var customers = await _db.Customers
            .AsNoTracking()
            .Where(c => cardCodes.Contains(c.CardCode))
            .Select(c => new { c.CardCode, c.CardName })
            .ToDictionaryAsync(c => c.CardCode, c => c.CardName, ct);

        return lines
            .GroupBy(l => l.ItemCode, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var ordered      = g.OrderByDescending(l => l.DeliveryDate).ThenByDescending(l => l.SapDocEntry);
                var last         = ordered.First();
                var customerName = customers.TryGetValue(last.CardCode, out var cn) ? cn : null;

                return new InventoryDeliveryAggregateRow
                {
                    ItemCode             = g.Key,
                    ItemName             = g.First().Description,
                    ArticleNumber        = g.Key,
                    Warehouse            = "",
                    DeliveredQty         = g.Sum(l => l.Quantity),
                    DeliveryCount        = g.Select(l => l.SapDocEntry).Distinct().Count(),
                    LastDeliveredAt      = g.Max(l => (DateTime?)l.DeliveryDate),
                    LastDeliveryDocEntry = last.SapDocEntry,
                    LastDeliveryDocNum   = last.SapDocNum.ToString(),
                    CustomerCode         = last.CardCode,
                    CustomerName         = customerName,
                    AsOfUtc              = DateTime.UtcNow,
                    Version              = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
            })
            .ToList();
    }

    private async Task<DateTime> MaxSyncedAtAsync(CancellationToken ct)
    {
        var max = await _db.Products
            .AsNoTracking()
            .OrderByDescending(p => p.SyncedAt)
            .Select(p => (DateTime?)p.SyncedAt)
            .FirstOrDefaultAsync(ct);

        return max ?? DateTime.UtcNow;
    }

    private static long ToVersion(DateTime syncedAt) =>
        new DateTimeOffset(syncedAt, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private static string ResolveStockStatus(decimal onHand, decimal available)
    {
        if (onHand <= OutOfStockThreshold || available <= OutOfStockThreshold)
            return "OUT_OF_STOCK";
        if (onHand <= LowStockThreshold || available <= LowStockThreshold)
            return "LOW_STOCK";
        return "IN_STOCK";
    }

    private static (DateOnly From, DateOnly To) ResolveDeliveryDateRange(DateOnly? from, DateOnly? to)
    {
        if (!from.HasValue && !to.HasValue)
        {
            var today = GetDarEsSalaamBusinessDate();
            return (today, today);
        }
        var f = from ?? to!.Value;
        var t = to ?? from!.Value;
        return f <= t ? (f, t) : throw new ArgumentException("dateFrom must be <= dateTo.");
    }

    private static DateOnly GetDarEsSalaamBusinessDate()
    {
        var tz  = GetDarEsSalaamTimeZone();
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
        return DateOnly.FromDateTime(now.DateTime);
    }

    private static TimeZoneInfo GetDarEsSalaamTimeZone()
    {
        foreach (var id in new[] { "Africa/Dar_es_Salaam", "E. Africa Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }
}
