using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Neon;
using MolasLubes.Infrastructure.Common;
using MolasLubes.Infrastructure.Persistence;
using Npgsql;
using System.Net.Sockets;

namespace MolasLubes.Infrastructure.Services.Sync;

public class NeonSalesOrderLineSyncService
{
    private readonly MolasCacheDbContext _cacheDb;
    private readonly NeonDbContext _neonDb;
    private readonly ILogger<NeonSalesOrderLineSyncService> _logger;

    public NeonSalesOrderLineSyncService(
        MolasCacheDbContext cacheDb,
        NeonDbContext neonDb,
        ILogger<NeonSalesOrderLineSyncService> logger)
    {
        _cacheDb = cacheDb;
        _neonDb = neonDb;
        _logger = logger;
    }

    // =====================================================
    // 🧾 SALES ORDER LINE DELTA SYNC (CACHE → NEON)
    // =====================================================
    public async Task SyncDeltaAsync()
    {
        _logger.LogInformation("📦 Neon SALES ORDER LINE DELTA sync started");

        try
        {
            var strategy = _neonDb.Database.CreateExecutionStrategy();
            var now = DateTime.UtcNow;

            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _neonDb.Database.BeginTransactionAsync();

                // -------------------------------------------------
                // 1️⃣ LAST SALES ORDER SYNC (UTC)
                // -------------------------------------------------
                var lastSync = await _neonDb.SalesOrders
                    .OrderByDescending(x => x.SyncedAt)
                    .Select(x => x.SyncedAt)
                    .FirstOrDefaultAsync();

                if (lastSync == default)
                    lastSync = DateTime.MinValue;

                lastSync = lastSync.AsUtc();

                // -------------------------------------------------
                // 2️⃣ AFFECTED SALES ORDERS
                // Only include entries that already have a parent row in NeonSalesOrders;
                // otherwise the FK constraint (NeonSalesOrderLines → NeonSalesOrders) fails.
                // -------------------------------------------------
                var candidateEntries = await _cacheDb.CacheSalesOrders
                    .AsNoTracking()
                    .Where(o =>
                        o.LastUpdatedAt != null &&
                        o.LastUpdatedAt > lastSync)
                    .Select(o => o.SapDocEntry)
                    .Distinct()
                    .ToListAsync();

                var existingNeonEntries = await _neonDb.SalesOrders
                    .Where(o => candidateEntries.Contains(o.SapDocEntry))
                    .Select(o => o.SapDocEntry)
                    .ToListAsync();

                var orderEntries = existingNeonEntries;

                if (orderEntries.Count == 0)
                {
                    _logger.LogInformation("ℹ No sales order line changes for Neon");
                    return;
                }

                // -------------------------------------------------
                // 3️⃣ READ LINES FROM CACHE
                // -------------------------------------------------
                var lines = await _cacheDb.CacheSalesOrderLines
                    .AsNoTracking()
                    .Where(l => orderEntries.Contains(l.SapDocEntry))
                    .Select(l => new NeonSalesOrderLine
                    {
                        SalesOrderEntry = l.SapDocEntry,

                        ItemCode  = l.ItemCode,
                        ItemName  = l.ItemName ?? string.Empty,

                        Quantity  = l.Quantity,
                        Price     = l.Price,
                        LineTotal = l.LineTotal,

                        OdooSalesOrderLineId = l.OdooSalesOrderLineId
                    })
                    .ToListAsync();

                if (lines.Count == 0)
                {
                    _logger.LogInformation("ℹ No sales order lines found");
                    return;
                }

                // -------------------------------------------------
                // 4️⃣ DELETE EXISTING LINES (PER ORDER)
                // -------------------------------------------------
                var existingLines = await _neonDb.SalesOrderLines
                    .Where(l => orderEntries.Contains(l.SalesOrderEntry))
                    .ToListAsync();

                try
                {
                    _neonDb.SalesOrderLines.RemoveRange(existingLines);

                    // -------------------------------------------------
                    // 5️⃣ INSERT NEW LINES
                    // -------------------------------------------------
                    _neonDb.SalesOrderLines.AddRange(lines);

                    await _neonDb.SaveChangesAsync();
                    await tx.CommitAsync();

                    _logger.LogInformation(
                        "✅ Neon SALES ORDER LINE DELTA sync completed | Orders={Orders} Lines={Lines}",
                        orderEntries.Count,
                        lines.Count);
                }
                catch (Exception ex)
                {
                    await TryRollbackAsync(tx, "Neon SALES ORDER LINE DELTA sync");

                    if (IsTransientNeonFailure(ex))
                    {
                        await ResetNeonConnectionAsync(
                            ex,
                            "Neon SALES ORDER LINE DELTA sync (transaction save)",
                            "Clearing the Neon pool and skipping this run. The next scheduled execution will retry.");
                        return;
                    }

                    _logger.LogError(ex,
                        "❌ Neon SALES ORDER LINE DELTA sync FAILED - transaction rolled back | Orders={Orders} Lines={Lines}",
                        orderEntries.Count,
                        lines.Count);
                    throw;
                }
            });
        }
        catch (Exception ex) when (IsTransientNeonFailure(ex))
        {
            await ResetNeonConnectionAsync(
                ex,
                "Neon SALES ORDER LINE DELTA sync",
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
