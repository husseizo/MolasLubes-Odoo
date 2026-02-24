/**
 * Payment handler  (SAP ORCT → Odoo /api/payments)
 *
 * SAP B1 webhook payload:
 *   { ObjectType: "24", Key: "777", EventType: "Add" | "Update" }
 *
 * A single SAP incoming payment (ORCT) can be applied to multiple invoices
 * via the PaymentInvoices collection. We push one Odoo payment record per
 * invoice link so Odoo can reconcile each invoice independently.
 *
 * Field mapping reference (SAP Service Layer → Odoo payload):
 *   DocEntry              → sap_doc_entry
 *   DocNum                → doc_num
 *   CardCode              → customer_code
 *   DocDate               → payment_date
 *   PaymentInvoices[]
 *     .DocEntry           → invoice_entry   (FK → the OINV DocEntry)
 *     .SumApplied         → amount          (amount applied to this invoice)
 *
 * Note: when a payment covers multiple invoices we use the DocEntry of the
 * first invoice link as the sap_doc_entry key (Odoo upserts on this).
 * If your business always creates one payment per invoice this is a no-op.
 */

const { fetchFromSL } = require('../sapClient');
const { pushToOdoo }  = require('../odooClient');

module.exports = async function handlePayment(sapWebhook) {
  const docEntry = sapWebhook.Key;

  console.log(`[Payment] Fetching ORCT DocEntry=${docEntry} from Service Layer`);

  const doc = await fetchFromSL(
    `/IncomingPayments(${docEntry})?$expand=PaymentInvoices`
  );

  const links = (doc.PaymentInvoices || []).filter(l => l.DocEntry > 0);

  if (links.length === 0) {
    console.warn(`[Payment] ORCT ${docEntry} has no invoice links — skipping`);
    return;
  }

  for (const link of links) {
    const payload = {
      sap_doc_entry:  doc.DocEntry,
      doc_num:        doc.DocNum,
      customer_code:  doc.CardCode,
      invoice_entry:  link.DocEntry,    // FK → NeonInvoice / sap.invoice in Odoo
      amount:         link.SumApplied  ?? 0,
      payment_date:   doc.DocDate,
    };

    await pushToOdoo('/api/payments', payload);
    console.log(`[Payment] ORCT ${docEntry} → invoice ${link.DocEntry} pushed to Odoo`);
  }
};
