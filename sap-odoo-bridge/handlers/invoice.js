/**
 * Invoice handler  (SAP OINV → Odoo /api/invoices)
 *
 * SAP B1 webhook payload:
 *   { ObjectType: "13", Key: "1234", EventType: "Add" | "Update" }
 *
 * Field mapping reference (SAP Service Layer → Odoo payload):
 *   DocEntry            → sap_doc_entry
 *   DocNum              → doc_num
 *   CardCode            → customer_code
 *   CardName            → card_name
 *   DocDate             → invoice_date
 *   DocTotal            → doc_total        (includes VAT)
 *   VatSum              → vat_sum
 *   DocumentLines[]
 *     .ItemCode         → lines[].item_code
 *     .ItemDescription  → lines[].description
 *     .Quantity         → lines[].quantity
 *     .LineTotal        → lines[].line_total
 *     .GrossBuyPrice    → lines[].gross_buy_pr  (cost price for margin reporting)
 *     .BaseEntry        → lines[].base_entry
 *     .BaseLine         → lines[].base_line
 *     .U_Odoo_InvLineId → lines[].odoo_invoice_line_id
 *
 * Note: is_paid / paid_amount come from the payment event, not the invoice
 * itself. We default both to 0/false here; the payment handler will update
 * them when a matching payment is pushed.
 */

const { fetchFromSL } = require('../sapClient');
const { pushToOdoo }  = require('../odooClient');

module.exports = async function handleInvoice(sapWebhook) {
  const docEntry = sapWebhook.Key;

  console.log(`[Invoice] Fetching OINV DocEntry=${docEntry} from Service Layer`);

  const doc = await fetchFromSL(
    `/Invoices(${docEntry})?$expand=DocumentLines`
  );

  const payload = {
    sap_doc_entry:  doc.DocEntry,
    doc_num:        doc.DocNum,
    customer_code:  doc.CardCode,
    card_name:      doc.CardName      ?? null,
    invoice_date:   doc.DocDate,
    doc_total:      doc.DocTotal      ?? 0,
    vat_sum:        doc.VatSum        ?? 0,
    is_paid:        false,
    paid_amount:    0,
    lines: (doc.DocumentLines || []).map(l => ({
      item_code:            l.ItemCode,
      description:          l.ItemDescription,
      quantity:             l.Quantity       ?? 0,
      line_total:           l.LineTotal      ?? 0,
      gross_buy_pr:         l.GrossBuyPrice  ?? 0,
      base_entry:           l.BaseEntry      ?? 0,
      base_line:            l.BaseLine       ?? 0,
      odoo_invoice_line_id: l.U_Odoo_InvLineId ?? null,
    })),
  };

  await pushToOdoo('/api/invoices', payload);
  console.log(`[Invoice] OINV ${docEntry} pushed to Odoo`);
};
