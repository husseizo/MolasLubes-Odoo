using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Sync;

public class OdooInvoicePushService
{
    private readonly NeonDbContext _neonDb;
    private readonly OdooApiClient _odoo;
    private readonly OdooPushResultHandler _resultHandler;
    private readonly ILogger<OdooInvoicePushService> _logger;

    public OdooInvoicePushService(
        NeonDbContext neonDb,
        OdooApiClient odoo,
        OdooPushResultHandler resultHandler,
        ILogger<OdooInvoicePushService> logger)
    {
        _neonDb = neonDb;
        _odoo = odoo;
        _resultHandler = resultHandler;
        _logger = logger;
    }

    // =====================================================
    // 🧾 PUSH PENDING INVOICES TO ODOO (NEON → ODOO)
    // Reads NeonInvoices where OdooStatus is NULL or PENDING,
    // POSTs each to Odoo's /api/invoices endpoint,
    // then writes the result back to both NeonInvoices and SAP OINV UDFs.
    // =====================================================
    public async Task PushPendingInvoicesAsync()
    {
        var pending = await _neonDb.Invoices
            .Include(i => i.Lines)
            .Where(i =>
                i.OdooStatus == null || i.OdooStatus == "PENDING")
            .ToListAsync();

        if (pending.Count == 0)
        {
            _logger.LogInformation("ℹ No pending invoices to push to Odoo");
            return;
        }

        _logger.LogInformation("🧾 Pushing {Count} invoices to Odoo", pending.Count);

        var synced = 0;
        var failed = 0;

        foreach (var invoice in pending)
        {
            var payload = new
            {
                sap_doc_entry  = invoice.SapDocEntry,
                doc_num        = invoice.DocNum,
                customer_code  = invoice.CustomerCode,
                card_name      = invoice.CardName,
                invoice_date   = invoice.InvoiceDate,
                doc_total      = invoice.DocTotal,
                vat_sum        = invoice.VatSum,
                is_paid        = invoice.IsPaid,
                paid_amount    = invoice.PaidAmount,
                lines = invoice.Lines.Select(l => new
                {
                    item_code              = l.ItemCode,
                    description            = l.Description,
                    quantity               = l.Quantity,
                    line_total             = l.LineTotal,
                    gross_buy_pr           = l.GrossBuyPr,
                    base_entry             = l.BaseEntry,
                    base_line              = l.BaseLine,
                    odoo_invoice_line_id   = l.OdooInvoiceLineId,
                }).ToList()
            };

            var result = await _odoo.PushInvoiceAsync(payload);
            var now    = DateTime.UtcNow;

            if (result.Success)
            {
                invoice.OdooStatus   = "SYNCED";
                invoice.OdooLastSync = now;
                invoice.OdooSyncDir  = "FROM_SAP";
                invoice.OdooErrorMsg = null;
                synced++;
            }
            else
            {
                invoice.OdooStatus   = "ERROR";
                invoice.OdooLastSync = now;
                invoice.OdooSyncDir  = "FROM_SAP";
                invoice.OdooErrorMsg = result.ErrorMessage;
                failed++;

                _logger.LogWarning(
                    "⚠️ Odoo push failed for OINV {DocEntry}: {Error}",
                    invoice.SapDocEntry,
                    result.ErrorMessage);
            }

            // Write result back to SAP OINV UDFs (U_Odoo_Status / U_Odoo_ErrorMsg).
            // Failures here are non-fatal — the Neon status is the source of truth
            // for the next push cycle; SAP UDFs will reconcile on the next invoice delta sync.
            try
            {
                _resultHandler.UpdateInvoiceStatus(invoice.SapDocEntry, result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "⚠️ Could not write Odoo sync result to SAP OINV {DocEntry} — will reconcile on next delta sync",
                    invoice.SapDocEntry);
            }
        }

        await _neonDb.SaveChangesAsync();

        _logger.LogInformation(
            "✅ Odoo invoice push completed | Synced={Synced} Failed={Failed}",
            synced, failed);
    }
}
