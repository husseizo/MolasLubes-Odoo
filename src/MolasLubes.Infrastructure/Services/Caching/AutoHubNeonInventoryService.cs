using Microsoft.EntityFrameworkCore;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Caching;

/// <summary>
/// Serves AutoHub inventory stock endpoints from the dedicated NeonAutoHubProducts table.
/// AutoHub deliveries are not migrated to Neon — those remain on SAP B1.
/// </summary>
public class AutoHubNeonInventoryService
{
    private const decimal LowStockThreshold   = 5m;
    private const decimal OutOfStockThreshold = 0m;
    private const string  Brand               = "AutoHub";

    private readonly AutoHubDbContext _db;

    public AutoHubNeonInventoryService(AutoHubDbContext db) => _db = db;

    // ── /inventory/stock ──────────────────────────────────────────────────

    public async Task<InventoryStockSnapshotResponse> GetStockAsync(
        string? search,
        string? warehouseCode,
        int skip,
        int take,
        bool includeZero,
        CancellationToken ct = default)
    {
        var q = BuildQuery(search, includeZero);

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
        var q       = BuildQuery(search: null, includeZero);
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
            var q     = BuildQuery(search, includeZero);
            var items = await q.OrderBy(p => p.ItemCode).ToListAsync(ct);
            rows = items.Select(ToStockRow).ToList();
        }
        else
        {
            var sinceTime = DateTimeOffset.FromUnixTimeMilliseconds(sinceVersion).UtcDateTime;

            var q = _db.AutoHubProducts
                .AsNoTracking()
                .Where(p => p.SyncedAt > sinceTime);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var needle = search.Trim();
                q = q.Where(p =>
                    p.ItemCode.Contains(needle) ||
                    p.ItemName.Contains(needle));
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

    // ── Private helpers ───────────────────────────────────────────────────

    private IQueryable<MolasLubes.Domain.Entities.Neon.NeonAutoHubProduct> BuildQuery(
        string? search,
        bool includeZero)
    {
        var q = _db.AutoHubProducts.AsNoTracking();

        if (!includeZero)
            q = q.Where(p => p.OnHandSap > 0);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim().ToLower();
            q = q.Where(p =>
                p.ItemCode.ToLower().Contains(needle) ||
                p.ItemName.ToLower().Contains(needle));
        }

        return q;
    }

    private static InventoryStockRow ToStockRow(MolasLubes.Domain.Entities.Neon.NeonAutoHubProduct p) =>
        new()
        {
            Key                 = $"{p.ItemCode}|",
            ItemCode            = p.ItemCode,
            ItemName            = p.ItemName,
            Brand               = Brand,
            ItemBrand           = Brand,
            PrimaryBarcode      = null,
            ItemGroup           = null,
            WarehouseCode       = "",
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

    private async Task<DateTime> MaxSyncedAtAsync(CancellationToken ct)
    {
        var max = await _db.AutoHubProducts
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
}
