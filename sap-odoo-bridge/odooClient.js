/**
 * Odoo REST client
 *
 * Sends POST requests to Odoo's custom SAP-sync endpoints.
 * Attaches X-API-KEY on every request.
 * Retries up to MAX_RETRIES times on network errors or 5xx responses.
 *
 * Usage:
 *   const { pushToOdoo } = require('./odooClient');
 *   await pushToOdoo('/api/deliveries', payload);
 */

const axios = require('axios');

const MAX_RETRIES = 3;
const RETRY_DELAY_MS = 2000;

const odooHttp = axios.create({
  baseURL: process.env.ODOO_BASE_URL,
  timeout: 15_000,
  headers: {
    'Content-Type': 'application/json',
    'X-API-KEY':    process.env.ODOO_API_KEY,
  },
});

function sleep(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

async function pushToOdoo(endpoint, payload) {
  let lastError;

  for (let attempt = 1; attempt <= MAX_RETRIES; attempt++) {
    try {
      const res = await odooHttp.post(endpoint, payload);
      console.log(`[Odoo] ${endpoint} → ${res.status} (attempt ${attempt})`);
      return res.data;
    } catch (err) {
      lastError = err;
      const status = err.response?.status;

      // 4xx (except 429) are permanent failures — don't retry
      if (status && status >= 400 && status < 500 && status !== 429) {
        const body = err.response?.data ?? '';
        throw new Error(`Odoo ${endpoint} rejected with ${status}: ${JSON.stringify(body)}`);
      }

      if (attempt < MAX_RETRIES) {
        const delay = RETRY_DELAY_MS * attempt;
        console.warn(`[Odoo] ${endpoint} failed (attempt ${attempt}), retrying in ${delay}ms…`);
        await sleep(delay);
      }
    }
  }

  throw lastError;
}

module.exports = { pushToOdoo };
