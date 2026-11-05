const { Chess } = require('chess.js');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { verifySession } = require('./session');
const opposite = c => c === 'white' ? 'black' : 'white';
const color = game => game.turn() === 'w' ? 'white' : 'black';
const types = { p: 0, r: 1, n: 2, b: 3, q: 4, k: 5 };
function board(game) {
  const pieces = [];
  const rights = game.fen().split(' ')[2];
  for (const row of game.board()) for (const p of row) if (p) {
    let hasMoved = true;
    if (p.type === 'p') hasMoved = p.square[1] !== (p.color === 'w' ? '2' : '7');
    if (p.type === 'k') hasMoved = !(p.color === 'w' ? /[KQ]/ : /[kq]/).test(rights);
    const rookRight = { a1: 'Q', h1: 'K', a8: 'q', h8: 'k' }[p.square];
    if (p.type === 'r' && rookRight) hasMoved = !rights.includes(rookRight);
    pieces.push({ r: Number(p.square[1]) - 1, c: p.square.charCodeAt(0) - 97, type: types[p.type], color: p.color === 'w' ? 0 : 1, hasMoved });
  }
  return JSON.stringify({ pieces, turn: game.turn() === 'w' ? 0 : 1 });
}
function parseMove(value) {
  const m = typeof value === 'string' && /^([0-7]),([0-7])->([0-7]),([0-7])(?:=(Queen|Rook|Bishop|Knight))?$/.exec(value);
  if (!m) throw new Error('Invalid move format');
  return { from: String.fromCharCode(97 + +m[2]) + (+m[1] + 1), to: String.fromCharCode(97 + +m[4]) + (+m[3] + 1), promotion: ({ Queen:'q', Rook:'r', Bishop:'b', Knight:'n' })[m[5]] || 'q' };
}
function attachMultiplayer(io, options = {}) {
  const rooms = new Map(), players = new Map(), connections = new Map();
  const stateFile = options.stateFile || process.env.MATCH_STATE_FILE || path.join(__dirname, 'data', 'matches.json');
  const grace = options.grace || 30000;
  const duration = Number(options.duration || process.env.MATCH_SECONDS || 600);
  if (!Number.isFinite(duration) || duration < 1) throw new Error('Invalid MATCH_SECONDS');
  function save() {
    fs.mkdirSync(path.dirname(stateFile), { recursive: true });
    const data = [...rooms.values()].map(({ game, ready, requests, ...r }) => ({ ...r, moves: game.history(), ready: [], requests: [], white: null, black: null }));
    fs.writeFileSync(stateFile + '.tmp', JSON.stringify(data));
    fs.renameSync(stateFile + '.tmp', stateFile);
  }
  if (fs.existsSync(stateFile)) {
    for (const saved of JSON.parse(fs.readFileSync(stateFile, 'utf8'))) {
      const game = new Chess(); for (const move of saved.moves) game.move(move);
      const r = { ...saved, game, ready: new Set(), requests: new Set(), white: null, black: null, offline: {} };
      if (r.status !== 'finished' && r.blackId) { r.status = 'reconnecting'; r.offline = { white: Date.now() + grace, black: Date.now() + grace }; }
      else if (r.status === 'waiting') continue;
      rooms.set(r.roomId, r); players.set(r.whiteId, r.roomId); if (r.blackId) players.set(r.blackId, r.roomId);
    }
  }
  const emit = (target, event, data) => io.to(target).emit(event, JSON.stringify(data));
  function list() { return { rooms: [...rooms.values()].filter(r => r.status === 'waiting' && !r.isPrivate).map(r => ({ roomId: r.roomId, creatorName: r.whiteName })) }; }
  function broadcast() { io.emit('availableRoomsList', JSON.stringify(list())); }
  function snapshot(r) { return { roomId: r.roomId, matchId: r.matchId, revision: r.revision, boardState: board(r.game), captured: r.game.history({ verbose: true }).filter(m => m.captured).map(m => ({ type: types[m.captured], color: m.color === 'w' ? 1 : 0 })), currentTurn: color(r.game), whiteTime: r.whiteTime, blackTime: r.blackTime, legalMoves: r.game.moves({ verbose: true }).map(m => `${+m.from[1]-1},${m.from.charCodeAt(0)-97}->${+m.to[1]-1},${m.to.charCodeAt(0)-97}`), status: r.status, winner: r.winner || '', reason: r.reason || '' }; }
  function sync(r) { emit(r.roomId, 'stateSync', snapshot(r)); }
  function finish(r, winner, reason) { r.status = 'finished'; r.winner = winner; r.reason = reason; r.expires = Date.now() + 120000; sync(r); emit(r.roomId, 'gameOver', { winner, reason }); save(); broadcast(); }
  function remove(r) {
    for (const id of [r.whiteId, r.blackId]) if (players.get(id) === r.roomId) players.delete(id);
    io.in(r.roomId).socketsLeave(r.roomId); rooms.delete(r.roomId); save(); broadcast();
  }
  function tick(r, now = Date.now()) {
    if (r.status !== 'playing') return;
    const key = color(r.game) + 'Time'; r[key] = Math.max(0, r[key] - (now - r.lastTick) / 1000); r.lastTick = now;
    if (r[key] <= 0) finish(r, opposite(color(r.game)), 'timeout');
  }
  function start(r) {
    r.game = new Chess(); r.matchId = crypto.randomUUID(); r.revision = 0; r.status = 'playing';
    r.whiteTime = r.blackTime = duration; r.lastTick = Date.now(); r.offline = {}; r.requests.clear(); r.ready.clear(); r.winner = ''; r.reason = '';
    for (const c of ['white', 'black']) emit(r[c], 'gameStart', { roomId: r.roomId, color: c, opponentName: r[opposite(c) + 'Name'] });
    sync(r); save(); broadcast();
  }
  io.use((socket, next) => {
    const user = verifySession(socket.handshake.auth?.token || socket.handshake.query.token);
    if (!user) return next(new Error('Session expired. Please log in again.'));
    socket.data.user = user; next();
  });
  io.on('connection', socket => {
    const user = socket.data.user, id = user.sub;
    const old = connections.get(id); connections.set(id, socket.id);
    if (old) io.sockets.sockets.get(old)?.disconnect(true);
    const getRoom = () => rooms.get(players.get(id));
    const seat = r => r.whiteId === id ? 'white' : 'black';
    function restore(r) {
      const c = seat(r); r[c] = socket.id; delete r.offline[c]; socket.join(r.roomId);
      if (r.status === 'reconnecting' && r.white && r.black) { r.status = 'playing'; r.lastTick = Date.now(); emit(r.roomId, 'opponentReconnected', {}); }
      if (r.status === 'waiting') emit(socket.id, 'roomCreated', { roomId: r.roomId });
      else emit(socket.id, 'reconnectSuccess', { ...snapshot(r), color: c });
      sync(r); save();
    }
    const existing = getRoom(); if (existing) restore(existing); else emit(socket.id, 'reconnectFailed', { reason: 'No active match' });
    emit(socket.id, 'availableRoomsList', list());
    let count = 0, windowStart = Date.now();
    function on(event, handler) {
      socket.on(event, raw => {
        if (connections.get(id) !== socket.id) return;
        if (Date.now() - windowStart > 10000) { count = 0; windowStart = Date.now(); }
        if (++count > 100) return socket.disconnect(true);
        try {
          let data = raw === undefined ? {} : typeof raw === 'string' ? JSON.parse(raw) : raw;
          if (!data || typeof data !== 'object' || Array.isArray(data)) throw new Error('Invalid payload');
          handler(data);
        } catch (error) {
          emit(socket.id, 'joinError', { error: error.message });
          const r = getRoom(); if (r) { emit(socket.id, 'moveRejected', { error: error.message }); emit(socket.id, 'stateSync', snapshot(r)); }
        }
      });
    }
    on('getAvailableRooms', () => emit(socket.id, 'availableRoomsList', list()));
    on('requestState', () => { const r = getRoom(); if (r) { tick(r); emit(socket.id, 'stateSync', snapshot(r)); } else emit(socket.id, 'reconnectFailed', {}); });
    on('createRoom', data => {
      if (getRoom()) throw new Error('Leave your current room first');
      if (rooms.size >= 1000) throw new Error('Server is full');
      let roomId; do { roomId = crypto.randomBytes(4).toString('hex').slice(0, 6).toUpperCase(); } while (rooms.has(roomId));
      const r = { roomId, matchId: crypto.randomUUID(), revision: 0, white: socket.id, black: null, whiteId: id, blackId: null, whiteName: user.name, blackName: '', status: 'waiting', game: new Chess(), whiteTime: duration, blackTime: duration, isPrivate: data.isPrivate === true, offline: {}, ready: new Set(), requests: new Set(), expires: Date.now() + 1800000 };
      rooms.set(roomId, r); players.set(id, roomId); socket.join(roomId); emit(socket.id, 'roomCreated', { roomId }); save(); broadcast();
    });
    on('joinRoom', data => {
      if (getRoom()) throw new Error('Leave your current room first');
      if (typeof data.roomId !== 'string') throw new Error('Invalid room code');
      const r = rooms.get(data.roomId.trim().toUpperCase());
      if (!r || r.status !== 'waiting' || !r.white) throw new Error('Room unavailable');
      r.black = socket.id; r.blackId = id; r.blackName = user.name; players.set(id, r.roomId); socket.join(r.roomId); start(r);
    });
    on('move', data => {
      const r = getRoom(); if (!r) throw new Error('No active match'); tick(r);
      if (data.matchId !== r.matchId || data.revision !== r.revision) throw new Error('Stale move; board resynchronized');
      if (r.status !== 'playing' || seat(r) !== color(r.game)) throw new Error('Not your turn');
      r.game.move(parseMove(data.move)); r.revision++; r.lastTick = Date.now();
      if (r.game.isCheckmate()) finish(r, opposite(color(r.game)), 'checkmate');
      else if (r.game.isDraw()) finish(r, 'draw', 'draw');
      else { sync(r); save(); }
    });
    // The server alone decides checkmate, draws and timeouts.
    on('gameOver', () => { const r = getRoom(); if (r) sync(r); });
    on('resign', () => { const r = getRoom(); if (r && ['playing', 'reconnecting'].includes(r.status)) finish(r, opposite(seat(r)), 'resigned'); });
    on('leaveRoom', () => { const r = getRoom(); if (!r) return; if (r.blackId && r.status !== 'finished') finish(r, opposite(seat(r)), 'opponent_left'); emit(r.roomId, 'roomClosed', { reason: 'Player left' }); remove(r); });
    on('requestRematch', () => {
      const r = getRoom(); if (!r || r.status !== 'finished' || !r.white || !r.black) throw new Error('Rematch unavailable');
      const before = r.requests.size; r.requests.add(id);
      if (r.requests.size === before) return;
      if (r.requests.size === 2) { r.expires = Date.now() + 30000; emit(r.roomId, 'rematchAgreed', {}); }
      else emit(r[opposite(seat(r))], 'opponentRequestedRematch', {});
    });
    on('playerReadyForRematch', () => {
      const r = getRoom(); if (!r || r.status !== 'finished' || r.requests.size !== 2 || !r.white || !r.black) throw new Error('Rematch not agreed');
      r.ready.add(id); if (r.ready.size !== 2) return;
      for (const suffix of ['', 'Id', 'Name']) [r['white' + suffix], r['black' + suffix]] = [r['black' + suffix], r['white' + suffix]];
      start(r);
    });
    socket.on('disconnect', () => {
      if (connections.get(id) !== socket.id) return;
      connections.delete(id); const r = getRoom(); if (!r) return;
      tick(r); const c = seat(r); r[c] = null;
      if (r.status === 'waiting') return remove(r);
      if (['playing', 'reconnecting'].includes(r.status)) { r.status = 'reconnecting'; r.offline[c] = Date.now() + grace; emit(r.roomId, 'opponentDisconnected', { message: 'Connection lost. Waiting for reconnect.' }); sync(r); save(); }
    });
  });
  const timer = setInterval(() => {
    const now = Date.now();
    for (const r of rooms.values()) {
      tick(r, now);
      if (r.status === 'reconnecting') {
        const expired = Object.entries(r.offline).filter(([, deadline]) => now >= deadline);
        if (expired.length) finish(r, !r.white && !r.black ? 'draw' : opposite(expired[0][0]), 'disconnect_timeout');
      }
      if (['finished', 'waiting'].includes(r.status) && now >= r.expires) { emit(r.roomId, 'roomClosed', { reason: 'Room expired' }); remove(r); }
      else if (r.status === 'playing') emit(r.roomId, 'clockSync', { whiteTime: r.whiteTime, blackTime: r.blackTime });
    }
    save();
  }, 1000);
  return { rooms, close() { clearInterval(timer); save(); } };
}
module.exports = { attachMultiplayer, parseMove, board };
