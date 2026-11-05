# Online chess server

Node 22+, one server instance. Unity and server must be updated together: old clients using the shared `UNITY` token are rejected.

## Local setup

1. Run `npm ci` in this directory.
2. Copy `.env.example` to `.env`. Fill Supabase URL and **service role** key, SMTP credentials, and a random `SESSION_SECRET` of at least 32 characters. Never put these keys in Unity.
3. Run `schema.sql` in Supabase. Existing duplicate usernames/emails must be resolved before creating the unique indexes. Review existing policies if using an existing database.
4. Run `npm test`, then `npm start`. Health endpoint: `/health`; API docs: `/api-docs`.
5. Configure both Unity clients to use the same server URL, register/verify two different accounts, then log in. A second connection to the same account replaces the first connection.

The session token is kept in Unity memory, lasts 24 hours, and survives scene changes. Restarting Unity requires logging in again. Account identity, rather than a generated device ID, allows rejoining an active match within the reconnect grace period.

## Hosting

The root `render.yaml` uses Render's **free web service**, without a persistent disk. Free services sleep after 15 minutes without inbound traffic and take time to wake up. Do not scale beyond one instance: the room registry and live sockets are not distributed.

`MATCH_STATE_FILE=./data/matches.json` stores SAN move history and clocks on the service's temporary filesystem. On Render Free, do not rely on it surviving sleep, restart or redeploy: active matches can be lost. Account data remains in Supabase. Durable match recovery requires external database storage or a paid persistent disk; the current match store uses a local file. When the file survives a restart, matches restore paused for a 30-second reconnect window. Keep `SESSION_SECRET` stable across restarts. A missing or short secret prevents production startup.

Set `SUPABASE_URL`, `SUPABASE_KEY`, and SMTP credentials in the hosting dashboard. Gmail uses `EMAIL_USER`/`EMAIL_PASS` (or the legacy Gmail values in `MAILTRAP_USER`/`MAILTRAP_PASS`). Mailtrap sandbox is for test inboxes, not real player email delivery. Registration fails if email cannot be delivered; verification codes are not exposed in logs or HTTP responses.

Use HTTPS/WSS publicly. Change `ServerConfig.DEFAULT_SERVER_URL` to the actual deployment URL. Local testing can set Unity `PlayerPrefs` key `CustomServerUrl` to `http://127.0.0.1:11100`. Inspector localhost values are intentionally bypassed by the existing URL resolver.

## Match behavior

- Server validates legal moves, castling, en passant and all four promotions using chess.js.
- Match ID and revision reject stale/duplicate moves. Full board snapshots acknowledge accepted moves and repair rejected moves. Clients request a fresh state after a 5-second acknowledgement timeout.
- Server decides checkmate, draw, resignation and timeouts. Client board/time/result values are not trusted.
- Online uses a shared sudden-death clock, default 600 seconds per side (`MATCH_SECONDS`). Local Unity clock preferences do not change online matches. Threefold repetition and fifty-move draws are automatically adjudicated by chess.js.
- Network disconnection pauses both clocks after server detection. Each absent seat has a 30-second deadline. Both players must be back before play resumes. If both remain absent, the match is drawn; otherwise the remaining player wins.
- Rematches require both accounts to agree and each account to become ready once. Colors swap. Finished rooms expire after two minutes; agreed rematches must become ready in 30 seconds.
- Waiting rooms expire after 30 minutes. Leaving a room closes it for both clients.

## Validation

`npm test` uses real localhost Socket.IO clients and temporary persistence files. It covers malformed packets, legal/illegal/stale moves, forged results, reconnect, rematch readiness, timeouts, persistence, and special chess moves.

Before release, run two actual Unity clients with different accounts and verify: login/email, create/join public and private rooms, captures, castling, en passant, all promotions, checkmate/draw, network loss on one/both sides, reconnect before/after grace expiry, resignation, rematch, server restart, and leaving during reconnect. MSBuild compilation alone does not validate scene/prefab wiring or player builds.
