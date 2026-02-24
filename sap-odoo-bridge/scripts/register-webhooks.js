/**
 * One-time webhook registration script
 *
 * Run this ONCE against your SAP B1 Service Layer to tell SAP
 * where to POST events. After that, SAP fires webhooks automatically
 * on every document Add/Update — no need to run this again unless
 * you change the listener URL or secret.
 *
 * Usage:
 *   cp .env.example .env        # fill in your values
 *   node scripts/register-webhooks.js
 *
 * To view registered webhooks:
 *   node scripts/register-webhooks.js --list
 *
 * To delete all registered webhooks:
 *   node scripts/register-webhooks.js --delete
 */

require('dotenv').config({ path: require('path').join(__dirname, '../.env') });

const axios = require('axios');
const https = require('https');

const args   = process.argv.slice(2);
const MODE   = args[0] ?? '--register';  // --register | --list | --delete

const slHttp = axios.create({
  baseURL: `https://${process.env.SAP_HOST}:50000/b1s/v1`,
  httpsAgent: new https.Agent({ rejectUnauthorized: false }),
});

// ── Webhooks to register ──────────────────────────────────
//
// Update LISTENER_BASE to the public URL where your Node.js
// service is reachable FROM the SAP B1 server.
//
const LISTENER_BASE = process.env.WEBHOOK_LISTENER_URL
  ?? `http://YOUR-PUBLIC-HOST:${process.env.PORT ?? 3000}`;

const WEBHOOKS = [
  {
    // SAP B1 Delivery Notes (ODLN) — ObjectType 15
    EventCode: 'DeliveryNotesEvent',
    URL:       `${LISTENER_BASE}/webhooks/sap/deliveries`,
    Secret:    process.env.SAP_WEBHOOK_SECRET,
    IsSystemEvent: false,
  },
  {
    // SAP B1 Invoices (OINV) — ObjectType 13
    EventCode: 'InvoicesEvent',
    URL:       `${LISTENER_BASE}/webhooks/sap/invoices`,
    Secret:    process.env.SAP_WEBHOOK_SECRET,
    IsSystemEvent: false,
  },
  {
    // SAP B1 Incoming Payments (ORCT) — ObjectType 24
    EventCode: 'IncomingPaymentsEvent',
    URL:       `${LISTENER_BASE}/webhooks/sap/payments`,
    Secret:    process.env.SAP_WEBHOOK_SECRET,
    IsSystemEvent: false,
  },
];

// ── Helpers ───────────────────────────────────────────────
async function login() {
  console.log('Logging in to SAP B1 Service Layer…');
  const res = await slHttp.post('/Login', {
    UserName:  process.env.SAP_USER,
    Password:  process.env.SAP_PASSWORD,
    CompanyDB: process.env.SAP_COMPANY,
  });

  const raw = res.headers['set-cookie'] || [];
  const b1  = raw.find(c => c.startsWith('B1SESSION'));
  if (!b1) throw new Error('No B1SESSION cookie in Login response');
  const cookie = b1.split(';')[0];
  console.log('Session OK\n');
  return cookie;
}

async function listWebhooks(cookie) {
  const res = await slHttp.get('/Webhooks', {
    headers: { Cookie: cookie },
  });
  return res.data?.value ?? [];
}

// ── Modes ─────────────────────────────────────────────────
async function register() {
  const cookie = await login();

  // Check what's already registered to avoid duplicates
  const existing = await listWebhooks(cookie);
  const existingCodes = existing.map(w => w.EventCode);

  for (const webhook of WEBHOOKS) {
    if (existingCodes.includes(webhook.EventCode)) {
      console.log(`  SKIP  ${webhook.EventCode} (already registered)`);
      continue;
    }

    await slHttp.post('/Webhooks', webhook, {
      headers: { Cookie: cookie },
    });
    console.log(`  OK    ${webhook.EventCode} → ${webhook.URL}`);
  }

  console.log('\nDone. SAP B1 will now POST to your listener on every document save.');
}

async function list() {
  const cookie   = await login();
  const webhooks = await listWebhooks(cookie);

  if (webhooks.length === 0) {
    console.log('No webhooks registered.');
    return;
  }

  console.log(`${webhooks.length} registered webhooks:\n`);
  for (const w of webhooks) {
    console.log(`  [${w.WebhookId}] ${w.EventCode}`);
    console.log(`         URL: ${w.URL}`);
    console.log('');
  }
}

async function deleteAll() {
  const cookie   = await login();
  const webhooks = await listWebhooks(cookie);

  if (webhooks.length === 0) {
    console.log('Nothing to delete.');
    return;
  }

  for (const w of webhooks) {
    await slHttp.delete(`/Webhooks(${w.WebhookId})`, {
      headers: { Cookie: cookie },
    });
    console.log(`  Deleted [${w.WebhookId}] ${w.EventCode}`);
  }
}

// ── Run ───────────────────────────────────────────────────
const run = MODE === '--list'   ? list
          : MODE === '--delete' ? deleteAll
          :                       register;

run().catch(err => {
  console.error('Error:', err.response?.data ?? err.message);
  process.exit(1);
});
