using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Neon;
using MolasLubes.Infrastructure.Common;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Sync;

public class NeonDeliverySyncService
{
    private readonly MolasCacheDbContext _cacheDb;
    private readonly NeonDbContext _neonDb;
    private readonly ILogger<NeonDeliverySyncService> _logger;

    public NeonDeliverySyncService(
        MolasCacheDbContext cacheDb,
        NeonDbContext neonDb,
        ILogger<NeonDeliverySyncService> logger)
    {
        _cacheDb = cacheDb;
        _neonDb = neonDb;
        _logger = logger;
    }

    // =====================================================
    // 🚚 DELIVERY DELTA SYNC (CACHE → NEON)
    // =====================================================
    public async Task SyncDeltaAsync()
    {
        _logger.LogInformation("🚚 Neon DELIVERY DELTA sync started");

        var strategy = _neonDb.Database.CreateExecutionStrategy();
        var now = DateTime.UtcNow;

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _neonDb.Database.BeginTransactionAsync();

            // -------------------------------------------------
            // 1️⃣ SAFE LAST SYNC (UTC)
            // -------------------------------------------------
            var lastSync = await _neonDb.Deliveries
                .OrderByDescending(x => x.SyncedAt)
                .Select(x => x.SyncedAt)
                .FirstOrDefaultAsync();

            if (lastSync == default)
                lastSync = DateTime.MinValue;

            // -------------------------------------------------
            // 2️⃣ READ DELIVERIES FROM CACHE
            // -------------------------------------------------
            var deliveries = await _cacheDb.CacheDeliveries
                .AsNoTracking()
                .Where(x =>
                    x.LastSapSyncAt != null &&
                    x.LastSapSyncAt > lastSync)
                .Select(x => new NeonDelivery
                {
                    SapDocEntry = x.SapDocEntry,
                    SapDocNum = x.SapDocNum,

                    CardCode = x.CardCode,

                    DeliveryDate = x.DeliveryDate.AsUtc(),
                    BaseOrderEntry = x.BaseOrderEntry,
                    DeliveredQty = x.DeliveredQty,

                    IsCancelled = x.IsCancelled,

                    // 🔗 ODOO UDFS (UTC SAFE)
                    OdooDeliveryId = x.OdooDeliveryId,
                    OdooStatus = x.OdooStatus,
                    OdooSyncDir = x.OdooSyncDir,
                    OdooErrorMsg = x.OdooErrorMsg,
                    OdooLastSync = x.OdooLastSync.AsUtc(),

                    SyncedAt = now
                })
                .ToListAsync();

            if (deliveries.Count == 0)
            {
                _logger.LogInformation("ℹ No delivery changes for Neon");
                return;
            }

            // -------------------------------------------------
            // 3️⃣ UPSERT INTO NEON
            // -------------------------------------------------
            var keys = deliveries
                .Select(d => d.SapDocEntry)
                .ToList();

            var existingMap = await _neonDb.Deliveries
                .Where(d => keys.Contains(d.SapDocEntry))
                .ToDictionaryAsync(d => d.SapDocEntry);

            foreach (var incoming in deliveries)
            {
                if (!existingMap.TryGetValue(incoming.SapDocEntry, out var entity))
                {
                    _neonDb.Deliveries.Add(incoming);
                }
                else
                {
                    entity.SapDocNum = incoming.SapDocNum;
                    entity.CardCode = incoming.CardCode;
                    entity.DeliveryDate = incoming.DeliveryDate;
                    entity.BaseOrderEntry = incoming.BaseOrderEntry;
                    entity.DeliveredQty = incoming.DeliveredQty;
                    entity.IsCancelled = incoming.IsCancelled;

                    entity.OdooDeliveryId = incoming.OdooDeliveryId;
                    entity.OdooStatus = incoming.OdooStatus;
                    entity.OdooSyncDir = incoming.OdooSyncDir;
                    entity.OdooErrorMsg = incoming.OdooErrorMsg;
                    entity.OdooLastSync = incoming.OdooLastSync.AsUtc();

                    entity.SyncedAt = now;
                }
            }

            await _neonDb.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "✅ Neon DELIVERY DELTA sync completed | Count={Count}",
                deliveries.Count);
        });
    }
}