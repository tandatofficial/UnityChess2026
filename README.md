🧩 Overview
This is a 2-player online chess game built in Unity, implementing all official rules of chess.

🎮 Features
✅ PvP online multiplayer with server-authoritative game state
✅ Full legal move logic (check, checkmate, stalemate, promotion, castling)
✅ Highlighting valid moves
✅ End game detection
✅ Restart game button
🎨 Simple and clean UI using Unity 2D
😅 (UI is a bit plain because I was lazy – didn’t polish it much)

🛠️ Technologies

Unity Engine (C#)

TextMeshPro for UI

Singleton pattern, ScriptableObjects, and Coroutines

ASP.NET Core REST + SignalR (.NET 10) for the active backend

## Backend migration

The new backend lives in [`chess-server-dotnet`](chess-server-dotnet/README.md). It retains the existing HTTP authentication contract and Supabase user table, uses REST for game commands/queries and SignalR for live updates, and adds Swagger/OpenAPI. The Unity client defaults to the new service. The original Node.js/Socket.IO backend remains in `chess-server` and is still deployed separately during validation; it has not been deleted or replaced.
