/**
 * Delivery handler  (SAP ODLN → Odoo /api/deliveries)
 *
 * SAP B1 webhook payload:
 *   { ObjectType: "15", Key: "1234", EventType: "Add" | "Update" }
 *
 * Fetches the full DeliveryNote from Service Layer, maps every field
 * to the exact shape our Odoo /api/deliveries controller expects,
 * then POSTs it.
 *
 * Field mapping reference (SAP Service Layer → Odoo payload):
 *   DocEntry            → sap_doc_entry
 *   DocNum              → sap_doc_num
 *   CardCode            → card_code
 *   DocDate             → delivery_date
 *   DocumentLines[0]
 *     .BaseEntry        → base_order_entry   (SAP sales order DocEntry)
 *   Cancelled           → is_cancelled
 *   DocumentLines[]
 *     .LineNum          → lines[].line_num
 *     .ItemCode         → lines[].item_code
 *     .ItemDescription  → lines[].description
 *     .Quantity         → lines[].quantity
 *     .LineTotal        → lines[].line_total
 *     .BaseEntry        → lines[].base_entry
 *     .BaseLine         → lines[].base_line
 *     .U_Odoo_SOLineId  → lines[].odoo_sales_order_line_id
 */

const { fetchFromSL } = require('../sapClient');
const { pushToOdoo }  = require('../odooClient');

module.exports = async function handleDelivery(sapWebhook) {
  const docEntry = sapWebhook.Key;

  console.log(`[Delivery] Fetching ODLN DocEntry=${docEntry} from Service Layer`);

  const doc = await fetchFromSL(
    `/DeliveryNotes(${docEntry})?$expand=DocumentLines`
  );

  // Resolve the originating sales order from the first line's BaseEntry.
  // All lines on the same delivery share the same base order so line[0] is enough.
  const baseOrderEntry = doc.DocumentLines?.[0]?.BaseEntry ?? null;

  const payload = {
    sap_doc_entry:    doc.DocEntry,
    sap_doc_num:      doc.DocNum,
    card_code:        doc.CardCode,
    delivery_date:    doc.DocDate,
    base_order_entry: baseOrderEntry,
    is_cancelled:     doc.Cancelled === 'tYES',
    lines: (doc.DocumentLines || []).map(l => ({
      line_num:                 l.LineNum,
      item_code:                l.ItemCode,
      description:              l.ItemDescription,
      quantity:                 l.Quantity      ?? 0,
      line_total:               l.LineTotal     ?? 0,
      base_entry:               l.BaseEntry     ?? 0,
      base_line:                l.BaseLine      ?? 0,
      odoo_sales_order_line_id: l.U_Odoo_SOLineId ?? null,
    })),
  };

  await pushToOdoo('/api/deliveries', payload);
  console.log(`[Delivery] ODLN ${docEntry} pushed to Odoo`);
};
