using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Neon;
using MolasLubes.Infrastructure.Common;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Sync;

public class NeonInvoiceSyncService
{
    private readonly MolasCacheDbContext _cacheDb;
    private readonly NeonDbContext _neonDb;
    private readonly ILogger<NeonInvoiceSyncService> _logger;

    public NeonInvoiceSyncService(
        MolasCacheDbContext cacheDb,
        NeonDbContext neonDb,
        ILogger<NeonInvoiceSyncService> logger)
    {
        _cacheDb = cacheDb;
        _neonDb = neonDb;
        _logger = logger;
    }

    // =====================================================
    // 🧾 INVOICE DELTA SYNC (CACHE → NEON)
    // =====================================================
    public async Task SyncDeltaAsync()
    {
        _logger.LogInformation("🧾 Neon INVOICE DELTA sync started");

        var strategy = _neonDb.Database.CreateExecutionStrategy();
        var now = DateTime.UtcNow;

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _neonDb.Database.BeginTransactionAsync();

            // -------------------------------------------------
            // 1️⃣ SAFE LAST SYNC (UTC)
            // -------------------------------------------------
            var lastSync = await _neonDb.Invoices
                .OrderByDescending(x => x.SyncedAt)
                .Select(x => x.SyncedAt)
                .FirstOrDefaultAsync();

            if (lastSync == default)
                lastSync = DateTime.MinValue;

            // -------------------------------------------------
            // 2️⃣ READ FROM CACHE (HEADERS)
            // -------------------------------------------------
            var invoices = await _cacheDb.CacheInvoices
                .AsNoTracking()
                .Where(x => x.CachedAt > lastSync)
                .Select(x => new NeonInvoice
                {
                    SapDocEntry = x.SapDocEntry,
                    DocNum = x.SapDocNum,
                    CustomerCode = x.CardCode,
                    CardName = x.CardName,

                    InvoiceDate = x.DocDate.AsUtc(),
                    DocTotal = x.DocTotal,
                    VatSum = x.VatSum,

                    // 💳 PAYMENT STATE – refreshed below from NeonPayments
                    PaidAmount = 0m,
                    IsPaid = false,

                    // 🔗 ODOO UDFS (UTC SAFE)
                    OdooInvoiceId = x.OdooInvoiceId,
                    OdooStatus = x.OdooStatus,
                    OdooSyncDir = x.OdooSyncDir,
                    OdooErrorMsg = x.OdooErrorMsg,
                    OdooLastSync = x.OdooLastSync.AsUtc(),

                    SyncedAt = now
                })
                .ToListAsync();

            if (invoices.Count == 0)
            {
                _logger.LogInformation("ℹ No invoice changes for Neon");
                return;
            }

            // -------------------------------------------------
            // 3️⃣ UPSERT HEADERS
            // -------------------------------------------------
            var keys = invoices
                .Select(i => i.SapDocEntry)
                .ToList();

            var existingMap = await _neonDb.Invoices
                .Where(i => keys.Contains(i.SapDocEntry))
                .ToDictionaryAsync(i => i.SapDocEntry);

            // Carry forward existing PaidAmount/IsPaid so payment data isn't reset
            var paidLookup = await _neonDb.Payments
                .Where(p => keys.Contains(p.InvoiceEntry))
                .GroupBy(p => p.InvoiceEntry)
                .Select(g => new { InvoiceEntry = g.Key, PaidAmount = g.Sum(p => p.Amount) })
                .ToDictionaryAsync(x => x.InvoiceEntry, x => x.PaidAmount);

            foreach (var incoming in invoices)
            {
                var paidAmount = paidLookup.GetValueOrDefault(incoming.SapDocEntry, 0m);
                var isPaid = paidAmount >= incoming.DocTotal && incoming.DocTotal > 0;

                if (!existingMap.TryGetValue(incoming.SapDocEntry, out var entity))
                {
                    incoming.PaidAmount = paidAmount;
                    incoming.IsPaid = isPaid;
                    _neonDb.Invoices.Add(incoming);
                }
                else
                {
                    entity.DocNum = incoming.DocNum;
                    entity.CustomerCode = incoming.CustomerCode;
                    entity.CardName = incoming.CardName;
                    entity.InvoiceDate = incoming.InvoiceDate;
                    entity.DocTotal = incoming.DocTotal;
                    entity.VatSum = incoming.VatSum;

                    // Only refresh payment state if payments exist; otherwise preserve
                    entity.PaidAmount = paidLookup.ContainsKey(incoming.SapDocEntry)
                        ? paidAmount
                        : entity.PaidAmount;
                    entity.IsPaid = entity.PaidAmount >= entity.DocTotal && entity.DocTotal > 0;

                    entity.OdooInvoiceId = incoming.OdooInvoiceId;
                    entity.OdooStatus = incoming.OdooStatus;
                    entity.OdooSyncDir = incoming.OdooSyncDir;
                    entity.OdooErrorMsg = incoming.OdooErrorMsg;
                    entity.OdooLastSync = incoming.OdooLastSync.AsUtc();

                    entity.SyncedAt = now;
                }
            }

            // -------------------------------------------------
            // 4️⃣ SYNC LINES (INV1)
            // -------------------------------------------------
            var cacheLines = await _cacheDb.CacheInvoiceLines
                .AsNoTracking()
                .Where(l => keys.Contains(l.SapDocEntry))
                .Select(l => new NeonInvoiceLine
                {
                    InvoiceEntry = l.SapDocEntry,
                    ItemCode = l.ItemCode,
                    Description = l.Description,
                    Quantity = l.Quantity,
                    LineTotal = l.LineTotal,
                    GrossBuyPr = l.GrossBuyPr,
                    BaseEntry = l.BaseEntry,
                    BaseLine = l.BaseLine,
                    OdooInvoiceLineId = l.OdooInvoiceLineId,
                    OdooStatus = l.OdooStatus,
                    OdooSyncDir = l.OdooSyncDir,
                    OdooErrorMsg = l.OdooErrorMsg,
                    OdooLastSync = l.OdooLastSync.AsUtc()
                })
                .ToListAsync();

            // DELETE existing lines for affected invoices then INSERT fresh
            var existingLines = await _neonDb.InvoiceLines
                .Where(l => keys.Contains(l.InvoiceEntry))
                .ToListAsync();

            _neonDb.InvoiceLines.RemoveRange(existingLines);
            _neonDb.InvoiceLines.AddRange(cacheLines);

            await _neonDb.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "✅ Neon INVOICE DELTA sync completed | Headers={Count} Lines={Lines}",
                invoices.Count,
                cacheLines.Count);
        });

        // -------------------------------------------------
        // 5️⃣ ORPHAN LINE BACKFILL
        // Invoices that were synced to Neon before the
        // line-migration was added have headers but no lines.
        // -------------------------------------------------
        await SyncOrphanedLinesAsync();
    }

    // =====================================================
    // 🔥 INVOICE FULL SYNC (CACHE → NEON)
    // =====================================================
    public async Task SyncFullAsync()
    {
        _logger.LogInformation("🔥 Neon INVOICE FULL sync started");

        var strategy = _neonDb.Database.CreateExecutionStrategy();
        var now = DateTime.UtcNow;

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _neonDb.Database.BeginTransactionAsync();

            // -------------------------------------------------
            // 1️⃣ READ ALL FROM CACHE (HEADERS)
            // -------------------------------------------------
            var invoices = await _cacheDb.CacheInvoices
                .AsNoTracking()
                .Select(x => new NeonInvoice
                {
                    SapDocEntry = x.SapDocEntry,
                    DocNum = x.SapDocNum,
                    CustomerCode = x.CardCode,
                    CardName = x.CardName,

                    InvoiceDate = x.DocDate.AsUtc(),
                    DocTotal = x.DocTotal,
                    VatSum = x.VatSum,

                    PaidAmount = 0m,
                    IsPaid = false,

                    OdooInvoiceId = x.OdooInvoiceId,
                    OdooStatus = x.OdooStatus,
                    OdooSyncDir = x.OdooSyncDir,
                    OdooErrorMsg = x.OdooErrorMsg,
                    OdooLastSync = x.OdooLastSync.AsUtc(),

                    SyncedAt = now
                })
                .ToListAsync();

            if (invoices.Count == 0)
            {
                _logger.LogInformation("ℹ No invoices in cache for full sync");
                return;
            }

            // -------------------------------------------------
            // 2️⃣ UPSERT HEADERS
            // -------------------------------------------------
            var keys = invoices
                .Select(i => i.SapDocEntry)
                .ToList();

            var existingMap = await _neonDb.Invoices
                .Where(i => keys.Contains(i.SapDocEntry))
                .ToDictionaryAsync(i => i.SapDocEntry);

            // Carry forward payment state calculated from NeonPayments
            var paidLookup = await _neonDb.Payments
                .Where(p => keys.Contains(p.InvoiceEntry))
                .GroupBy(p => p.InvoiceEntry)
                .Select(g => new { InvoiceEntry = g.Key, PaidAmount = g.Sum(p => p.Amount) })
                .ToDictionaryAsync(x => x.InvoiceEntry, x => x.PaidAmount);

            foreach (var incoming in invoices)
            {
                var paidAmount = paidLookup.GetValueOrDefault(incoming.SapDocEntry, 0m);
                var isPaid = paidAmount >= incoming.DocTotal && incoming.DocTotal > 0;

                if (!existingMap.TryGetValue(incoming.SapDocEntry, out var entity))
                {
                    incoming.PaidAmount = paidAmount;
                    incoming.IsPaid = isPaid;
                    _neonDb.Invoices.Add(incoming);
                }
                else
                {
                    entity.DocNum = incoming.DocNum;
                    entity.CustomerCode = incoming.CustomerCode;
                    entity.CardName = incoming.CardName;
                    entity.InvoiceDate = incoming.InvoiceDate;
                    entity.DocTotal = incoming.DocTotal;
                    entity.VatSum = incoming.VatSum;

                    entity.PaidAmount = paidAmount;
                    entity.IsPaid = isPaid;

                    entity.OdooInvoiceId = incoming.OdooInvoiceId;
                    entity.OdooStatus = incoming.OdooStatus;
                    entity.OdooSyncDir = incoming.OdooSyncDir;
                    entity.OdooErrorMsg = incoming.OdooErrorMsg;
                    entity.OdooLastSync = incoming.OdooLastSync.AsUtc();

                    entity.SyncedAt = now;
                }
            }

            // -------------------------------------------------
            // 3️⃣ SYNC LINES (INV1)
            // -------------------------------------------------
            var cacheLines = await _cacheDb.CacheInvoiceLines
                .AsNoTracking()
                .Where(l => keys.Contains(l.SapDocEntry))
                .Select(l => new NeonInvoiceLine
                {
                    InvoiceEntry = l.SapDocEntry,
                    ItemCode = l.ItemCode,
                    Description = l.Description,
                    Quantity = l.Quantity,
                    LineTotal = l.LineTotal,
                    GrossBuyPr = l.GrossBuyPr,
                    BaseEntry = l.BaseEntry,
                    BaseLine = l.BaseLine,
                    OdooInvoiceLineId = l.OdooInvoiceLineId,
                    OdooStatus = l.OdooStatus,
                    OdooSyncDir = l.OdooSyncDir,
                    OdooErrorMsg = l.OdooErrorMsg,
                    OdooLastSync = l.OdooLastSync.AsUtc()
                })
                .ToListAsync();

            var existingLines = await _neonDb.InvoiceLines
                .Where(l => keys.Contains(l.InvoiceEntry))
                .ToListAsync();

            _neonDb.InvoiceLines.RemoveRange(existingLines);
            _neonDb.InvoiceLines.AddRange(cacheLines);

            await _neonDb.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "✅ Neon INVOICE FULL sync completed | Headers={Count} Lines={Lines}",
                invoices.Count,
                cacheLines.Count);
        });
    }

    // =====================================================
    // 🩹 ORPHAN LINE BACKFILL
    // Syncs invoice lines that exist in cache but were
    // never propagated to Neon because the parent invoice
    // was originally synced before line support was added.
    // =====================================================
    public async Task SyncOrphanedLinesAsync()
    {
        // 1️⃣ Which invoice entries already have lines in Neon?
        var neonLineKeys = await _neonDb.InvoiceLines
            .AsNoTracking()
            .Select(l => l.InvoiceEntry)
            .Distinct()
            .ToListAsync();

        var neonLineKeySet = neonLineKeys.ToHashSet();

        // 2️⃣ Which invoice entries have lines in cache?
        var cacheLineKeys = await _cacheDb.CacheInvoiceLines
            .AsNoTracking()
            .Select(l => l.SapDocEntry)
            .Distinct()
            .ToListAsync();

        // 3️⃣ Orphaned = in cache lines but NOT yet in Neon lines,
        //    AND the parent NeonInvoice header already exists
        var orphanedKeys = cacheLineKeys
            .Where(k => !neonLineKeySet.Contains(k))
            .ToList();

        if (orphanedKeys.Count == 0)
        {
            _logger.LogDebug("✅ No orphaned invoice lines found");
            return;
        }

        // Confirm parent headers exist in Neon (safety check)
        var neonInvoiceKeys = await _neonDb.Invoices
            .AsNoTracking()
            .Where(i => orphanedKeys.Contains(i.SapDocEntry))
            .Select(i => i.SapDocEntry)
            .ToListAsync();

        var safeOrphanKeys = neonInvoiceKeys; // only backfill where header exists

        if (safeOrphanKeys.Count == 0)
        {
            _logger.LogInformation(
                "⚠ Orphaned lines found but parent headers missing in Neon | Count={Count}",
                orphanedKeys.Count);
            return;
        }

        _logger.LogInformation(
            "🩹 Backfilling orphaned invoice lines | Invoices={Count}",
            safeOrphanKeys.Count);

        // 4️⃣ Load & insert the missing lines inside a transaction
        var strategy = _neonDb.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _neonDb.Database.BeginTransactionAsync();

            var orphanLines = await _cacheDb.CacheInvoiceLines
                .AsNoTracking()
                .Where(l => safeOrphanKeys.Contains(l.SapDocEntry))
                .Select(l => new NeonInvoiceLine
                {
                    InvoiceEntry = l.SapDocEntry,
                    ItemCode = l.ItemCode,
                    Description = l.Description,
                    Quantity = l.Quantity,
                    LineTotal = l.LineTotal,
                    GrossBuyPr = l.GrossBuyPr,
                    BaseEntry = l.BaseEntry,
                    BaseLine = l.BaseLine,
                    OdooInvoiceLineId = l.OdooInvoiceLineId,
                    OdooStatus = l.OdooStatus,
                    OdooSyncDir = l.OdooSyncDir,
                    OdooErrorMsg = l.OdooErrorMsg,
                    OdooLastSync = l.OdooLastSync.AsUtc()
                })
                .ToListAsync();

            _neonDb.InvoiceLines.AddRange(orphanLines);

            await _neonDb.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "✅ Orphaned invoice lines backfilled | Invoices={Invoices} Lines={Lines}",
                safeOrphanKeys.Count,
                orphanLines.Count);
        });
    }
}
