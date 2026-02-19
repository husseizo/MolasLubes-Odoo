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

            // -------------------------------------------------
            // 1️⃣ GET LAST SYNC
            // -------------------------------------------------
            var lastSync = await _neonDb.Payments
                .OrderByDescending(x => x.SyncedAt)
                .Select(x => x.SyncedAt)
                .FirstOrDefaultAsync();

            if (lastSync == default)
                lastSync = DateTime.MinValue;

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
            var payments = cachePayments
                .Where(p => p.InvoiceDocEntry > 0 && invoiceKeySet.Contains(p.InvoiceDocEntry))
                .Select(p => new NeonPayment
                {
                    SapDocEntry  = p.SapDocEntry,
                    DocNum       = p.SapDocNum,
                    CustomerCode = p.CardCode,
                    InvoiceEntry = p.InvoiceDocEntry,   // ✅ correct FK → NeonInvoice
                    PaymentDate  = p.DocDate.AsUtc(),
                    Amount       = p.SumApplied > 0 ? p.SumApplied : p.TotalPaid,

                    OdooPaymentId = p.OdooPaymentId,
                    OdooStatus    = p.OdooStatus,
                    OdooSyncDir   = p.OdooSyncDir,
                    OdooErrorMsg  = p.OdooErrorMsg,
                    OdooLastSync  = p.OdooLastSync.AsUtc(),

                    SyncedAt = now
                })
                .ToList();

            if (payments.Count == 0)
            {
                _logger.LogInformation("ℹ No FK-safe payments to sync");
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
                "✅ Neon PAYMENT DELTA completed | Total={Total} | Inserted={Inserted} | Updated={Updated}",
                payments.Count,
                inserted,
                updated);
        });
    }
}