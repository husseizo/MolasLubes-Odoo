using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Caching;

public class SalesOrderStatusService
{
    private readonly MolasCacheDbContext _db;
    private readonly ILogger<SalesOrderStatusService> _logger;

    public SalesOrderStatusService(
        MolasCacheDbContext db,
        ILogger<SalesOrderStatusService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Marks cached sales order as Delivered (SAP DocStatus = C)
    /// </summary>
    public async Task MarkOrderDeliveredAsync(int sapDocEntry, string? odooSalesOrderIdFallback = null)
    {
        var order = await _db.CacheSalesOrders
            .FirstOrDefaultAsync(o => o.SapDocEntry == sapDocEntry);

        // 🔄 Fallback: Try lookup by Odoo Sales Order ID if primary lookup failed
        if (order == null && !string.IsNullOrEmpty(odooSalesOrderIdFallback))
        {
            order = await _db.CacheSalesOrders
                .FirstOrDefaultAsync(o => o.OdooSalesOrderId == odooSalesOrderIdFallback);

            if (order != null)
            {
                _logger.LogInformation(
                    "ℹ Order found via Odoo ID fallback | SapDocEntry={SapDocEntry} OdooSalesOrderId={OdooId}",
                    sapDocEntry, odooSalesOrderIdFallback);
            }
        }

        if (order == null)
        {
            _logger.LogWarning(
                "⚠ Order not found in cache | SapDocEntry={SapDocEntry} OdooSalesOrderIdFallback={OdooId}",
                sapDocEntry, odooSalesOrderIdFallback ?? "N/A");
            return;
        }

        // 🔁 Already delivered → ignore
        if (order.DocStatus == "D")
        {
            _logger.LogDebug(
                "ℹ Order already delivered | SapDocEntry={SapDocEntry}",
                sapDocEntry);
            return;
        }

        // ❌ Cancelled → ignore
        if (order.DocStatus == "C")
        {
            _logger.LogDebug(
                "ℹ Order cancelled → skipping delivery mark | SapDocEntry={SapDocEntry}",
                sapDocEntry);
            return;
        }

        // ❌ Not open → ignore (no exception)
        if (order.DocStatus != "O")
        {
            _logger.LogWarning(
                "⚠ Invalid state transition to Delivered | SapDocEntry={SapDocEntry} | CurrentStatus={Status}",
                sapDocEntry,
                order.DocStatus);
            return;
        }

        // ✅ Valid transition O → D
        order.DocStatus = "D";
        order.LastUpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "✅ Order marked Delivered | SapDocEntry={SapDocEntry}",
            sapDocEntry);
    }
}