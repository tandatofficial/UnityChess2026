# Chess server (.NET 10)

This is the replacement backend under active migration. The existing Node.js server remains in `../chess-server` and is still deployed separately as a rollback option.

## Local development

1. Install the .NET 10 SDK and copy `.env.example` to `.env` (the process does not automatically load `.env`; export the values in your shell or IDE launch profile).
2. Set `SUPABASE_URL`, the Supabase **service-role** `SUPABASE_KEY`, and `SESSION_SECRET` (at least 32 characters). Apply the existing `../chess-server/schema.sql` once to the Supabase project.
3. Configure Gmail app-password settings or Mailtrap credentials to enable verification mail.
4. Run `dotnet run --project ChessServer.csproj`. Set `PORT` to `11100` to use the current Unity local-server override.
5. Run `dotnet test ChessServer.Tests/ChessServer.Tests.csproj`. Open `/swagger` for the HTTP contract; health is `/health`.

The existing `/api/auth/register`, `/api/auth/verify`, and `/api/auth/login` request/response shapes and signed 24-hour session tokens are retained. The same token authorizes room REST requests and SignalR's `/hubs/game` endpoint.

## Multiplayer protocol

REST is authoritative for commands and queries:

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/rooms` | List public waiting rooms |
| POST | `/api/rooms` | Create `{ "isPrivate": false }` |
| POST | `/api/rooms/{roomId}/join` | Join a waiting room |
| GET | `/api/rooms/{roomId}` | Fetch an authoritative state snapshot |
| POST | `/api/rooms/{roomId}/moves` | Submit `{ "matchId", "revision", "move" }` |
| POST | `/api/rooms/{roomId}/resign` | Resign |
| POST | `/api/rooms/{roomId}/leave` | Leave and close a room |
| POST | `/api/rooms/{roomId}/rematch` | Request a rematch |
| POST | `/api/rooms/{roomId}/ready` | Mark readiness after both requests |

SignalR delivers the existing Unity event names (`roomCreated`, `gameStart`, `stateSync`, `clockSync`, `opponentDisconnected`, `opponentReconnected`, `rematchAgreed`, `availableRoomsList`, and others) as JSON hub messages. The Unity client connects using SignalR's JSON WebSocket protocol and uses REST for all commands.

Moves use the existing board coordinate format (for example `1,4->3,4`); the backend validates them with ChessDotNet. The server owns clocks, turns, legal moves, match results, reconnect grace, rematch agreement, and persisted SAN history. Match records use the existing JSON file location/shape so Node-created SAN histories can be replayed.

## Hosting and migration

The root `render.yaml` keeps the Node service and adds a separately named .NET 10 Docker service. Both default to the same Supabase table but use independent match-state files and secrets. Keep each service to one instance; in-memory rooms and SignalR connections are not distributed. File persistence on Render's free filesystem is temporary. Set stable `SESSION_SECRET`, `MATCH_STATE_FILE`, and the same Supabase/email settings on the new service. Point Unity's `CustomServerUrl` to the new service to switch; changing the default URL alone does not remove the Node service.

The new default Unity URL is `https://unity-chess-server-dotnet.onrender.com`; use `CustomServerUrl` for local or custom deployments. Verify TLS/WebSocket and account email delivery in deployed Unity builds because Unity's available WebSocket support varies by target (notably WebGL).
