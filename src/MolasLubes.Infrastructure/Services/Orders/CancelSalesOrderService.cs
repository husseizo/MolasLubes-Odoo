using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Persistence;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

namespace MolasLubes.Infrastructure.Services.Orders;

public class CancelSalesOrderService
{
    private readonly MolasCacheDbContext _db;
    private readonly SapSalesOrderCanceler _sap;
    private readonly ILogger<CancelSalesOrderService> _logger;

    public CancelSalesOrderService(
        MolasCacheDbContext db,
        SapSalesOrderCanceler sap,
        ILogger<CancelSalesOrderService> logger)
    {
        _db = db;
        _sap = sap;
        _logger = logger;
    }

    public async Task CancelAsync(int sapDocEntry, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Cancel reason is required", nameof(reason));

        await using var tx = await _db.Database.BeginTransactionAsync();

        // 1️⃣ LOAD ORDER
        var order = await _db.CacheSalesOrders
            .SingleOrDefaultAsync(o => o.SapDocEntry == sapDocEntry);

        if (order == null)
            throw new InvalidOperationException($"Order not found | SapDocEntry={sapDocEntry}");

        // 🔒 SAFETY CHECKS
        if (order.DocStatus == "X")
            throw new InvalidOperationException("Order already cancelled");

        if (order.DocStatus != "O")
            throw new InvalidOperationException(
                $"Only OPEN orders can be cancelled (Current={order.DocStatus})");

        // 2️⃣ LOAD COMMITTED RESERVATIONS
        var reservations = await _db.CacheStockReservations
            .Where(r =>
                r.SapDocEntry == sapDocEntry &&
                r.IsCommitted &&
                r.ReleasedAt == null)
            .ToListAsync();

        // 3️⃣ RETURN STOCK (CACHE BRAIN)
        foreach (var r in reservations)
        {
            var product = await _db.CacheProducts
                .SingleAsync(p => p.ItemCode == r.ItemCode);

            product.AvailableCache += r.Quantity;

            r.ReleasedAt = DateTime.UtcNow;
            r.IsCommitted = false;
            r.SapDocEntry = null;
        }

        // 4️⃣ CANCEL IN SAP (SOURCE OF TRUTH)
        _sap.Cancel(sapDocEntry, reason);

        // 5️⃣ UPDATE CACHE ORDER
        order.DocStatus = "X"; // Cancelled
        order.CancelledAt = DateTime.UtcNow;
        order.CancelReason = reason;
        order.LastUpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        _logger.LogInformation(
            "🛑 SalesOrder cancelled | SapDocEntry={SapDocEntry} | Reason={Reason}",
            sapDocEntry,
            reason);
    }
}