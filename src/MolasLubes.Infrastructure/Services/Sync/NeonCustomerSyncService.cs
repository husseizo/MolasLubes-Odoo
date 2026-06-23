using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Neon;
using MolasLubes.Infrastructure.Common;
using MolasLubes.Infrastructure.Persistence;
using Npgsql;
using System.Net.Sockets;

namespace MolasLubes.Infrastructure.Services.Sync;

public class NeonCustomerSyncService
{
    private readonly MolasCacheDbContext _cacheDb;
    private readonly NeonDbContext _neonDb;
    private readonly ILogger<NeonCustomerSyncService> _logger;

    public NeonCustomerSyncService(
        MolasCacheDbContext cacheDb,
        NeonDbContext neonDb,
        ILogger<NeonCustomerSyncService> logger)
    {
        _cacheDb = cacheDb;
        _neonDb = neonDb;
        _logger = logger;
    }

    // =====================================================
    // 👥 CUSTOMER DELTA SYNC (CACHE → NEON)
    // =====================================================
    public async Task SyncDeltaAsync()
    {
        _logger.LogInformation("👥 Neon CUSTOMER DELTA sync started");

        try
        {
            var strategy = _neonDb.Database.CreateExecutionStrategy();
            var now = DateTime.UtcNow;

            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _neonDb.Database.BeginTransactionAsync();

                // -------------------------------------------------
                // 1️⃣ SAFE LAST SYNC (UTC)
                // -------------------------------------------------
                var lastSync = await _neonDb.Customers
                    .OrderByDescending(x => x.SyncedAt)
                    .Select(x => x.SyncedAt)
                    .FirstOrDefaultAsync();

                if (lastSync == default)
                    lastSync = DateTime.MinValue;

                lastSync = lastSync.AsUtc();

                // -------------------------------------------------
                // 2️⃣ READ DELTA FROM CACHE
                // -------------------------------------------------
                var customers = await _cacheDb.CacheCustomers
                    .AsNoTracking()
                    .Where(x =>
                        x.IsActive &&
                        x.LastSapDeltaAt > lastSync)
                    .Select(x => new NeonCustomer
                    {
                        CardCode = x.CardCode,
                        CardName = x.CardName,

                        CreditLimit = x.CreditLimit ?? 0m,
                        OutstandingBalance = x.OutstandingBalance ?? 0m,
                        AvailableCredit = x.AvailableCredit ?? 0m,

                        IsActive = x.IsActive,
                        PriceList = x.PriceList,
                        SalesPersonCode = x.SlpCode,

                        Phone1 = x.Phone1,
                        Phone2 = x.Phone2,
                        Email = x.Email,

                        BillToStreet = x.BillToStreet,
                        BillToCity = x.BillToCity,
                        BillToCountry = x.BillToCountry,

                        ShipToStreet = x.ShipToStreet,
                        ShipToCity = x.ShipToCity,
                        ShipToCountry = x.ShipToCountry,

                        // 🔗 ODOO UDFS (UTC SAFE)
                        OdooPartnerId = x.OdooPartnerId,
                        OdooStatus = x.OdooStatus,
                        OdooSyncDir = x.OdooSyncDir,
                        OdooErrorMsg = x.OdooErrorMsg,
                        OdooLastSync = x.OdooLastSync.AsUtc(),

                        SyncedAt = now
                    })
                    .ToListAsync();

                if (customers.Count == 0)
                {
                    _logger.LogInformation("ℹ No customer changes for Neon");
                    return;
                }

                // -------------------------------------------------
                // 3️⃣ UPSERT INTO NEON
                // -------------------------------------------------
                var cardCodes = customers
                    .Select(c => c.CardCode)
                    .ToList();

                var existing = await _neonDb.Customers
                    .Where(c => cardCodes.Contains(c.CardCode))
                    .ToDictionaryAsync(c => c.CardCode);

                foreach (var incoming in customers)
                {
                    if (!existing.TryGetValue(incoming.CardCode, out var entity))
                    {
                        _neonDb.Customers.Add(incoming);
                    }
                    else
                    {
                        entity.CardName = incoming.CardName;
                        entity.CreditLimit = incoming.CreditLimit;
                        entity.OutstandingBalance = incoming.OutstandingBalance;
                        entity.AvailableCredit = incoming.AvailableCredit;
                        entity.IsActive = incoming.IsActive;

                        entity.PriceList = incoming.PriceList;
                        entity.SalesPersonCode = incoming.SalesPersonCode;

                        entity.Phone1 = incoming.Phone1;
                        entity.Phone2 = incoming.Phone2;
                        entity.Email = incoming.Email;

                        entity.BillToStreet = incoming.BillToStreet;
                        entity.BillToCity = incoming.BillToCity;
                        entity.BillToCountry = incoming.BillToCountry;

                        entity.ShipToStreet = incoming.ShipToStreet;
                        entity.ShipToCity = incoming.ShipToCity;
                        entity.ShipToCountry = incoming.ShipToCountry;

                        entity.OdooPartnerId = incoming.OdooPartnerId;
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
                        "✅ Neon CUSTOMER DELTA sync completed | Count={Count}",
                        customers.Count);
                }
                catch (Exception ex)
                {
                    await TryRollbackAsync(tx, "Neon CUSTOMER DELTA sync");

                    if (IsTransientNeonFailure(ex))
                    {
                        await ResetNeonConnectionAsync(
                            ex,
                            "Neon CUSTOMER DELTA sync (transaction save)",
                            "Clearing the Neon pool and skipping this run. The next scheduled execution will retry.");
                        return;
                    }

                    _logger.LogError(ex,
                        "❌ Neon CUSTOMER DELTA sync FAILED - transaction rolled back | Count={Count}",
                        customers.Count);
                    throw;
                }
            });
        }
        catch (Exception ex) when (IsTransientNeonFailure(ex))
        {
            await ResetNeonConnectionAsync(
                ex,
                "Neon CUSTOMER DELTA sync",
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
