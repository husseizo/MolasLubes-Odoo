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
            // -------------------------------------------------
            var payments = cachePayments
                .Where(p => invoiceKeySet.Contains(p.SapDocEntry))
                .Select(p => new NeonPayment
                {
                    SapDocEntry = p.SapDocEntry,
                    DocNum = p.SapDocNum,
                    CustomerCode = p.CardCode,
                    PaymentDate = p.DocDate.AsUtc(),
                    Amount = p.TotalPaid,

                    OdooPaymentId = p.OdooPaymentId,
                    OdooStatus = p.OdooStatus,
                    OdooSyncDir = p.OdooSyncDir,
                    OdooErrorMsg = p.OdooErrorMsg,
                    OdooLastSync = p.OdooLastSync.AsUtc(),

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
                    entity.DocNum = incoming.DocNum;
                    entity.CustomerCode = incoming.CustomerCode;
                    entity.PaymentDate = incoming.PaymentDate;
                    entity.Amount = incoming.Amount;

                    entity.OdooPaymentId = incoming.OdooPaymentId;
                    entity.OdooStatus = incoming.OdooStatus;
                    entity.OdooSyncDir = incoming.OdooSyncDir;
                    entity.OdooErrorMsg = incoming.OdooErrorMsg;
                    entity.OdooLastSync = incoming.OdooLastSync;

                    entity.SyncedAt = now;
                    updated++;
                }
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