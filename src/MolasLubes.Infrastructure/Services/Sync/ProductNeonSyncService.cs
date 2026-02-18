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
            // 2️⃣ READ DELTA FROM CACHE
            // -------------------------------------------------
            var products = await _cacheDb.CacheProducts
                .AsNoTracking()
                .Where(x =>
                    x.IsActive &&
                    x.LastSapSyncAt > lastSync)
                .Select(x => new NeonProduct
                {
                    ItemCode = x.ItemCode,
                    ItemName = x.ItemName,


                    OnHandSap = x.OnHandSap,
                    AvailableCache = x.AvailableCache,

                    IsActive = x.IsActive,

                    Barcode = x.Barcode,

                    // 🔗 ODOO UDFS
                    OdooProductId = x.OdooProductId,
                    OdooStatus = x.OdooStatus,
                    OdooSyncDir = x.OdooSyncDir,
                    OdooErrorMsg = x.OdooErrorMsg,
                    OdooLastSync = x.OdooLastSync.AsUtc(),

                    // ✅ ALWAYS UTC
                    SyncedAt = DateTime.UtcNow
                })
                .ToListAsync();

            if (products.Count == 0)
            {
                _logger.LogInformation("ℹ No product changes for Neon");
                return;
            }

            // -------------------------------------------------
            // 3️⃣ UPSERT INTO NEON
            // -------------------------------------------------
            var itemCodes = products
                .Select(p => p.ItemCode)
                .ToList();

            var existing = await _neonDb.Products
                .Where(p => itemCodes.Contains(p.ItemCode))
                .ToDictionaryAsync(p => p.ItemCode);

            foreach (var incoming in products)
            {
                if (!existing.TryGetValue(incoming.ItemCode, out var entity))
                {
                    _neonDb.Products.Add(incoming);
                }
                else
                {
                    entity.ItemName = incoming.ItemName;
                    entity.OnHandSap = incoming.OnHandSap;
                    entity.AvailableCache = incoming.AvailableCache;
                    entity.IsActive = incoming.IsActive;

                    // 🔗 ODOO UDFS
                    entity.OdooProductId = incoming.OdooProductId;
                    entity.OdooStatus = incoming.OdooStatus;
                    entity.OdooSyncDir = incoming.OdooSyncDir;
                    entity.OdooErrorMsg = incoming.OdooErrorMsg;
                    entity.OdooLastSync = incoming.OdooLastSync.AsUtc();

                    // ✅ ALWAYS UTC
                    entity.SyncedAt = DateTime.UtcNow;
                }
            }

            await _neonDb.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "✅ Neon PRODUCT DELTA sync completed | Count={Count}",
                products.Count);
        });
    }
}