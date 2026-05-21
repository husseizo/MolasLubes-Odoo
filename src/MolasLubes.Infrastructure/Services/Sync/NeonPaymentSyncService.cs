using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Neon;
using MolasLubes.Infrastructure.Common;
using MolasLubes.Infrastructure.Persistence;
using Npgsql;

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
    // PAYMENT DELTA SYNC (CACHE -> NEON)
    // =====================================================
    public async Task SyncDeltaAsync()
    {
        _logger.LogInformation("Neon PAYMENT DELTA sync started");

        try
        {
            var strategy = _neonDb.Database.CreateExecutionStrategy();
            var now = DateTime.UtcNow;

            await strategy.ExecuteAsync(async () =>
            {
                // -------------------------------------------------
                // 1) GET LAST SYNC (UTC SAFE)
                // -------------------------------------------------
                var lastSync = await TryGetLastPaymentSyncUtcAsync();
                if (lastSync is null)
                {
                    _logger.LogWarning(
                        "Skipping Neon PAYMENT DELTA sync because the Neon watermark could not be read safely");
                    return;
                }

                // -------------------------------------------------
                // 2) READ FROM CACHE (SQL SERVER)
                // -------------------------------------------------
                var cachePayments = await _cacheDb.CachePayment
                    .AsNoTracking()
                    .Where(x => x.CachedAt > lastSync.Value)
                    .ToListAsync();

                if (cachePayments.Count == 0)
                {
                    _logger.LogInformation("No new payments in cache");
                    return;
                }

                // -------------------------------------------------
                // 3) LOAD ONLY REFERENCED INVOICE KEYS FROM NEON
                // -------------------------------------------------
                var referencedInvoiceKeys = cachePayments
                    .Where(p => p.InvoiceDocEntry > 0)
                    .Select(p => p.InvoiceDocEntry)
                    .Distinct()
                    .ToList();

                if (referencedInvoiceKeys.Count == 0)
                {
                    _logger.LogInformation("No payments reference valid invoices");
                    return;
                }

                var invoiceKeySet = (await _neonDb.Invoices
                    .AsNoTracking()
                    .Where(i => referencedInvoiceKeys.Contains(i.SapDocEntry))
                    .Select(x => x.SapDocEntry)
                    .ToListAsync())
                    .ToHashSet();

                // -------------------------------------------------
                // 4) FILTER PAYMENTS (FK SAFE)
                // -------------------------------------------------
                var orphanedCount = 0;
                var payments = new List<NeonPayment>();

                foreach (var p in cachePayments)
                {
                    if (p.InvoiceDocEntry <= 0)
                        continue;

                    if (!invoiceKeySet.Contains(p.InvoiceDocEntry))
                    {
                        orphanedCount++;
                        _logger.LogDebug(
                            "Skipping orphaned payment | PaymentEntry={PaymentEntry} InvoiceEntry={InvoiceEntry} (invoice not in Neon yet)",
                            p.SapDocEntry,
                            p.InvoiceDocEntry);
                        continue;
                    }

                    payments.Add(new NeonPayment
                    {
                        SapDocEntry = p.SapDocEntry,
                        DocNum = p.SapDocNum,
                        CustomerCode = p.CardCode,
                        InvoiceEntry = p.InvoiceDocEntry,
                        PaymentDate = p.DocDate.AsUtc(),
                        Amount = p.SumApplied > 0 ? p.SumApplied : p.TotalPaid,
                        OdooPaymentId = p.OdooPaymentId,
                        OdooStatus = p.OdooStatus,
                        OdooSyncDir = p.OdooSyncDir,
                        OdooErrorMsg = p.OdooErrorMsg,
                        OdooLastSync = p.OdooLastSync.AsUtc(),
                        SyncedAt = now
                    });
                }

                if (payments.Count == 0)
                {
                    _logger.LogInformation(
                        "No FK-safe payments to sync | Orphaned={Orphaned}",
                        orphanedCount);
                    return;
                }

                _logger.LogInformation(
                    "Processing {Count} payments in batches to prevent timeout",
                    payments.Count);

                // -------------------------------------------------
                // 5) UPSERT PAYMENTS (BATCHED)
                // -------------------------------------------------
                const int batchSize = 1000;
                var paymentBatches = payments.Chunk(batchSize).ToList();
                var totalInserted = 0;
                var totalUpdated = 0;

                await using var tx = await _neonDb.Database.BeginTransactionAsync();

                try
                {
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
                                entity.DocNum = incoming.DocNum;
                                entity.CustomerCode = incoming.CustomerCode;
                                entity.InvoiceEntry = incoming.InvoiceEntry;
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

                        totalInserted += inserted;
                        totalUpdated += updated;

                        _logger.LogDebug(
                            "Payment batch processed | Inserted={Inserted} Updated={Updated}",
                            inserted,
                            updated);
                    }

                    await _neonDb.SaveChangesAsync();

                    // -------------------------------------------------
                    // 6) REFRESH PaidAmount / IsPaid ON NEON INVOICES
                    // -------------------------------------------------
                    var affectedInvoices = payments
                        .Select(p => p.InvoiceEntry)
                        .Distinct()
                        .ToList();

                    _logger.LogDebug(
                        "Refreshing payment state for {Count} affected invoices",
                        affectedInvoices.Count);

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
                        "Neon PAYMENT DELTA completed | Total={Total} | Inserted={Inserted} | Updated={Updated} | Orphaned={Orphaned} | Batches={Batches}",
                        payments.Count,
                        totalInserted,
                        totalUpdated,
                        orphanedCount,
                        paymentBatches.Count);
                }
                catch (Exception ex)
                {
                    await TryRollbackAsync(tx, ex, "Neon PAYMENT DELTA sync");
                    _logger.LogError(ex,
                        "Neon PAYMENT DELTA sync FAILED - transaction rolled back");
                    throw;
                }
            });
        }
        catch (Exception ex) when (IsTransientNeonStreamReadFailure(ex))
        {
            await ResetNeonConnectionAsync(
                ex,
                "Neon PAYMENT DELTA sync",
                "Clearing the Neon pool and skipping this run. The next scheduled execution will retry.");
        }
    }

    private async Task<DateTime?> TryGetLastPaymentSyncUtcAsync()
    {
        const int maxAttempts = 2;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var lastSync = await _neonDb.Payments
                    .AsNoTracking()
                    .OrderByDescending(x => x.SyncedAt)
                    .Select(x => x.SyncedAt)
                    .FirstOrDefaultAsync();

                if (lastSync == default)
                {
                    lastSync = DateTime.MinValue;
                }

                return lastSync.AsUtc();
            }
            catch (Exception ex) when (IsTransientNeonStreamReadFailure(ex) && attempt < maxAttempts)
            {
                await ResetNeonConnectionAsync(
                    ex,
                    "Neon PAYMENT watermark read",
                    "Clearing the Neon pool and retrying the watermark query on a fresh connection.");
            }
            catch (Exception ex) when (IsTransientNeonStreamReadFailure(ex))
            {
                await ResetNeonConnectionAsync(
                    ex,
                    "Neon PAYMENT watermark read",
                    "Clearing the Neon pool and skipping this payment sync run.");
                return null;
            }
        }

        return null;
    }

    private async Task TryRollbackAsync(
        IDbContextTransaction tx,
        Exception originalException,
        string operationName)
    {
        try
        {
            await tx.RollbackAsync();
        }
        catch (ObjectDisposedException rollbackEx)
        {
            _logger.LogWarning(rollbackEx,
                "{Operation} could not roll back because the Neon transaction was already disposed after the original failure",
                operationName);
        }
        catch (Exception rollbackEx) when (IsTransientNeonStreamReadFailure(rollbackEx))
        {
            _logger.LogWarning(rollbackEx,
                "{Operation} hit a transient Neon failure while rolling back after: {OriginalMessage}",
                operationName,
                originalException.Message);
        }
    }

    private async Task ResetNeonConnectionAsync(
        Exception ex,
        string operationName,
        string recoveryAction)
    {
        _logger.LogWarning(ex,
            "{Operation} hit a transient Neon stream read failure. {RecoveryAction}",
            operationName,
            recoveryAction);

        try
        {
            await _neonDb.Database.CloseConnectionAsync();

            if (_neonDb.Database.GetDbConnection() is NpgsqlConnection npgsqlConnection)
            {
                NpgsqlConnection.ClearPool(npgsqlConnection);
            }
        }
        catch (Exception resetEx)
        {
            _logger.LogWarning(resetEx,
                "Failed to reset the Neon PostgreSQL connection after {Operation}",
                operationName);
        }
    }

    private static bool IsTransientNeonStreamReadFailure(Exception ex)
    {
        if (ex is EndOfStreamException or IOException)
        {
            return true;
        }

        if (ex is NpgsqlException npgsqlException &&
            npgsqlException.Message.Contains(
                "Exception while reading from stream",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ex.InnerException is not null &&
               IsTransientNeonStreamReadFailure(ex.InnerException);
    }
}
