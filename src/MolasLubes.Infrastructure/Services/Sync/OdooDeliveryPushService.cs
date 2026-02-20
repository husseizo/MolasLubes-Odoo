using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Sync;

public class OdooDeliveryPushService
{
    private readonly NeonDbContext _neonDb;
    private readonly OdooApiClient _odoo;
    private readonly OdooPushResultHandler _resultHandler;
    private readonly ILogger<OdooDeliveryPushService> _logger;

    public OdooDeliveryPushService(
        NeonDbContext neonDb,
        OdooApiClient odoo,
        OdooPushResultHandler resultHandler,
        ILogger<OdooDeliveryPushService> logger)
    {
        _neonDb = neonDb;
        _odoo = odoo;
        _resultHandler = resultHandler;
        _logger = logger;
    }

    // =====================================================
    // 🚚 PUSH PENDING DELIVERIES TO ODOO (NEON → ODOO)
    // Reads NeonDeliveries where OdooStatus is NULL or PENDING,
    // POSTs each to Odoo's /api/deliveries endpoint,
    // then writes the result back to both NeonDeliveries and SAP ODLN UDFs.
    // =====================================================
    public async Task PushPendingDeliveriesAsync()
    {
        var pending = await _neonDb.Deliveries
            .Include(d => d.Lines)
            .Where(d =>
                !d.IsCancelled &&
                (d.OdooStatus == null || d.OdooStatus == "PENDING"))
            .ToListAsync();

        if (pending.Count == 0)
        {
            _logger.LogInformation("ℹ No pending deliveries to push to Odoo");
            return;
        }

        _logger.LogInformation("🚚 Pushing {Count} deliveries to Odoo", pending.Count);

        var synced = 0;
        var failed = 0;

        foreach (var delivery in pending)
        {
            var payload = new
            {
                sap_doc_entry            = delivery.SapDocEntry,
                sap_doc_num              = delivery.SapDocNum,
                card_code                = delivery.CardCode,
                delivery_date            = delivery.DeliveryDate,
                base_order_entry         = delivery.BaseOrderEntry,
                is_cancelled             = delivery.IsCancelled,
                lines = delivery.Lines.Select(l => new
                {
                    line_num                   = l.LineNum,
                    item_code                  = l.ItemCode,
                    description                = l.Description,
                    quantity                   = l.Quantity,
                    line_total                 = l.LineTotal,
                    base_entry                 = l.BaseEntry,
                    base_line                  = l.BaseLine,
                    odoo_sales_order_line_id   = l.OdooSalesOrderLineId,
                }).ToList()
            };

            var result = await _odoo.PushDeliveryAsync(payload);
            var now    = DateTime.UtcNow;

            if (result.Success)
            {
                delivery.OdooStatus   = "SYNCED";
                delivery.OdooLastSync = now;
                delivery.OdooSyncDir  = "FROM_SAP";
                delivery.OdooErrorMsg = null;
                synced++;
            }
            else
            {
                delivery.OdooStatus   = "ERROR";
                delivery.OdooLastSync = now;
                delivery.OdooSyncDir  = "FROM_SAP";
                delivery.OdooErrorMsg = result.ErrorMessage;
                failed++;

                _logger.LogWarning(
                    "⚠️ Odoo push failed for ODLN {DocEntry}: {Error}",
                    delivery.SapDocEntry,
                    result.ErrorMessage);
            }

            // Write result back to SAP ODLN UDFs (U_Odoo_Status / U_Odoo_ErrorMsg).
            // Failures here are non-fatal — the Neon status is the source of truth
            // for the next push cycle; SAP UDFs will reconcile on the next delivery delta sync.
            try
            {
                _resultHandler.UpdateDeliveryStatus(delivery.SapDocEntry, result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "⚠️ Could not write Odoo sync result to SAP ODLN {DocEntry} — will reconcile on next delta sync",
                    delivery.SapDocEntry);
            }
        }

        await _neonDb.SaveChangesAsync();

        _logger.LogInformation(
            "✅ Odoo delivery push completed | Synced={Synced} Failed={Failed}",
            synced, failed);
    }
}
