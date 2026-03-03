using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Neon;
using MolasLubes.Infrastructure.Common;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Sync;

public class PriceListNeonSyncService
{
    private readonly MolasCacheDbContext _cacheDb;
    private readonly NeonDbContext _neonDb;
    private readonly ILogger<PriceListNeonSyncService> _logger;

    public PriceListNeonSyncService(
        MolasCacheDbContext cacheDb,
        NeonDbContext neonDb,
        ILogger<PriceListNeonSyncService> logger)
    {
        _cacheDb = cacheDb;
        _neonDb = neonDb;
        _logger = logger;
    }

    // =====================================================
    // 💰 PRICE LIST DELTA SYNC (CACHE → NEON)
    // =====================================================
    public async Task SyncAsync()
    {
        _logger.LogInformation("💰 Neon PRICE LIST sync started");

        var strategy = _neonDb.Database.CreateExecutionStrategy();
        var nowUtc = DateTime.UtcNow;

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _neonDb.Database.BeginTransactionAsync();

            // -------------------------------------------------
            // 1️⃣ READ SOURCE FROM CACHE (STRONGLY TYPED)
            // CacheProducts has a composite PK (ItemCode + Warehouse) — multiple rows
            // per ItemCode exist (one per warehouse). Prices are product-level, not
            // warehouse-level, so we deduplicate by ItemCode after fetching.
            // -------------------------------------------------
            var source = (await _cacheDb.CacheProducts
                .AsNoTracking()
                .Select(p => new PriceSourceRow
                {
                    ItemCode = p.ItemCode,
                    PriceList_1 = p.PriceList_1,
                    PriceList_2 = p.PriceList_2,
                    PriceList_3 = p.PriceList_3,

                    OdooPricelistId = p.OdooPricelistId,
                    OdooStatus = p.OdooStatus,
                    OdooSyncDir = p.OdooSyncDir,
                    OdooErrorMsg = p.OdooErrorMsg,
                    OdooLastSync = p.OdooLastSync
                })
                .ToListAsync())
                .DistinctBy(p => p.ItemCode)
                .ToList();

            if (source.Count == 0)
            {
                _logger.LogInformation("ℹ No products found for price list sync");
                return;
            }

            // -------------------------------------------------
            // 2️⃣ EXPAND TO PRICE LIST ROWS (IN MEMORY)
            // -------------------------------------------------
            var prices = source
                .SelectMany(p => new[]
                {
                    BuildPrice(p, 1, p.PriceList_1, nowUtc),
                    BuildPrice(p, 2, p.PriceList_2, nowUtc),
                    BuildPrice(p, 3, p.PriceList_3, nowUtc)
                })
                .Where(x => x.Price > 0)
                .ToList();

            if (prices.Count == 0)
            {
                _logger.LogInformation("ℹ No price list rows generated");
                return;
            }

            // -------------------------------------------------
            // 3️⃣ LOAD EXISTING (SAFE UPSERT)
            // -------------------------------------------------
            var itemCodes = prices
                .Select(p => p.ItemCode)
                .Distinct()
                .ToList();

            var existing = await _neonDb.PriceLists
                .Where(x => itemCodes.Contains(x.ItemCode))
                .ToListAsync();

            var map = existing.ToDictionary(
                x => (x.ItemCode, x.PriceList),
                x => x);

            // -------------------------------------------------
            // 4️⃣ UPSERT
            // -------------------------------------------------
            foreach (var incoming in prices)
            {
                var key = (incoming.ItemCode, incoming.PriceList);

                if (!map.TryGetValue(key, out var entity))
                {
                    _neonDb.PriceLists.Add(incoming);
                }
                else
                {
                    entity.Price = incoming.Price;

                    entity.OdooPricelistId = incoming.OdooPricelistId;
                    entity.OdooStatus = incoming.OdooStatus;
                    entity.OdooSyncDir = incoming.OdooSyncDir;
                    entity.OdooErrorMsg = incoming.OdooErrorMsg;
                    entity.OdooLastSync = incoming.OdooLastSync.AsUtc();

                    entity.SyncedAt = nowUtc;
                }
            }

            await _neonDb.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "✅ Neon PRICE LIST sync completed | Rows={Count}",
                prices.Count);
        });
    }

    // =====================================================
    // 🧠 HELPER (UTC SAFE, STRONGLY TYPED)
    // =====================================================
    private static NeonPriceList BuildPrice(
        PriceSourceRow p,
        int priceList,
        decimal? price,
        DateTime nowUtc)
    {
        return new NeonPriceList
        {
            ItemCode = p.ItemCode,
            PriceList = priceList,
            Price = price ?? 0m,

            OdooPricelistId = p.OdooPricelistId,
            OdooStatus = p.OdooStatus,
            OdooSyncDir = p.OdooSyncDir,
            OdooErrorMsg = p.OdooErrorMsg,
            OdooLastSync = p.OdooLastSync.AsUtc(),

            SyncedAt = nowUtc
        };
    }
}

// =====================================================
// 🔒 INTERNAL DTO (SERVICE-SCOPED)
// =====================================================
internal sealed class PriceSourceRow
{
    public string ItemCode { get; set; } = null!;
    public decimal? PriceList_1 { get; set; }
    public decimal? PriceList_2 { get; set; }
    public decimal? PriceList_3 { get; set; }

    public string? OdooPricelistId { get; set; }
    public string? OdooStatus { get; set; }
    public string? OdooSyncDir { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }
}