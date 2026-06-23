using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Neon;
using MolasLubes.Infrastructure.Common;
using MolasLubes.Infrastructure.Persistence;
using Npgsql;
using System.Net.Sockets;

namespace MolasLubes.Infrastructure.Services.Sync;

public class NeonSalesOrderSyncService
{
    private readonly MolasCacheDbContext _cacheDb;
    private readonly NeonDbContext _neonDb;
    private readonly ILogger<NeonSalesOrderSyncService> _logger;

    public NeonSalesOrderSyncService(
        MolasCacheDbContext cacheDb,
        NeonDbContext neonDb,
        ILogger<NeonSalesOrderSyncService> logger)
    {
        _cacheDb = cacheDb;
        _neonDb = neonDb;
        _logger = logger;
    }

    // =====================================================
    // 🧾 SALES ORDER DELTA SYNC (CACHE → NEON)
    // =====================================================
    public async Task SyncDeltaAsync()
    {
        _logger.LogInformation("🧾 Neon SALES ORDER DELTA sync started");

        try
        {
            var strategy = _neonDb.Database.CreateExecutionStrategy();
            var now = DateTime.UtcNow;

            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _neonDb.Database.BeginTransactionAsync();

                // -------------------------------------------------
                // 1️⃣ SAFE LAST SYNC (UTC GUARANTEED)
                // -------------------------------------------------
                var lastSync = await _neonDb.SalesOrders
                    .OrderByDescending(x => x.SyncedAt)
                    .Select(x => x.SyncedAt)
                    .FirstOrDefaultAsync();

                if (lastSync == default)
                    lastSync = DateTime.MinValue;

                lastSync = lastSync.AsUtc();

                // -------------------------------------------------
                // 2️⃣ READ SALES ORDERS FROM CACHE
                // -------------------------------------------------
                var orders = await _cacheDb.CacheSalesOrders
                    .AsNoTracking()
                    .Where(x =>
                        x.LastUpdatedAt != null &&
                        x.LastUpdatedAt > lastSync)
                    .Select(x => new NeonSalesOrder
                    {
                        SapDocEntry = x.SapDocEntry,
                        DocNum = x.SapDocNum,

                        CustomerCode = x.CustomerCode,
                        CustomerName = x.CustomerName ?? string.Empty,

                        Status =
                            x.DocStatus == "O" ? "OPEN" :
                            x.DocStatus == "C" ? "CLOSED" :
                            x.DocStatus == "X" ? "CANCELLED" :
                            "UNKNOWN",

                        DocTotal = x.DocTotal,
                        DocDate = (x.DocDate ?? x.CreatedAt).AsUtc(),

                        // 🔗 ODOO UDFS (UTC SAFE)
                        OdooSalesOrderId = x.OdooSalesOrderId,
                        OdooStatus = x.OdooStatus,
                        OdooSyncDir = x.OdooSyncDir,
                        OdooErrorMsg = x.OdooErrorMsg,
                        OdooLastSync = x.OdooLastSync.AsUtc(),

                        SyncedAt = now
                    })
                    .ToListAsync();

                if (orders.Count == 0)
                {
                    _logger.LogInformation("ℹ No sales order changes for Neon");
                    return;
                }

                // -------------------------------------------------
                // 3️⃣ UPSERT SALES ORDERS
                // -------------------------------------------------
                var keys = orders
                    .Select(o => o.SapDocEntry)
                    .ToList();

                var existingMap = await _neonDb.SalesOrders
                    .Where(o => keys.Contains(o.SapDocEntry))
                    .ToDictionaryAsync(o => o.SapDocEntry);

                foreach (var incoming in orders)
                {
                    if (!existingMap.TryGetValue(incoming.SapDocEntry, out var entity))
                    {
                        _neonDb.SalesOrders.Add(incoming);
                    }
                    else
                    {
                        entity.DocNum = incoming.DocNum;
                        entity.CustomerCode = incoming.CustomerCode;
                        entity.CustomerName = incoming.CustomerName;
                        entity.Status = incoming.Status;
                        entity.DocTotal = incoming.DocTotal;
                        entity.DocDate = incoming.DocDate;

                        // 🔗 ODOO UDFS
                        entity.OdooSalesOrderId = incoming.OdooSalesOrderId;
                        entity.OdooStatus = incoming.OdooStatus;
                        entity.OdooSyncDir = incoming.OdooSyncDir;
                        entity.OdooErrorMsg = incoming.OdooErrorMsg;
                        entity.OdooLastSync = incoming.OdooLastSync.AsUtc();

                        entity.SyncedAt = now;
                    }
                }

                try
                {
                    await _neonDb.SaveChangesAsync();
                    await tx.CommitAsync();

                    _logger.LogInformation(
                        "✅ Neon SALES ORDER DELTA sync completed | Count={Count}",
                        orders.Count);
                }
                catch (Exception ex)
                {
                    await TryRollbackAsync(tx, "Neon SALES ORDER DELTA sync");

                    if (IsTransientNeonFailure(ex))
                    {
                        await ResetNeonConnectionAsync(
                            ex,
                            "Neon SALES ORDER DELTA sync (transaction save)",
                            "Clearing the Neon pool and skipping this run. The next scheduled execution will retry.");
                        return;
                    }

                    _logger.LogError(ex,
                        "❌ Neon SALES ORDER DELTA sync FAILED - transaction rolled back | Count={Count}",
                        orders.Count);
                    throw;
                }
            });
        }
        catch (Exception ex) when (IsTransientNeonFailure(ex))
        {
            await ResetNeonConnectionAsync(
                ex,
                "Neon SALES ORDER DELTA sync",
                "Clearing the Neon pool and skipping this run. The next scheduled execution will retry.");
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
            else
            {
                NpgsqlConnection.ClearAllPools();
            }
        }
        catch (Exception resetEx)
        {
            _logger.LogWarning(resetEx,
                "Failed to reset the Neon PostgreSQL connection after {Operation}",
                operationName);
        }
    }

    private static bool IsTransientNeonFailure(Exception ex)
    {
        if (ex is EndOfStreamException or IOException or TimeoutException or SocketException)
        {
            return true;
        }

        if (ex is NpgsqlException npgsqlException)
        {
            if (npgsqlException.Message.Contains(
                    "Exception while reading from stream",
                    StringComparison.OrdinalIgnoreCase) ||
                npgsqlException.Message.Contains(
                    "Failed to connect to",
                    StringComparison.OrdinalIgnoreCase) ||
                npgsqlException.Message.Contains(
                    "Timeout during connection attempt",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return ex.InnerException is not null &&
               IsTransientNeonFailure(ex.InnerException);
    }

    private async Task TryRollbackAsync(
        IDbContextTransaction tx,
        string operationName)
    {
        try
        {
            await tx.RollbackAsync();
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex,
                "{Operation}: transaction already disposed before rollback (safe to ignore).",
                operationName);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex,
                "{Operation}: rollback skipped due to invalid transaction state (safe to ignore).",
                operationName);
        }
        catch (Exception ex) when (IsTransientNeonFailure(ex))
        {
            _logger.LogDebug(ex,
                "{Operation}: transient Neon failure occurred during rollback (safe to ignore).",
                operationName);
        }
    }
}
