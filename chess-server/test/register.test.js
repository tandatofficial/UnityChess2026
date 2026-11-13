const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { EventEmitter } = require('node:events');

// Run the real route with isolated database and mail adapters: no real accounts or emails.
function registrationHarness(options = {}) {
  const routes = new Map();
  const queries = [];
  const logs = [];
  let sent = 0;
  const app = { use() {}, get() {}, post(url, ...handlers) { routes.set(url, handlers); } };
  const express = () => app;
  express.json = () => () => {};
  const supabase = {
    from(table) {
      const query = { table };
      queries.push(query);
      const chain = {
        select() { return chain; },
        ilike(column, pattern) { Object.assign(query, { column, pattern }); return chain; },
        async maybeSingle() {
          return { data: options.user || (options.existingColumn === query.column ? {} : null),
            error: options.lookupError || null };
        },
        async insert(rows) { query.rows = rows; return { error: options.insertError || null }; },
        delete() { query.delete = true; return chain; },
        eq(column, value) { query[column] = value; return chain; },
        then(resolve, reject) { return Promise.resolve({ error: options.cleanupError || null }).then(resolve, reject); }
      };
      return chain;
    }
  };
  const stubs = {
    dotenv: { config() {} },
    express,
    cors: () => () => {},
    '@supabase/supabase-js': { createClient: () => supabase },
    nodemailer: { createTransport: () => ({ async sendMail() {
      if (options.mailError) throw options.mailError;
      sent++;
    } }) },
    http: { createServer: () => ({ listen() {}, close() {} }) },
    'socket.io': { Server: class {} },
    'swagger-jsdoc': () => ({}),
    'swagger-ui-express': { serve() {}, setup: () => () => {} },
    './session': { issueSession() { return 'test-session'; } },
    './multiplayer': { attachMultiplayer: () => ({ rooms: new Map() }) }
  };
  const serverPath = path.join(__dirname, '..', 'server.js');
  vm.runInNewContext(fs.readFileSync(serverPath, 'utf8'), {
    require: name => Object.hasOwn(stubs, name) ? stubs[name] : require(name),
    __dirname: path.dirname(serverPath), __filename: serverPath,
    process: { env: { SUPABASE_URL: 'https://database.invalid', SUPABASE_KEY: 'test',
      EMAIL_USER: 'test@example.com', EMAIL_PASS: 'test' }, on() {} },
    console: Object.fromEntries(['info', 'log', 'warn', 'error'].map(level => [level, (...args) => logs.push(args)])),
    Buffer
  });
  return {
    async login(body) {
      const res = { statusCode: 200, status(code) { this.statusCode = code; return this; }, json(value) { this.body = value; return this; } };
      await routes.get('/api/auth/login')[0]({ body }, res);
      return res;
    },
    queries, logs, get sent() { return sent; },
    async register(body = { username: 'player_one', email: 'player_one@example.com', password: 'password123' }) {
      const res = new EventEmitter();
      const headers = {};
      Object.assign(res, {
        locals: {}, statusCode: 200,
        setHeader(key, value) { headers[key] = value; },
        getHeader(key) { return headers[key]; },
        status(code) { this.statusCode = code; return this; },
        json(value) { this.body = value; this.emit('finish'); return this; }
      });
      const [trace, handler] = routes.get('/api/auth/register');
      trace({ body }, res, () => {});
      await handler({ body }, res);
      return res;
    }
  };
}

test('register treats underscores literally and logs correlated completion without credentials', async () => {
  const h = registrationHarness();
  const res = await h.register();
  assert.equal(res.statusCode, 200);
  assert.equal(h.queries[0].pattern, 'player\\_one@example.com');
  assert.equal(h.queries[1].pattern, 'player\\_one');
  assert.equal(h.sent, 1);
  const started = JSON.parse(h.logs[0][0]);
  const completed = JSON.parse(h.logs.at(-1)[0]);
  assert.equal(started.requestId, res.getHeader('X-Request-Id'));
  assert.equal(completed.requestId, started.requestId);
  assert.equal(completed.stage, 'complete');
  assert.equal(completed.status, 200);
  assert.equal(JSON.stringify(h.logs).includes('password123'), false);
  assert.equal(JSON.stringify(h.logs).includes(h.queries[2].rows[0].verification_code), false);
});

