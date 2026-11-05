const { test } = require('node:test');
const assert = require('node:assert/strict');
const http = require('node:http');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { Server } = require('socket.io');
const { io: connect } = require('socket.io-client');
const { Chess } = require('chess.js');
const { issueSession, verifySession } = require('../session');
const { attachMultiplayer, parseMove, board } = require('../multiplayer');
function event(socket, name) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => { socket.off(name, handler); reject(new Error('Timeout: ' + name)); }, 4000);
    function handler(raw) { clearTimeout(timer); resolve(typeof raw === 'string' ? JSON.parse(raw) : raw); }
    socket.once(name, handler);
  });
}
async function setup(t, options = {}) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'chess-test-'));
  const server = http.createServer(); const io = new Server(server);
  const stateFile = path.join(dir, 'matches.json');
  const manager = attachMultiplayer(io, { stateFile, ...options });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const url = `http://127.0.0.1:${server.address().port}`;
  const sockets = [];
  async function client(id) {
    const socket = connect(url, { autoConnect: false, transports: ['websocket'], reconnection: false, auth: { token: issueSession({ id, username: id }) } });
    sockets.push(socket); const ready = event(socket, 'connect'); socket.connect(); await ready; return socket;
  }
  t.after(async () => { manager.close(); for (const socket of sockets) socket.disconnect(); await new Promise(resolve => io.close(resolve)); fs.rmSync(dir, { recursive: true, force: true }); });
  async function match() {
    const a = await client('alice'), b = await client('bob');
    const created = event(a, 'roomCreated'); a.emit('createRoom', {}); const { roomId } = await created;
    const state = event(a, 'stateSync'), otherState = event(b, 'stateSync'); b.emit('joinRoom', { roomId }); const initial = await state; await otherState;
    return { a, b, roomId, initial };
  }
  return { client, match, manager, io, url, stateFile };
}
test('signed sessions reject legacy, tampered and expired credentials', () => {
  const token = issueSession({ id: '1', username: 'alice' });
  assert.equal(verifySession(token).sub, '1');
  assert.equal(verifySession('UNITY'), null); assert.equal(verifySession(token + 'x'), null);
  const originalNow = Date.now;
  try { Date.now = () => originalNow() + 86400001; assert.equal(verifySession(token), null); }
  finally { Date.now = originalNow; }
});
test('coordinate mapping, castling, en passant and promotion use the rules engine', () => {
  const game = new Chess();
  for (const move of ['1,4->3,4', '6,0->5,0', '3,4->4,4', '6,3->4,3', '4,4->5,3']) game.move(parseMove(move));
  assert.equal(game.get('d5'), undefined); assert.equal(game.get('d6').color, 'w');
  const castle = new Chess('r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1'); castle.move(parseMove('0,4->0,6'));
  assert.equal(castle.get('f1').type, 'r');
  const promote = new Chess('7k/P7/8/8/8/8/8/7K w - - 0 1'); promote.move(parseMove('6,0->7,0=Knight'));
  assert.equal(promote.get('a8').type, 'n'); assert.equal(JSON.parse(board(promote)).pieces.find(p => p.r === 7 && p.c === 0).type, 2);
  assert.throws(() => parseMove('99,0->0,0'));
});
test('two clients: legal move, stale move, forged result, malformed input and persistence', async t => {
  const h = await setup(t); const { a, b, roomId, initial } = await h.match();
  const accepted = event(b, 'stateSync'); a.emit('move', { matchId: initial.matchId, revision: 0, move: '1,4->3,4', whiteTime: 99999 });
  const state = await accepted; assert.equal(state.revision, 1); assert.equal(state.currentTurn, 'black'); assert.ok(state.whiteTime <= 600);
  const rejected = event(a, 'moveRejected'); a.emit('move', { matchId: initial.matchId, revision: 0, move: '1,3->3,3' }); await rejected;
  const forged = event(b, 'stateSync'); b.emit('gameOver', { winner: 'black' }); assert.equal((await forged).status, 'playing');
  const malformed = event(b, 'joinError'); b.emit('joinRoom', null); await malformed;
  const invalid = event(b, 'moveRejected'); b.emit('move', { matchId: initial.matchId, revision: 1, move: '6,4->3,4' }); await invalid;
  assert.equal(h.manager.rooms.get(roomId).revision, 1);
  assert.deepEqual(JSON.parse(fs.readFileSync(h.stateFile))[0].moves, ['e4']);
});
test('both disconnect: first reconnect stays paused; second resumes; duplicate readiness cannot restart', async t => {
  const h = await setup(t); const { a, b, roomId } = await h.match();
  const paused = event(b, 'stateSync'); a.disconnect(); assert.equal((await paused).status, 'reconnecting'); b.disconnect();
  await new Promise(resolve => setTimeout(resolve, 30));
  const a2 = await h.client('alice');
  assert.equal(h.manager.rooms.get(roomId).status, 'reconnecting');
  const resumed = event(a2, 'stateSync'); const b2 = await h.client('bob'); assert.equal((await resumed).status, 'playing');
  const over = event(a2, 'gameOver'); b2.emit('resign'); await over;
  const rejected = event(a2, 'joinError'); a2.emit('playerReadyForRematch'); await rejected;
  a2.emit('requestRematch'); const agreed = event(a2, 'rematchAgreed'); b2.emit('requestRematch'); await agreed;
  a2.emit('playerReadyForRematch'); a2.emit('playerReadyForRematch');
  await new Promise(resolve => setTimeout(resolve, 30)); assert.equal(h.manager.rooms.get(roomId).status, 'finished');
  const started = event(a2, 'gameStart'); b2.emit('playerReadyForRematch'); assert.equal((await started).color, 'black');
});
test('leaving during reconnect informs opponent and removes room membership', async t => {
  const h = await setup(t); const { a, b, roomId } = await h.match();
  const paused = event(a, 'stateSync'); b.disconnect(); await paused;
  const closed = event(a, 'roomClosed'); a.emit('leaveRoom'); await closed;
  assert.equal(h.manager.rooms.has(roomId), false);
});
test('server clock ends match without trusting a client timeout', async t => {
  const h = await setup(t, { duration: 1 }); const { a } = await h.match();
  const result = await event(a, 'gameOver'); assert.equal(result.reason, 'timeout'); assert.equal(result.winner, 'black');
});
test('server detects checkmate and rejects further moves', async t => {
  const h = await setup(t); const { a, b, initial } = await h.match();
  let state = initial;
  for (const [socket, move] of [[a, '1,5->2,5'], [b, '6,4->4,4'], [a, '1,6->3,6'], [b, '7,3->3,7']]) {
    const updatedA = event(a, 'stateSync'), updatedB = event(b, 'stateSync');
    socket.emit('move', { matchId: state.matchId, revision: state.revision, move });
    state = await updatedA; await updatedB;
  }
  assert.equal(state.status, 'finished'); assert.equal(state.winner, 'black'); assert.equal(state.reason, 'checkmate');
  const rejected = event(a, 'moveRejected'); a.emit('move', { matchId: state.matchId, revision: state.revision, move: '1,0->2,0' }); await rejected;
});
test('legacy socket credentials are rejected at handshake', async t => {
  const h = await setup(t);
  const socket = connect(h.url, { autoConnect: false, transports: ['websocket'], reconnection: false, query: { token: 'UNITY', playerId: 'alice' } });
  t.after(() => socket.disconnect());
  const failure = event(socket, 'connect_error'); socket.connect();
  assert.match((await failure).message, /log in again/);
  assert.equal(socket.connected, false);
});
test('saved moves restore into a paused match after a server restart', async t => {
  const h = await setup(t); const { a, b, initial, roomId } = await h.match();
  const moved = event(b, 'stateSync'); a.emit('move', { matchId: initial.matchId, revision: 0, move: '1,4->3,4' }); await moved;
  h.manager.close();
  const secondIO = new Server(); const restored = attachMultiplayer(secondIO, { stateFile: h.stateFile });
  t.after(() => restored.close());
  assert.equal(restored.rooms.get(roomId).game.get('e4').type, 'p');
  assert.equal(restored.rooms.get(roomId).status, 'reconnecting');
});
