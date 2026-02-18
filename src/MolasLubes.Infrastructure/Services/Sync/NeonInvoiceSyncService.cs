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
            // 2️⃣ READ FROM CACHE
            // -------------------------------------------------
            var invoices = await _cacheDb.CacheInvoices
                .AsNoTracking()
                .Where(x => x.CachedAt > lastSync)
                .Select(x => new NeonInvoice
                {
                    SapDocEntry = x.SapDocEntry,
                    DocNum = x.SapDocNum,
                    CustomerCode = x.CardCode,

                    InvoiceDate = x.DocDate.AsUtc(),
                    DocTotal = x.DocTotal,

                    // 💳 PAYMENT STATE (future-proof)
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
            // 3️⃣ UPSERT
            // -------------------------------------------------
            var keys = invoices
                .Select(i => i.SapDocEntry)
                .ToList();

            var existingMap = await _neonDb.Invoices
                .Where(i => keys.Contains(i.SapDocEntry))
                .ToDictionaryAsync(i => i.SapDocEntry);

            foreach (var incoming in invoices)
            {
                if (!existingMap.TryGetValue(incoming.SapDocEntry, out var entity))
                {
                    _neonDb.Invoices.Add(incoming);
                }
                else
                {
                    entity.DocNum = incoming.DocNum;
                    entity.CustomerCode = incoming.CustomerCode;
                    entity.InvoiceDate = incoming.InvoiceDate;
                    entity.DocTotal = incoming.DocTotal;

                    entity.PaidAmount = incoming.PaidAmount;
                    entity.IsPaid = incoming.IsPaid;

                    entity.OdooInvoiceId = incoming.OdooInvoiceId;
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
                "✅ Neon INVOICE DELTA sync completed | Count={Count}",
                invoices.Count);
        });
    }
}