test('concurrent duplicate insert returns 400 instead of 500', async () => {
  const h = registrationHarness({ insertError: { code: '23505' } });
  const res = await h.register();
  assert.equal(res.statusCode, 400);
  assert.equal(h.sent, 0);
});

test('database failure is distinguishable from mail failure in register logs', async () => {
  const h = registrationHarness({ lookupError: { code: '42501' } });
  const res = await h.register();
  assert.equal(res.statusCode, 500);
  assert.equal(JSON.parse(h.logs.at(-1)[0]).stage, 'email_lookup');
  assert.equal(h.sent, 0);
});

test('mail failure returns 503, rolls back unverified user and records SMTP error code', async () => {
  const h = registrationHarness({ mailError: { code: 'ETIMEDOUT', command: 'CONN' },
    cleanupError: { code: '42501' } });
  const res = await h.register();
  assert.equal(res.statusCode, 503);
  const cleanup = h.queries.at(-1);
  assert.equal(cleanup.delete, true);
  assert.equal(cleanup.is_verified, false);
  assert.equal(cleanup.email, 'player_one@example.com');
  assert.ok(h.logs.some(args => args[0] === 'Registration rollback failed:'));
  assert.ok(h.logs.some(args => String(args[0]).includes('ETIMEDOUT')));
  assert.equal(JSON.parse(h.logs.at(-1)[0]).stage, 'send_verification_email');
});

test('existing account and invalid input do not create a user', async () => {
  const h = registrationHarness({ existingColumn: 'email' });
  assert.equal((await h.register()).statusCode, 400);
  assert.equal(h.queries.length, 1);
  const invalid = registrationHarness();
  assert.equal((await invalid.register({ username: 'x', email: 'bad', password: 'short' })).statusCode, 400);
  assert.equal(invalid.queries.length, 0);
});


test('login distinguishes database failure from missing account', async () => {
  const failed = registrationHarness({ lookupError: { code: '42501' } });
  const unavailable = await failed.login({ usernameOrEmail: 'player_one', password: 'password123' });
  assert.equal(unavailable.statusCode, 503);
  assert.match(unavailable.body.error, /Database temporarily unavailable/);
  const missing = registrationHarness();
  const absent = await missing.login({ usernameOrEmail: 'player_one', password: 'password123' });
  assert.equal(absent.statusCode, 400);
  assert.equal(absent.body.error, 'Account not found.');
});

for (const legacy of [true, false]) {
  test('login accepts correct password and rejects wrong password for ' + (legacy ? 'legacy' : 'current') + ' hash', async () => {
    const crypto = require('node:crypto');
    const salt = '0123456789abcdef0123456789abcdef';
    const hash = crypto.pbkdf2Sync('password123', Buffer.from(salt, 'hex'), legacy ? 10000 : 600000, 32, 'sha256').toString('hex');
    const user = { username: 'player_one', email: 'player_one@example.com', salt,
      password_hash: legacy ? hash : 'pbkdf2-sha256:600000:' + hash, is_verified: true };
    const h = registrationHarness({ user });
    const valid = await h.login({ usernameOrEmail: user.username, password: 'password123' });
    assert.equal(valid.statusCode, 200);
    assert.equal(valid.body.token, 'test-session');
    const invalid = await h.login({ usernameOrEmail: user.username, password: 'wrong-password' });
    assert.equal(invalid.statusCode, 400);
    assert.equal(invalid.body.error, 'Invalid password.');
  });
}
