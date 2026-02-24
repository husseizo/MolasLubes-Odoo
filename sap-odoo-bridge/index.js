/**
 * SAP B1 → Odoo Real-time Bridge
 *
 * Receives webhook POSTs from SAP B1 Service Layer, verifies HMAC
 * signatures, fetches the full document from Service Layer, maps it
 * to Odoo's expected payload format, and POSTs to Odoo.
 *
 * The .NET polling jobs (every 5 min) run in parallel as a
 * reconciliation safety net — no conflict because Odoo upserts
 * on sap_doc_entry so duplicate pushes are harmless.
 *
 * Routes
 *   POST /webhooks/sap/deliveries  ← SAP ObjectType 15 (ODLN)
 *   POST /webhooks/sap/invoices    ← SAP ObjectType 13 (OINV)
 *   POST /webhooks/sap/payments    ← SAP ObjectType 24 (ORCT)
 *   GET  /health                   ← uptime check
 */

require('dotenv').config();

const express    = require('express');
const verifyHmac = require('./middleware/verifyHmac');

const handleDelivery = require('./handlers/delivery');
const handleInvoice  = require('./handlers/invoice');
const handlePayment  = require('./handlers/payment');

const app  = express();
const PORT = process.env.PORT || 3000;

// Parse JSON bodies — must come before routes
app.use(express.json());

// ── Health check ─────────────────────────────────────────
app.get('/health', (_req, res) => {
  res.json({ status: 'ok', uptime: process.uptime() });
});

// ── Webhook routes ────────────────────────────────────────
//
// IMPORTANT: we always respond 200 BEFORE processing.
// SAP B1 considers the webhook delivered as soon as it gets a 2xx.
// If we delay the response until processing is done and something
// is slow, SAP may time out and retry — causing duplicate pushes.
// Errors are logged but don't affect the 200 already sent.

app.post('/webhooks/sap/deliveries', verifyHmac, (req, res) => {
  res.sendStatus(200);
  handleDelivery(req.body).catch(err =>
    console.error('[Delivery] Handler error:', err.message)
  );
});

app.post('/webhooks/sap/invoices', verifyHmac, (req, res) => {
  res.sendStatus(200);
  handleInvoice(req.body).catch(err =>
    console.error('[Invoice] Handler error:', err.message)
  );
});

app.post('/webhooks/sap/payments', verifyHmac, (req, res) => {
  res.sendStatus(200);
  handlePayment(req.body).catch(err =>
    console.error('[Payment] Handler error:', err.message)
  );
});

// ── Start ────────────────────────────────────────────────
app.listen(PORT, () => {
  console.log(`SAP-Odoo bridge running on port ${PORT}`);
  console.log(`  POST /webhooks/sap/deliveries`);
  console.log(`  POST /webhooks/sap/invoices`);
  console.log(`  POST /webhooks/sap/payments`);
  console.log(`  GET  /health`);
});
