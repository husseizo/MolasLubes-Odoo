const crypto = require('crypto');

/**
 * Express middleware that validates the HMAC-SHA256 signature
 * SAP B1 Service Layer attaches to every outgoing webhook POST.
 *
 * SAP signs the raw JSON body with the Secret you supplied at
 * webhook registration and puts the hex digest in the header:
 *   X-SAP-Signature: <hex>
 *
 * We re-compute the HMAC from the body and compare with timingSafeEqual
 * so the check is immune to timing attacks.
 *
 * Returns 401 if the header is missing or the signature doesn't match.
 */
module.exports = function verifyHmac(req, res, next) {
  const signature = req.headers['x-sap-signature'] || '';

  if (!signature) {
    console.warn('[HMAC] Missing X-SAP-Signature header');
    return res.status(401).json({ error: 'Missing signature' });
  }

  const secret  = process.env.SAP_WEBHOOK_SECRET;
  const rawBody = JSON.stringify(req.body);

  const expected = crypto
    .createHmac('sha256', secret)
    .update(rawBody)
    .digest('hex');

  // Lengths must match before timingSafeEqual or it throws
  if (expected.length !== signature.length) {
    console.warn('[HMAC] Signature length mismatch — possible tampering');
    return res.status(401).json({ error: 'Invalid signature' });
  }

  const valid = crypto.timingSafeEqual(
    Buffer.from(expected),
    Buffer.from(signature)
  );

  if (!valid) {
    console.warn('[HMAC] Signature mismatch');
    return res.status(401).json({ error: 'Invalid signature' });
  }

  next();
};
