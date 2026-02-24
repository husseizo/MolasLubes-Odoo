/**
 * SAP B1 Service Layer client
 *
 * Maintains a single session cookie and transparently re-logs-in
 * when SAP returns 401 (session expired — default lifetime 30 min).
 *
 * Usage:
 *   const { fetchFromSL } = require('./sapClient');
 *   const order = await fetchFromSL(`/Orders(${docEntry})`);
 */

const axios = require('axios');
const https = require('https');

// SAP B1 ships with a self-signed certificate on port 50000.
// Only disable verification for the internal SAP host.
const slHttp = axios.create({
  baseURL: `https://${process.env.SAP_HOST}:50000/b1s/v1`,
  httpsAgent: new https.Agent({ rejectUnauthorized: false }),
  timeout: 15_000,
});

let sessionCookie = null;

async function login() {
  console.log('[SAP] Logging in to Service Layer…');
  const res = await slHttp.post('/Login', {
    UserName:  process.env.SAP_USER,
    Password:  process.env.SAP_PASSWORD,
    CompanyDB: process.env.SAP_COMPANY,
  });

  const raw = res.headers['set-cookie'] || [];
  const b1  = raw.find(c => c.startsWith('B1SESSION'));
  if (!b1) throw new Error('B1SESSION cookie not found in Login response');

  sessionCookie = b1.split(';')[0]; // "B1SESSION=xxxxxxxx"
  console.log('[SAP] Session established');
}

/**
 * GET a resource from Service Layer.
 * Automatically refreshes the session once on 401.
 */
async function fetchFromSL(path) {
  if (!sessionCookie) await login();

  try {
    const res = await slHttp.get(path, {
      headers: { Cookie: sessionCookie },
    });
    return res.data;
  } catch (err) {
    if (err.response?.status === 401) {
      // Session expired — re-login and retry once
      sessionCookie = null;
      await login();
      const res = await slHttp.get(path, {
        headers: { Cookie: sessionCookie },
      });
      return res.data;
    }
    throw err;
  }
}

module.exports = { fetchFromSL };
