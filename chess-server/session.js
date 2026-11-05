const crypto = require('node:crypto');
const secret = process.env.SESSION_SECRET || (process.env.NODE_ENV === 'production' ? null : crypto.randomBytes(32).toString('hex'));
if (!secret || secret.length < 32) throw new Error('SESSION_SECRET must contain at least 32 characters');
function sign(body) { return crypto.createHmac('sha256', secret).update(body).digest('base64url'); }
function issueSession(user) {
  const body = Buffer.from(JSON.stringify({ sub: String(user.id), name: user.username, exp: Date.now() + 86400000 })).toString('base64url');
  return `${body}.${sign(body)}`;
}
function verifySession(token) {
  if (typeof token !== 'string' || token.length > 4096) return null;
  const parts = token.split('.'); if (parts.length !== 2) return null;
  const [body, signature] = parts;
  if (!body || !signature) return null;
  const expected = Buffer.from(sign(body)); const actual = Buffer.from(signature);
  if (actual.length !== expected.length || !crypto.timingSafeEqual(actual, expected)) return null;
  try { const user = JSON.parse(Buffer.from(body, 'base64url')); return user.sub && user.exp > Date.now() ? user : null; } catch { return null; }
}
module.exports = { issueSession, verifySession };
