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
                // 3️⃣ LOAD EXISTING INVOICE KEYS FROM NEON (POSTGRES)
                // ⚠️ CRITICAL: Re-fetch inside transaction to avoid race condition
                // -------------------------------------------------
                var invoiceKeys = await _neonDb.Invoices
                    .AsNoTracking()
                    .Select(x => x.SapDocEntry)
                    .ToListAsync();

                var invoiceKeySet = invoiceKeys.ToHashSet();

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

                // -------------------------------------------------
                // 5️⃣ UPSERT PAYMENTS
                // -------------------------------------------------
                var keys = payments.Select(p => p.SapDocEntry).ToList();

                var existing = await _neonDb.Payments
                    .Where(p => keys.Contains(p.SapDocEntry))
                    .ToDictionaryAsync(p => p.SapDocEntry);

                var inserted = 0;
                var updated = 0;

                foreach (var incoming in payments)
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

                await _neonDb.SaveChangesAsync();

                // -------------------------------------------------
                // 6️⃣ REFRESH PaidAmount / IsPaid ON NEON INVOICES
                // -------------------------------------------------
                var affectedInvoices = payments.Select(p => p.InvoiceEntry).Distinct().ToList();

                var invoicesToUpdate = await _neonDb.Invoices
                    .Where(i => affectedInvoices.Contains(i.SapDocEntry))
                    .ToListAsync();

                var paidTotals = await _neonDb.Payments
                    .Where(p => affectedInvoices.Contains(p.InvoiceEntry))
                    .GroupBy(p => p.InvoiceEntry)
                    .Select(g => new { InvoiceEntry = g.Key, Total = g.Sum(p => p.Amount) })
                    .ToDictionaryAsync(x => x.InvoiceEntry, x => x.Total);

                foreach (var inv in invoicesToUpdate)
                {
                    inv.PaidAmount = paidTotals.GetValueOrDefault(inv.SapDocEntry, 0m);
                    inv.IsPaid = inv.PaidAmount >= inv.DocTotal && inv.DocTotal > 0;
                }

                await _neonDb.SaveChangesAsync();
                await tx.CommitAsync();

                _logger.LogInformation(
                    "✅ Neon PAYMENT DELTA completed | Total={Total} | Inserted={Inserted} | Updated={Updated} | Orphaned={Orphaned}",
                    payments.Count,
                    inserted,
                    updated,
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