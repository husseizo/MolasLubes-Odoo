using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Neon;
using MolasLubes.Infrastructure.Common;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Sync;

public class ProductNeonSyncService
{
    private readonly MolasCacheDbContext _cacheDb;
    private readonly NeonDbContext _neonDb;
    private readonly ILogger<ProductNeonSyncService> _logger;

    public ProductNeonSyncService(
        MolasCacheDbContext cacheDb,
        NeonDbContext neonDb,
        ILogger<ProductNeonSyncService> logger)
    {
        _cacheDb = cacheDb;
        _neonDb = neonDb;
        _logger = logger;
    }

    // =====================================================
    // 🔄 PRODUCT DELTA SYNC (CACHE → NEON)
    // =====================================================
    public async Task SyncDeltaAsync()
    {
        _logger.LogInformation("🌍 Neon PRODUCT DELTA sync started");

        var strategy = _neonDb.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _neonDb.Database.BeginTransactionAsync();

            // -------------------------------------------------
            // 1️⃣ LAST SUCCESSFUL SYNC (NEON)
            // -------------------------------------------------
            var lastSync = await _neonDb.Products
                .OrderByDescending(x => x.SyncedAt)
                .Select(x => x.SyncedAt)
                .FirstOrDefaultAsync();

            if (lastSync == default)
                lastSync = DateTime.MinValue;

            lastSync = lastSync.AsUtc(); // ✅ CRITICAL

            // -------------------------------------------------
            // 2️⃣ HYBRID DELTA → UPSERT ALL CHANGED ACTIVE ITEMS
            //
            //    • ALL active items that changed since lastSync are
            //      pushed — regardless of AvailableCache value.
            //    • IsActive in Neon is DERIVED from stock level:
            //        IsActive = AvailableCache > 0
            //      so Odoo/Neon only shows orderable items, but the
            //      row is always present with an accurate stock snapshot.
            //    • "Healing" is automatic: when SAP replenishes stock,
            //      the next delta flips IsActive back to true with no
            //      separate sweep needed.
            //    • Stale zero-stock rows stay dormant in Neon (not
            //      deleted) so history and Odoo UDFs are preserved.
            // -------------------------------------------------
            // CacheProducts has a composite PK (ItemCode + Warehouse), so the same
            // ItemCode can appear in multiple rows (one per warehouse location).
            // Step 1: fetch the raw rows; Step 2: aggregate in-memory per ItemCode.
            var rawProducts = await _cacheDb.CacheProducts
                .AsNoTracking()
                .Where(x =>
                    x.IsActive &&
                    x.LastSapSyncAt > lastSync)
                .ToListAsync();

            var products = rawProducts
                .GroupBy(x => x.ItemCode)
                .Select(g =>
                {
                    var first = g.First();
                    var totalAvailable = g.Sum(x => x.AvailableCache);
                    return new NeonProduct
                    {
                        ItemCode = g.Key,
                        ItemName = first.ItemName,

                        OnHandSap      = g.Sum(x => x.OnHandSap),
                        AvailableCache = totalAvailable,

                        // ✅ HYBRID: orderable only when aggregate stock > 0
                        IsActive = totalAvailable > 0,

                        Barcode = first.Barcode,

                        // 🔗 ODOO UDFS — taken from the first warehouse row
                        OdooProductId = first.OdooProductId,
                        OdooStatus    = first.OdooStatus,
                        OdooSyncDir   = first.OdooSyncDir,
                        OdooErrorMsg  = first.OdooErrorMsg,
                        OdooLastSync  = first.OdooLastSync.AsUtc(),

                        // ✅ ALWAYS UTC
                        SyncedAt = DateTime.UtcNow
                    };
                })
                .ToList();

            int upserted = 0, deactivated = 0, reactivated = 0;

            if (products.Count > 0)
            {
                var itemCodes = products.Select(p => p.ItemCode).ToList();

                var existing = await _neonDb.Products
                    .Where(p => itemCodes.Contains(p.ItemCode))
                    .ToDictionaryAsync(p => p.ItemCode);

                foreach (var incoming in products)
                {
                    if (!existing.TryGetValue(incoming.ItemCode, out var entity))
                    {
                        _neonDb.Products.Add(incoming);
                        upserted++;
                    }
                    else
                    {
                        bool wasActive = entity.IsActive;

                        entity.ItemName        = incoming.ItemName;
                        entity.OnHandSap       = incoming.OnHandSap;
                        entity.AvailableCache  = incoming.AvailableCache;
                        entity.IsActive        = incoming.IsActive;
                        entity.Barcode         = incoming.Barcode;

                        // 🔗 ODOO UDFS
                        entity.OdooProductId = incoming.OdooProductId;
                        entity.OdooStatus    = incoming.OdooStatus;
                        entity.OdooSyncDir   = incoming.OdooSyncDir;
                        entity.OdooErrorMsg  = incoming.OdooErrorMsg;
                        entity.OdooLastSync  = incoming.OdooLastSync.AsUtc();

                        // ✅ ALWAYS UTC
                        entity.SyncedAt = DateTime.UtcNow;

                        upserted++;
                        if (wasActive && !entity.IsActive) deactivated++;
                        if (!wasActive && entity.IsActive)  reactivated++;
                    }
                }
            }

            await _neonDb.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "✅ Neon PRODUCT DELTA sync completed | Upserted={Upserted} | Deactivated={Deactivated} | Reactivated={Reactivated}",
                upserted, deactivated, reactivated);
        });
    }
}