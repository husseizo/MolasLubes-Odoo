using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Neon;
using MolasLubes.Infrastructure.Common;
using MolasLubes.Infrastructure.Persistence;
using Npgsql;

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

    // Delta sync from cache into Neon.
    public async Task SyncDeltaAsync()
    {
        _logger.LogInformation("Neon INVOICE DELTA sync started");

        try
        {
            var strategy = _neonDb.Database.CreateExecutionStrategy();
            var now = DateTime.UtcNow;
            var hasSyncedHeaders = false;

            await strategy.ExecuteAsync(async () =>
            {
                var lastSync = await TryGetLastInvoiceSyncUtcAsync();
                if (lastSync is null)
                {
                    _logger.LogWarning(
                        "Skipping Neon INVOICE DELTA sync because the Neon watermark could not be read safely");
                    return;
                }

                var invoices = await _cacheDb.CacheInvoices
                    .AsNoTracking()
                    .Where(x => x.CachedAt > lastSync.Value)
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
                    _logger.LogInformation("No invoice changes for Neon");
                    return;
                }

                var keys = invoices
                    .Select(i => i.SapDocEntry)
                    .ToList();

                var existingMap = await _neonDb.Invoices
                    .Where(i => keys.Contains(i.SapDocEntry))
                    .ToDictionaryAsync(i => i.SapDocEntry);

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

                const int batchSize = 500;
                var batches = keys.Chunk(batchSize).ToList();

                _logger.LogInformation(
                    "Processing invoice lines in {Batches} batches of max {BatchSize}",
                    batches.Count,
                    batchSize);

                await using var tx = await _neonDb.Database.BeginTransactionAsync();

                try
                {
                    var totalLinesProcessed = 0;

                    foreach (var batch in batches)
                    {
                        var batchKeys = batch.ToList();

                        var cacheLines = await _cacheDb.CacheInvoiceLines
                            .AsNoTracking()
                            .Where(l => batchKeys.Contains(l.SapDocEntry))
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
                            .Where(l => batchKeys.Contains(l.InvoiceEntry))
                            .ToListAsync();

                        _neonDb.InvoiceLines.RemoveRange(existingLines);
                        _neonDb.InvoiceLines.AddRange(cacheLines);

                        totalLinesProcessed += cacheLines.Count;

                        _logger.LogDebug(
                            "Invoice batch processed | Invoices={Count} Lines={Lines}",
                            batchKeys.Count,
                            cacheLines.Count);
                    }

                    await _neonDb.SaveChangesAsync();
                    await tx.CommitAsync();

                    hasSyncedHeaders = true;

                    _logger.LogInformation(
                        "Neon INVOICE DELTA sync completed | Headers={Headers} Lines={Lines} Batches={Batches}",
                        invoices.Count,
                        totalLinesProcessed,
                        batches.Count);
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    _logger.LogError(ex,
                        "Neon INVOICE DELTA sync failed - transaction rolled back | Headers={Count}",
                        invoices.Count);
                    throw;
                }
            });

            if (hasSyncedHeaders)
            {
                await SyncOrphanedLinesAsync();
            }
        }
        catch (Exception ex) when (IsTransientNeonStreamReadFailure(ex))
        {
            await ResetNeonConnectionAsync(
                ex,
                "Neon INVOICE DELTA sync",
                "Clearing the Neon pool and skipping this run. The next scheduled execution will retry.");
        }
    }

    // Full sync from cache into Neon.
    public async Task SyncFullAsync()
    {
        _logger.LogInformation("Neon INVOICE FULL sync started");

        try
        {
            var strategy = _neonDb.Database.CreateExecutionStrategy();
            var now = DateTime.UtcNow;

            await strategy.ExecuteAsync(async () =>
            {
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
                    _logger.LogInformation("No invoices in cache for full sync");
                    return;
                }

                var keys = invoices
                    .Select(i => i.SapDocEntry)
                    .ToList();

                var existingMap = await _neonDb.Invoices
                    .Where(i => keys.Contains(i.SapDocEntry))
                    .ToDictionaryAsync(i => i.SapDocEntry);

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

                const int batchSize = 500;
                var batches = keys.Chunk(batchSize).ToList();

                _logger.LogInformation(
                    "Processing invoice lines in {Batches} batches of max {BatchSize}",
                    batches.Count,
                    batchSize);

                await using var tx = await _neonDb.Database.BeginTransactionAsync();

                try
                {
                    var totalLinesProcessed = 0;

                    foreach (var batch in batches)
                    {
                        var batchKeys = batch.ToList();

                        var cacheLines = await _cacheDb.CacheInvoiceLines
                            .AsNoTracking()
                            .Where(l => batchKeys.Contains(l.SapDocEntry))
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
                            .Where(l => batchKeys.Contains(l.InvoiceEntry))
                            .ToListAsync();

                        _neonDb.InvoiceLines.RemoveRange(existingLines);
                        _neonDb.InvoiceLines.AddRange(cacheLines);

                        totalLinesProcessed += cacheLines.Count;

                        _logger.LogDebug(
                            "Invoice batch processed | Invoices={Count} Lines={Lines}",
                            batchKeys.Count,
                            cacheLines.Count);
                    }

                    await _neonDb.SaveChangesAsync();
                    await tx.CommitAsync();

                    _logger.LogInformation(
                        "Neon INVOICE FULL sync completed | Headers={Headers} Lines={Lines} Batches={Batches}",
                        invoices.Count,
                        totalLinesProcessed,
                        batches.Count);
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    _logger.LogError(ex,
                        "Neon INVOICE FULL sync failed - transaction rolled back | Headers={Count}",
                        invoices.Count);
                    throw;
                }
            });
        }
        catch (Exception ex) when (IsTransientNeonStreamReadFailure(ex))
        {
            await ResetNeonConnectionAsync(
                ex,
                "Neon INVOICE FULL sync",
                "Clearing the Neon pool and skipping this run. A later full sync can retry cleanly.");
        }
    }

    // Backfill lines for invoice headers that were synced before line support existed.
    public async Task SyncOrphanedLinesAsync()
    {
        try
        {
            var neonLineKeys = await _neonDb.InvoiceLines
                .AsNoTracking()
                .Select(l => l.InvoiceEntry)
                .Distinct()
                .ToListAsync();

            var neonLineKeySet = neonLineKeys.ToHashSet();

            var cacheLineKeys = await _cacheDb.CacheInvoiceLines
                .AsNoTracking()
                .Select(l => l.SapDocEntry)
                .Distinct()
                .ToListAsync();

            var orphanedKeys = cacheLineKeys
                .Where(k => !neonLineKeySet.Contains(k))
                .ToList();

            if (orphanedKeys.Count == 0)
            {
                _logger.LogDebug("No orphaned invoice lines found");
                return;
            }

            var safeOrphanKeys = await _neonDb.Invoices
                .AsNoTracking()
                .Where(i => orphanedKeys.Contains(i.SapDocEntry))
                .Select(i => i.SapDocEntry)
                .ToListAsync();

            if (safeOrphanKeys.Count == 0)
            {
                _logger.LogInformation(
                    "Orphaned lines found but parent headers are missing in Neon | Count={Count}",
                    orphanedKeys.Count);
                return;
            }

            _logger.LogInformation(
                "Backfilling orphaned invoice lines | Invoices={Count}",
                safeOrphanKeys.Count);

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
                    "Orphaned invoice lines backfilled | Invoices={Invoices} Lines={Lines}",
                    safeOrphanKeys.Count,
                    orphanLines.Count);
            });
        }
        catch (Exception ex) when (IsTransientNeonStreamReadFailure(ex))
        {
            await ResetNeonConnectionAsync(
                ex,
                "Neon INVOICE orphan line backfill",
                "Clearing the Neon pool and skipping orphan backfill for this run.");
        }
    }

    private async Task<DateTime?> TryGetLastInvoiceSyncUtcAsync()
    {
        const int maxAttempts = 2;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var lastSync = await _neonDb.Invoices
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
                    "Neon INVOICE watermark read",
                    "Clearing the Neon pool and retrying the watermark query on a fresh connection.");
            }
            catch (Exception ex) when (IsTransientNeonStreamReadFailure(ex))
            {
                await ResetNeonConnectionAsync(
                    ex,
                    "Neon INVOICE watermark read",
                    "Clearing the Neon pool and skipping this invoice sync run.");
                return null;
            }
        }

        return null;
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
