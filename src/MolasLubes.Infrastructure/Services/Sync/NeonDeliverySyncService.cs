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
                    SapDocEntry    = x.SapDocEntry,
                    SapDocNum      = x.SapDocNum,
                    CardCode       = x.CardCode,
                    DeliveryDate   = x.DeliveryDate.AsUtc(),
                    BaseOrderEntry = x.BaseOrderEntry,
                    DeliveredQty   = x.DeliveredQty,
                    IsCancelled    = x.IsCancelled,

                    // 🔗 ODOO UDFS (UTC SAFE)
                    OdooDeliveryId = x.OdooDeliveryId,
                    OdooStatus     = x.OdooStatus,
                    OdooSyncDir    = x.OdooSyncDir,
                    OdooErrorMsg   = x.OdooErrorMsg,
                    OdooLastSync   = x.OdooLastSync.AsUtc(),

                    SyncedAt = now
                })
                .ToListAsync();

            if (deliveries.Count == 0)
            {
                _logger.LogInformation("ℹ No delivery changes for Neon");
                return;
            }

            // -------------------------------------------------
            // 3️⃣ UPSERT HEADERS
            // -------------------------------------------------
            var keys = deliveries.Select(d => d.SapDocEntry).ToList();

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
                    entity.SapDocNum      = incoming.SapDocNum;
                    entity.CardCode       = incoming.CardCode;
                    entity.DeliveryDate   = incoming.DeliveryDate;
                    entity.BaseOrderEntry = incoming.BaseOrderEntry;
                    entity.DeliveredQty   = incoming.DeliveredQty;
                    entity.IsCancelled    = incoming.IsCancelled;

                    entity.OdooDeliveryId = incoming.OdooDeliveryId;
                    entity.OdooStatus     = incoming.OdooStatus;
                    entity.OdooSyncDir    = incoming.OdooSyncDir;
                    entity.OdooErrorMsg   = incoming.OdooErrorMsg;
                    entity.OdooLastSync   = incoming.OdooLastSync.AsUtc();

                    entity.SyncedAt = now;
                }
            }

            // -------------------------------------------------
            // 4️⃣ SYNC LINES (DLN1)
            // -------------------------------------------------
            var cacheLines = await _cacheDb.CacheDeliveryLines
                .AsNoTracking()
                .Where(l => keys.Contains(l.SapDocEntry))
                .Select(l => new NeonDeliveryLine
                {
                    DeliveryEntry        = l.SapDocEntry,
                    LineNum              = l.LineNum,
                    ItemCode             = l.ItemCode,
                    Description          = l.Description,
                    Quantity             = l.Quantity,
                    LineTotal            = l.LineTotal,
                    GrossBuyPr           = l.GrossBuyPr,
                    BaseEntry            = l.BaseEntry,
                    BaseLine             = l.BaseLine,
                    OdooMoveId           = l.OdooMoveId,
                    OdooSalesOrderLineId = l.OdooSalesOrderLineId,
                    OdooStatus           = l.OdooStatus,
                    OdooSyncDir          = l.OdooSyncDir,
                    OdooErrorMsg         = l.OdooErrorMsg,
                    OdooLastSync         = l.OdooLastSync.AsUtc()
                })
                .ToListAsync();

            // DELETE existing lines for affected deliveries then INSERT fresh
            var existingLines = await _neonDb.DeliveryLines
                .Where(l => keys.Contains(l.DeliveryEntry))
                .ToListAsync();

            _neonDb.DeliveryLines.RemoveRange(existingLines);
            _neonDb.DeliveryLines.AddRange(cacheLines);

            await _neonDb.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "✅ Neon DELIVERY DELTA sync completed | Headers={Count} Lines={Lines}",
                deliveries.Count,
                cacheLines.Count);
        });

        // -------------------------------------------------
        // 5️⃣ ORPHAN LINE BACKFILL
        // Deliveries synced before line support was added
        // have headers but no lines — backfill them now.
        // -------------------------------------------------
        await SyncOrphanedLinesAsync();
    }

    // =====================================================
    // 🔥 DELIVERY FULL SYNC (CACHE → NEON)
    // =====================================================
    public async Task SyncFullAsync()
    {
        _logger.LogInformation("🔥 Neon DELIVERY FULL sync started");

        var strategy = _neonDb.Database.CreateExecutionStrategy();
        var now = DateTime.UtcNow;

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _neonDb.Database.BeginTransactionAsync();

            // -------------------------------------------------
            // 1️⃣ READ ALL FROM CACHE
            // -------------------------------------------------
            var deliveries = await _cacheDb.CacheDeliveries
                .AsNoTracking()
                .Select(x => new NeonDelivery
                {
                    SapDocEntry    = x.SapDocEntry,
                    SapDocNum      = x.SapDocNum,
                    CardCode       = x.CardCode,
                    DeliveryDate   = x.DeliveryDate.AsUtc(),
                    BaseOrderEntry = x.BaseOrderEntry,
                    DeliveredQty   = x.DeliveredQty,
                    IsCancelled    = x.IsCancelled,

                    OdooDeliveryId = x.OdooDeliveryId,
                    OdooStatus     = x.OdooStatus,
                    OdooSyncDir    = x.OdooSyncDir,
                    OdooErrorMsg   = x.OdooErrorMsg,
                    OdooLastSync   = x.OdooLastSync.AsUtc(),

                    SyncedAt = now
                })
                .ToListAsync();

            if (deliveries.Count == 0)
            {
                _logger.LogInformation("ℹ No deliveries in cache for full sync");
                return;
            }

            // -------------------------------------------------
            // 2️⃣ UPSERT HEADERS
            // -------------------------------------------------
            var keys = deliveries.Select(d => d.SapDocEntry).ToList();

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
                    entity.SapDocNum      = incoming.SapDocNum;
                    entity.CardCode       = incoming.CardCode;
                    entity.DeliveryDate   = incoming.DeliveryDate;
                    entity.BaseOrderEntry = incoming.BaseOrderEntry;
                    entity.DeliveredQty   = incoming.DeliveredQty;
                    entity.IsCancelled    = incoming.IsCancelled;

                    entity.OdooDeliveryId = incoming.OdooDeliveryId;
                    entity.OdooStatus     = incoming.OdooStatus;
                    entity.OdooSyncDir    = incoming.OdooSyncDir;
                    entity.OdooErrorMsg   = incoming.OdooErrorMsg;
                    entity.OdooLastSync   = incoming.OdooLastSync.AsUtc();

                    entity.SyncedAt = now;
                }
            }

            // -------------------------------------------------
            // 3️⃣ SYNC LINES (DLN1)
            // -------------------------------------------------
            var cacheLines = await _cacheDb.CacheDeliveryLines
                .AsNoTracking()
                .Where(l => keys.Contains(l.SapDocEntry))
                .Select(l => new NeonDeliveryLine
                {
                    DeliveryEntry        = l.SapDocEntry,
                    LineNum              = l.LineNum,
                    ItemCode             = l.ItemCode,
                    Description          = l.Description,
                    Quantity             = l.Quantity,
                    LineTotal            = l.LineTotal,
                    GrossBuyPr           = l.GrossBuyPr,
                    BaseEntry            = l.BaseEntry,
                    BaseLine             = l.BaseLine,
                    OdooMoveId           = l.OdooMoveId,
                    OdooSalesOrderLineId = l.OdooSalesOrderLineId,
                    OdooStatus           = l.OdooStatus,
                    OdooSyncDir          = l.OdooSyncDir,
                    OdooErrorMsg         = l.OdooErrorMsg,
                    OdooLastSync         = l.OdooLastSync.AsUtc()
                })
                .ToListAsync();

            var existingLines = await _neonDb.DeliveryLines
                .Where(l => keys.Contains(l.DeliveryEntry))
                .ToListAsync();

            _neonDb.DeliveryLines.RemoveRange(existingLines);
            _neonDb.DeliveryLines.AddRange(cacheLines);

            await _neonDb.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "✅ Neon DELIVERY FULL sync completed | Headers={Count} Lines={Lines}",
                deliveries.Count,
                cacheLines.Count);
        });
    }

    // =====================================================
    // 🩹 ORPHAN LINE BACKFILL
    // Syncs delivery lines that exist in cache but were
    // never propagated to Neon because the parent delivery
    // was originally synced before line support was added.
    // =====================================================
    public async Task SyncOrphanedLinesAsync()
    {
        // 1️⃣ Which delivery entries already have lines in Neon?
        var neonLineKeySet = (await _neonDb.DeliveryLines
            .AsNoTracking()
            .Select(l => l.DeliveryEntry)
            .Distinct()
            .ToListAsync())
            .ToHashSet();

        // 2️⃣ Which delivery entries have lines in cache?
        var cacheLineKeys = await _cacheDb.CacheDeliveryLines
            .AsNoTracking()
            .Select(l => l.SapDocEntry)
            .Distinct()
            .ToListAsync();

        // 3️⃣ Orphaned = in cache lines but NOT yet in Neon lines
        var orphanedKeys = cacheLineKeys
            .Where(k => !neonLineKeySet.Contains(k))
            .ToList();

        if (orphanedKeys.Count == 0)
        {
            _logger.LogDebug("✅ No orphaned delivery lines found");
            return;
        }

        // Confirm parent headers exist in Neon (safety check)
        var safeOrphanKeys = await _neonDb.Deliveries
            .AsNoTracking()
            .Where(d => orphanedKeys.Contains(d.SapDocEntry))
            .Select(d => d.SapDocEntry)
            .ToListAsync();

        if (safeOrphanKeys.Count == 0)
        {
            _logger.LogInformation(
                "⚠ Orphaned delivery lines found but parent headers missing in Neon | Count={Count}",
                orphanedKeys.Count);
            return;
        }

        _logger.LogInformation(
            "🩹 Backfilling orphaned delivery lines | Deliveries={Count}",
            safeOrphanKeys.Count);

        // 4️⃣ Load & insert inside a transaction
        var strategy = _neonDb.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _neonDb.Database.BeginTransactionAsync();

            var orphanLines = await _cacheDb.CacheDeliveryLines
                .AsNoTracking()
                .Where(l => safeOrphanKeys.Contains(l.SapDocEntry))
                .Select(l => new NeonDeliveryLine
                {
                    DeliveryEntry        = l.SapDocEntry,
                    LineNum              = l.LineNum,
                    ItemCode             = l.ItemCode,
                    Description          = l.Description,
                    Quantity             = l.Quantity,
                    LineTotal            = l.LineTotal,
                    GrossBuyPr           = l.GrossBuyPr,
                    BaseEntry            = l.BaseEntry,
                    BaseLine             = l.BaseLine,
                    OdooMoveId           = l.OdooMoveId,
                    OdooSalesOrderLineId = l.OdooSalesOrderLineId,
                    OdooStatus           = l.OdooStatus,
                    OdooSyncDir          = l.OdooSyncDir,
                    OdooErrorMsg         = l.OdooErrorMsg,
                    OdooLastSync         = l.OdooLastSync.AsUtc()
                })
                .ToListAsync();

            _neonDb.DeliveryLines.AddRange(orphanLines);
            await _neonDb.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "✅ Orphaned delivery lines backfilled | Deliveries={Deliveries} Lines={Lines}",
                safeOrphanKeys.Count,
                orphanLines.Count);
        });
    }
}
