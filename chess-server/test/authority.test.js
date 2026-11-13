const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { attachMultiplayer } = require('../multiplayer');
const { issueSession } = require('../session');
const { attachAdmin } = require('../admin');
test('server owns legal moves, turn, revision and admin state', t => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'authority-'));
  const sent = []; let authenticate, connection;
  const io = { use(fn) { authenticate = fn; }, on(name, fn) { connection = fn; },
    to(target) { return { emit(event, payload) { sent.push({ target, event, payload }); } }; },
    emit() {}, in() { return { socketsLeave() {} }; }, sockets: { sockets: new Map() } };
  const manager = attachMultiplayer(io, { stateFile: path.join(dir, 'matches.json') });
  t.after(() => { manager.close(); fs.rmSync(dir, { recursive: true, force: true }); });
  function client(id) {
    const handlers = {};
    const socket = { id, data: {}, handshake: { auth: { token: issueSession({ id, username: id }) } },
      on(name, fn) { handlers[name] = fn; }, join() {}, disconnect() {} };
    authenticate(socket, error => assert.equal(error, undefined)); connection(socket);
    return { send(name, body) { handlers[name](body); } };
  }
  const white = client('alice'), black = client('bob');
  white.send('createRoom', {}); const room = [...manager.rooms.values()][0];
  black.send('joinRoom', { roomId: room.roomId });
  const initial = room.game.fen();
  black.send('move', { matchId: room.matchId, revision: 0, move: '6,4->4,4' });
  assert.equal(room.game.fen(), initial);
  white.send('move', { matchId: room.matchId, revision: 0, move: '1,4->4,4' });
  assert.equal(room.revision, 0);
  white.send('move', { matchId: room.matchId, revision: 0, move: '1,4->3,4', boardState: 'forged', whiteTime: 99999 });
  assert.equal(room.revision, 1); assert.equal(room.game.get('e4').type, 'p');
  assert.ok(room.whiteTime <= 600);
  white.send('move', { matchId: room.matchId, revision: 0, move: '1,4->3,4' });
  assert.equal(room.revision, 1);
  const syncs = sent.filter(x => x.event === 'stateSync' && x.target === room.roomId);
  assert.ok(syncs.length >= 2); assert.equal(JSON.parse(syncs.at(-1).payload).revision, 1);
  const routes = {}; attachAdmin({ get(url, handler) { routes[url] = handler; } }, manager);
  const previous = process.env.ADMIN_VIEW_KEY; process.env.ADMIN_VIEW_KEY = 'test-admin-key-123456789012345';
  t.after(() => { if (previous === undefined) delete process.env.ADMIN_VIEW_KEY; else process.env.ADMIN_VIEW_KEY = previous; });
  function read(key) { const res = { statusCode: 200, setHeader() {}, status(code) { this.statusCode = code; return this; }, json(value) { this.body = value; } }; routes['/api/admin/rooms']({ headers: { authorization: 'Bearer ' + key } }, res); return res; }
  assert.equal(read('wrong').statusCode, 401);
  const admin = read(process.env.ADMIN_VIEW_KEY); assert.equal(admin.statusCode, 200);
  assert.equal(admin.body.rooms[0].fen, room.game.fen());
  assert.equal(admin.body.rooms[0].moves[0].san, 'e4');
  assert.equal(admin.body.rooms[0].revision, 1);
  assert.equal(JSON.stringify(admin.body).includes(process.env.ADMIN_VIEW_KEY), false);
});
