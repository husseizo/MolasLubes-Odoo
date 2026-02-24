require('dotenv').config();
const axios = require('axios');
const https = require('https');

const http = axios.create({
  baseURL: 'https://' + process.env.SAP_HOST + ':50000/b1s/v1',
  httpsAgent: new https.Agent({ rejectUnauthorized: false }),
});

async function run() {
  console.log('Logging in to', process.env.SAP_HOST, '...');
  const login = await http.post('/Login', {
    UserName:  process.env.SAP_USER,
    Password:  process.env.SAP_PASSWORD,
    CompanyDB: process.env.SAP_COMPANY,
  });

  const cookie = login.headers['set-cookie']
    .find(c => c.startsWith('B1SESSION'))
    .split(';')[0];

  console.log('Session OK. Fetching $metadata...');
  const meta = await http.get('/$metadata', { headers: { Cookie: cookie } });

  const count = (meta.data.match(/Webhook/g) || []).length;
  if (count > 0) {
    console.log('\nWEBHOOKS SUPPORTED (' + count + ' occurrences found)');
  } else {
    console.log('\nWEBHOOKS NOT FOUND — version may be too old');
  }
}

run().catch(e => console.error('Error:', e.response?.data ?? e.message));
