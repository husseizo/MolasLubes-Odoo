using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Sync;

public class OdooPaymentPushService
{
    private readonly NeonDbContext _neonDb;
    private readonly OdooApiClient _odoo;
    private readonly OdooPushResultHandler _resultHandler;
    private readonly ILogger<OdooPaymentPushService> _logger;

    public OdooPaymentPushService(
        NeonDbContext neonDb,
        OdooApiClient odoo,
        OdooPushResultHandler resultHandler,
        ILogger<OdooPaymentPushService> logger)
    {
        _neonDb = neonDb;
        _odoo = odoo;
        _resultHandler = resultHandler;
        _logger = logger;
    }

    // =====================================================
    // 💳 PUSH PENDING PAYMENTS TO ODOO (NEON → ODOO)
    // Reads NeonPayments where OdooStatus is NULL or PENDING,
    // POSTs each to Odoo's /api/payments endpoint,
    // then writes the result back to both NeonPayments and SAP ORCT UDFs.
    // =====================================================
    public async Task PushPendingPaymentsAsync()
    {
        var pending = await _neonDb.Payments
            .Where(p =>
                p.OdooStatus == null || p.OdooStatus == "PENDING")
            .ToListAsync();

        if (pending.Count == 0)
        {
            _logger.LogInformation("ℹ No pending payments to push to Odoo");
            return;
        }

        _logger.LogInformation("💳 Pushing {Count} payments to Odoo", pending.Count);

        var synced = 0;
        var failed = 0;

        foreach (var payment in pending)
        {
            var payload = new
            {
                sap_doc_entry  = payment.SapDocEntry,
                doc_num        = payment.DocNum,
                customer_code  = payment.CustomerCode,
                invoice_entry  = payment.InvoiceEntry,
                amount         = payment.Amount,
                payment_date   = payment.PaymentDate,
            };

            var result = await _odoo.PushPaymentAsync(payload);
            var now    = DateTime.UtcNow;

            if (result.Success)
            {
                payment.OdooStatus   = "SYNCED";
                payment.OdooLastSync = now;
                payment.OdooSyncDir  = "FROM_SAP";
                payment.OdooErrorMsg = null;
                synced++;
            }
            else
            {
                payment.OdooStatus   = "ERROR";
                payment.OdooLastSync = now;
                payment.OdooSyncDir  = "FROM_SAP";
                payment.OdooErrorMsg = result.ErrorMessage;
                failed++;

                _logger.LogWarning(
                    "⚠️ Odoo push failed for ORCT {DocEntry}: {Error}",
                    payment.SapDocEntry,
                    result.ErrorMessage);
            }

            // Write result back to SAP ORCT UDFs (U_Odoo_Status / U_Odoo_ErrorMsg).
            // Failures here are non-fatal — the Neon status is the source of truth
            // for the next push cycle; SAP UDFs will reconcile on the next payment delta sync.
            try
            {
                _resultHandler.UpdatePaymentStatus(payment.SapDocEntry, result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "⚠️ Could not write Odoo sync result to SAP ORCT {DocEntry} — will reconcile on next delta sync",
                    payment.SapDocEntry);
            }
        }

        await _neonDb.SaveChangesAsync();

        _logger.LogInformation(
            "✅ Odoo payment push completed | Synced={Synced} Failed={Failed}",
            synced, failed);
    }
}
