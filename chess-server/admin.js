const crypto = require('node:crypto');
const path = require('node:path');
function attachAdmin(app, multiplayer) {
  app.get('/admin', (req, res) => {
    res.setHeader('Cache-Control', 'no-store');
    res.sendFile(path.join(__dirname, 'admin.html'));
  });
  app.get('/api/admin/rooms', (req, res) => {
    res.setHeader('Cache-Control', 'no-store');
    const expected = process.env.ADMIN_VIEW_KEY;
    if (!expected || expected.length < 24) return res.status(503).json({ error: 'Admin viewing is not configured.' });
    const supplied = (req.headers.authorization || '').replace(/^Bearer /, '');
    const digest = value => crypto.createHash('sha256').update(value).digest();
    if (!crypto.timingSafeEqual(digest(expected), digest(supplied))) return res.status(401).json({ error: 'Invalid admin key.' });
    res.json({ serverTime: new Date().toISOString(), rooms: [...multiplayer.rooms.values()].map(r => ({
      roomId: r.roomId, matchId: r.matchId, revision: r.revision,
      white: r.whiteName, black: r.blackName, status: r.status,
      turn: r.game.turn() === 'w' ? 'white' : 'black', fen: r.game.fen(),
      whiteTime: r.whiteTime, blackTime: r.blackTime, winner: r.winner || '', reason: r.reason || '',
      moves: r.game.history({ verbose: true }).map((m, i) => ({ ply: i + 1, color: m.color, san: m.san, from: m.from, to: m.to, promotion: m.promotion || '' }))
    })) });
  });
}
module.exports = { attachAdmin };
