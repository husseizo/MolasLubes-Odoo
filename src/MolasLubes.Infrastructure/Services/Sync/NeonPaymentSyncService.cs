using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Neon;
using MolasLubes.Infrastructure.Common;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Sync;

public class NeonPaymentSyncService
{
    private readonly MolasCacheDbContext _cacheDb;
    private readonly NeonDbContext _neonDb;
    private readonly ILogger<NeonPaymentSyncService> _logger;

    public NeonPaymentSyncService(
        MolasCacheDbContext cacheDb,
        NeonDbContext neonDb,
        ILogger<NeonPaymentSyncService> logger)
    {
        _cacheDb = cacheDb;
        _neonDb = neonDb;
        _logger = logger;
    }

    // =====================================================
    // 💳 PAYMENT DELTA SYNC (CACHE → NEON)
    // =====================================================
    public async Task SyncDeltaAsync()
    {
        _logger.LogInformation("💳 Neon PAYMENT DELTA sync started");

        var strategy = _neonDb.Database.CreateExecutionStrategy();
        var now = DateTime.UtcNow;

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _neonDb.Database.BeginTransactionAsync();

            try
            {
                // -------------------------------------------------
                // 1️⃣ GET LAST SYNC (UTC SAFE)
                // -------------------------------------------------
                var lastSync = await _neonDb.Payments
                    .OrderByDescending(x => x.SyncedAt)
                    .Select(x => x.SyncedAt)
                    .FirstOrDefaultAsync();

                if (lastSync == default)
                    lastSync = DateTime.MinValue;

                lastSync = lastSync.AsUtc();

                // -------------------------------------------------
                // 2️⃣ READ FROM CACHE (SQL SERVER)
                // -------------------------------------------------
                var cachePayments = await _cacheDb.CachePayment
                    .AsNoTracking()
                    .Where(x => x.CachedAt > lastSync)
                    .ToListAsync();

                if (cachePayments.Count == 0)
                {
                    _logger.LogInformation("ℹ No new payments in cache");
                    await tx.CommitAsync();
                    return;
                }

                // -------------------------------------------------
                // 3️⃣ LOAD ONLY REFERENCED INVOICE KEYS FROM NEON (OPTIMIZED)
                // ⚠️ CRITICAL: Only load invoice keys that are actually referenced
                // by the cache payments instead of ALL invoices (huge performance gain)
                // -------------------------------------------------
                var referencedInvoiceKeys = cachePayments
                    .Where(p => p.InvoiceDocEntry > 0)
                    .Select(p => p.InvoiceDocEntry)
                    .Distinct()
                    .ToList();

                if (referencedInvoiceKeys.Count == 0)
                {
                    _logger.LogInformation("ℹ No payments reference valid invoices");
                    await tx.CommitAsync();
                    return;
                }

                var invoiceKeySet = (await _neonDb.Invoices
                    .AsNoTracking()
                    .Where(i => referencedInvoiceKeys.Contains(i.SapDocEntry))
                    .Select(x => x.SapDocEntry)
                    .ToListAsync())
                    .ToHashSet();

                // -------------------------------------------------
                // 4️⃣ FILTER PAYMENTS (FK SAFE)
                // Only sync payments that have a known invoice link
                // (InvoiceDocEntry > 0) AND whose invoice header already
                // exists in Neon so the FK constraint is satisfied.
                // -------------------------------------------------
                var orphanedCount = 0;
                var payments = new List<NeonPayment>();

                foreach (var p in cachePayments)
                {
                    // Skip invalid invoice references
                    if (p.InvoiceDocEntry <= 0)
                        continue;

                    // ⚠️ FK SAFETY CHECK
                    if (!invoiceKeySet.Contains(p.InvoiceDocEntry))
                    {
                        orphanedCount++;
                        _logger.LogDebug(
                            "⚠️ Skipping orphaned payment | PaymentEntry={PaymentEntry} InvoiceEntry={InvoiceEntry} (invoice not in Neon yet)",
                            p.SapDocEntry,
                            p.InvoiceDocEntry);
                        continue;
                    }

                    payments.Add(new NeonPayment
                    {
                        SapDocEntry  = p.SapDocEntry,
                        DocNum       = p.SapDocNum,
                        CustomerCode = p.CardCode,
                        InvoiceEntry = p.InvoiceDocEntry,
                        PaymentDate  = p.DocDate.AsUtc(),
                        Amount       = p.SumApplied > 0 ? p.SumApplied : p.TotalPaid,

                        OdooPaymentId = p.OdooPaymentId,
                        OdooStatus    = p.OdooStatus,
                        OdooSyncDir   = p.OdooSyncDir,
                        OdooErrorMsg  = p.OdooErrorMsg,
                        OdooLastSync  = p.OdooLastSync.AsUtc(),

                        SyncedAt = now
                    });
                }

                if (payments.Count == 0)
                {
                    _logger.LogInformation(
                        "ℹ No FK-safe payments to sync | Orphaned={Orphaned}",
                        orphanedCount);
                    await tx.CommitAsync();
                    return;
                }

                _logger.LogInformation(
                    "💳 Processing {Count} payments in batches to prevent timeout",
                    payments.Count);

                // -------------------------------------------------
                // 5️⃣ UPSERT PAYMENTS (BATCHED)
                // -------------------------------------------------
                const int batchSize = 1000; // Process 1000 payments at a time
                var paymentBatches = payments.Chunk(batchSize).ToList();

                var totalInserted = 0;
                var totalUpdated = 0;

                foreach (var batch in paymentBatches)
                {
                    var batchList = batch.ToList();
                    var keys = batchList.Select(p => p.SapDocEntry).ToList();

                    var existing = await _neonDb.Payments
                        .Where(p => keys.Contains(p.SapDocEntry))
                        .ToDictionaryAsync(p => p.SapDocEntry);

                    var inserted = 0;
                    var updated = 0;

                    foreach (var incoming in batchList)
                    {
                        if (!existing.TryGetValue(incoming.SapDocEntry, out var entity))
                        {
                            _neonDb.Payments.Add(incoming);
                            inserted++;
                        }
                        else
                        {
                            entity.DocNum        = incoming.DocNum;
                            entity.CustomerCode  = incoming.CustomerCode;
                            entity.InvoiceEntry  = incoming.InvoiceEntry;
                            entity.PaymentDate   = incoming.PaymentDate;
                            entity.Amount        = incoming.Amount;

                            entity.OdooPaymentId = incoming.OdooPaymentId;
                            entity.OdooStatus    = incoming.OdooStatus;
                            entity.OdooSyncDir   = incoming.OdooSyncDir;
                            entity.OdooErrorMsg  = incoming.OdooErrorMsg;
                            entity.OdooLastSync  = incoming.OdooLastSync;

                            entity.SyncedAt = now;
                            updated++;
                        }
                    }

                    totalInserted += inserted;
                    totalUpdated += updated;

                    _logger.LogDebug(
                        "✅ Payment batch processed | Inserted={Inserted} Updated={Updated}",
                        inserted,
                        updated);
                }

                await _neonDb.SaveChangesAsync();

                // -------------------------------------------------
                // 6️⃣ REFRESH PaidAmount / IsPaid ON NEON INVOICES (BATCHED)
                // -------------------------------------------------
                var affectedInvoices = payments.Select(p => p.InvoiceEntry).Distinct().ToList();

                _logger.LogDebug(
                    "🔄 Refreshing payment state for {Count} affected invoices",
                    affectedInvoices.Count);

                // Process invoice updates in batches too
                var invoiceBatches = affectedInvoices.Chunk(500).ToList();

                foreach (var invoiceBatch in invoiceBatches)
                {
                    var batchKeys = invoiceBatch.ToList();

                    var invoicesToUpdate = await _neonDb.Invoices
                        .Where(i => batchKeys.Contains(i.SapDocEntry))
                        .ToListAsync();

                    var paidTotals = await _neonDb.Payments
                        .Where(p => batchKeys.Contains(p.InvoiceEntry))
                        .GroupBy(p => p.InvoiceEntry)
                        .Select(g => new { InvoiceEntry = g.Key, Total = g.Sum(p => p.Amount) })
                        .ToDictionaryAsync(x => x.InvoiceEntry, x => x.Total);

                    foreach (var inv in invoicesToUpdate)
                    {
                        inv.PaidAmount = paidTotals.GetValueOrDefault(inv.SapDocEntry, 0m);
                        inv.IsPaid = inv.PaidAmount >= inv.DocTotal && inv.DocTotal > 0;
                    }
                }

                await _neonDb.SaveChangesAsync();
                await tx.CommitAsync();

                _logger.LogInformation(
                    "✅ Neon PAYMENT DELTA completed | Total={Total} | Inserted={Inserted} | Updated={Updated} | Orphaned={Orphaned} | Batches={Batches}",
                    payments.Count,
                    totalInserted,
                    totalUpdated,
                    orphanedCount);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex,
                    "❌ Neon PAYMENT DELTA sync FAILED - transaction rolled back");
                throw;
            }
        });
    }
}