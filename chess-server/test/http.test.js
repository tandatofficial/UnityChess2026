const { test } = require('node:test');
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
test('HTTP boot, health, Swagger, malformed auth and missing database fail safely', async t => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'chess-http-'));
  const child = spawn(process.execPath, ['server.js'], {
    cwd: path.join(__dirname, '..'), windowsHide: true,
    env: { ...process.env, PORT: '0', NODE_ENV: 'test', SUPABASE_URL: '', SUPABASE_KEY: '', MATCH_STATE_FILE: path.join(dir, 'matches.json') },
    stdio: ['ignore', 'pipe', 'pipe']
  });
  t.after(async () => { if (child.exitCode === null) { const ended = new Promise(resolve => child.once('exit', resolve)); child.kill(); await ended; } fs.rmSync(dir, { recursive: true, force: true }); });
  const port = await new Promise((resolve, reject) => {
    let output = '';
    const timeout = setTimeout(() => reject(new Error('Server startup timeout: ' + output)), 10000);
    child.stdout.on('data', chunk => { output += chunk; const match = /running on port (\d+)/.exec(output); if (match) { clearTimeout(timeout); resolve(match[1]); } });
    child.stderr.on('data', chunk => { output += chunk; });
    child.once('exit', code => { clearTimeout(timeout); reject(new Error(`Server exited ${code}: ${output}`)); });
  });
  const base = `http://127.0.0.1:${port}`;
  assert.equal((await fetch(base + '/health')).status, 200);
  assert.equal((await fetch(base + '/api-docs/')).status, 200);
  const post = body => fetch(base + '/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  assert.equal((await post({ usernameOrEmail: {}, password: 'x' })).status, 400);
  assert.equal((await post({ usernameOrEmail: 'alice', password: 'password' })).status, 503);
});
