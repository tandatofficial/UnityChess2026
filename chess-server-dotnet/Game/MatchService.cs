using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using ChessDotNet;
using Microsoft.AspNetCore.SignalR;

namespace ChessServer.Game;

public sealed record CreateRoomRequest(bool IsPrivate = false);
public sealed record MoveRequest(string? MatchId, int Revision, string? Move, double? WhiteTime = null, double? BlackTime = null);
public sealed record RoomList(IReadOnlyList<AvailableRoom> Rooms);
public sealed record AvailableRoom(string RoomId, string CreatorName);
public sealed record PieceData(int R, int C, int Type, int Color, bool HasMoved);
public sealed record CapturedPiece(int Type, int Color);
public sealed record BoardData(IReadOnlyList<PieceData> Pieces, int Turn);
public sealed record MatchSnapshot(string RoomId, string MatchId, int Revision, string BoardState,
    IReadOnlyList<CapturedPiece> Captured, string CurrentTurn, double WhiteTime, double BlackTime,
    IReadOnlyList<string> LegalMoves, string Status, string Winner, string Reason);
public sealed record GameStartData(string RoomId, string Color, string OpponentName);
public sealed record ServerError(string Error);

public sealed class MatchService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MatchRoom> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _players = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _connections = new(StringComparer.Ordinal);
    private readonly ILogger<MatchService> _logger;
    private readonly string _stateFile;
    private readonly TimeSpan _grace = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _duration;

    public int RoomCount { get { lock (_gate) return _rooms.Count; } }

    public MatchService(IConfiguration configuration, ILogger<MatchService> logger)
    {
        _logger = logger;
        var statePath = configuration["MATCH_STATE_FILE"] ?? Path.Combine(AppContext.BaseDirectory, "data", "matches.json");
        _stateFile = Path.GetFullPath(statePath);
        _duration = ReadDuration(configuration);
        Load();
    }

    public RoomList ListRooms()
    {
        lock (_gate)
            return new RoomList(_rooms.Values
                .Where(r => r.Status == "waiting" && !r.IsPrivate)
                .Select(r => new AvailableRoom(r.RoomId, r.WhiteName))
                .ToArray());
    }

    public async Task<IResult> CreateRoomAsync(ClaimsPrincipal user, CreateRoomRequest request, IHubContext<GameHub> hub)
    {
        var id = UserId(user);
        MatchRoom room;
        string? connection;
        lock (_gate)
        {
            if (_players.ContainsKey(id)) return Error(400, "Leave your current room first");
            if (_rooms.Count >= 1000) return Error(503, "Server is full");
            var roomId = NewRoomId();
            var username = UserName(user);
            room = new MatchRoom
            {
                RoomId = roomId, MatchId = Guid.NewGuid().ToString(), WhiteId = id, WhiteName = username,
                Status = "waiting", IsPrivate = request.IsPrivate, Game = new ChessGame(),
                WhiteTime = _duration.TotalSeconds, BlackTime = _duration.TotalSeconds,
                Expires = UtcNowMilliseconds() + (long)TimeSpan.FromMinutes(30).TotalMilliseconds
            };
            _rooms[roomId] = room;
            _players[id] = roomId;
            _connections.TryGetValue(id, out connection);
            Save();
        }
        if (connection is not null) await hub.Groups.AddToGroupAsync(connection, room.RoomId);
        await SendToUser(hub, id, "roomCreated", new { roomId = room.RoomId });
        await BroadcastRooms(hub);
        return Results.Ok(new { roomId = room.RoomId });
    }

    public async Task<IResult> JoinRoomAsync(ClaimsPrincipal user, string roomId, IHubContext<GameHub> hub)
    {
        var id = UserId(user);
        MatchRoom room;
        string? joiningConnection;
        string? creatorConnection;
        lock (_gate)
        {
            if (_players.ContainsKey(id)) return Error(400, "Leave your current room first");
            if (!_rooms.TryGetValue(roomId.Trim().ToUpperInvariant(), out room!) || room.Status != "waiting" || room.WhiteId is null)
                return Error(400, "Room unavailable");
            room.BlackId = id;
            room.BlackName = UserName(user);
            _players[id] = room.RoomId;
            _connections.TryGetValue(id, out joiningConnection);
            _connections.TryGetValue(room.WhiteId, out creatorConnection);
            Start(room);
            Save();
        }
        if (joiningConnection is not null) await hub.Groups.AddToGroupAsync(joiningConnection, room.RoomId);
        if (creatorConnection is not null) await hub.Groups.AddToGroupAsync(creatorConnection, room.RoomId);
        await SendToUser(hub, room.WhiteId!, "gameStart", new GameStartData(room.RoomId, "white", room.BlackName));
        await SendToUser(hub, id, "gameStart", new GameStartData(room.RoomId, "black", room.WhiteName));
        await SendState(hub, room);
        await BroadcastRooms(hub);
        return Results.Ok(Snapshot(room));
    }

    public async Task<IResult> GetStateAsync(ClaimsPrincipal user, string roomId, IHubContext<GameHub> hub)
    {
        MatchRoom room;
        var timedOut = false;
        IResult result;
        lock (_gate)
        {
            var id = UserId(user);
            if (!_rooms.TryGetValue(roomId, out room!) || (room.WhiteId != id && room.BlackId != id))
                return Error(404, "Room not found.");
            var wasPlaying = room.Status == "playing";
            Tick(room, DateTimeOffset.UtcNow);
            timedOut = wasPlaying && room.Status == "finished";
            if (timedOut) Save();
            result = Results.Ok(Snapshot(room));
        }
        if (timedOut)
        {
            await SendState(hub, room);
            await hub.Clients.Group(room.RoomId).SendAsync("gameOver", new { winner = room.Winner, reason = room.Reason });
        }
        return result;
    }

    public async Task<IResult> MoveAsync(ClaimsPrincipal user, string roomId, MoveRequest request, IHubContext<GameHub> hub)
    {
        MatchRoom room;
        var timedOut = false;
        lock (_gate)
        {
            var id = UserId(user);
            if (!TryGetPlayerRoom(id, roomId, out room, out var seat)) return Error(404, "Room not found.");
            var wasPlaying = room.Status == "playing";
            Tick(room, DateTimeOffset.UtcNow);
            timedOut = wasPlaying && room.Status == "finished";
            if (timedOut) Save();
            else
            {
                if (!string.Equals(request.MatchId, room.MatchId, StringComparison.Ordinal) || request.Revision != room.Revision)
                    return Error(409, "Stale move; board resynchronized");
                if (room.Status != "playing" || seat != Color(room.Game.WhoseTurn))
                    return Error(409, "Not your turn");
                if (!TryParseMove(request.Move, room.Game.WhoseTurn, out var move) || !room.Game.IsValidMove(move))
                    return Error(400, "Invalid move");
                room.Game.MakeMove(move, true);
                room.Revision++;
                room.LastTick = DateTimeOffset.UtcNow;
                if (room.Game.IsCheckmated(room.Game.WhoseTurn))
                    Finish(room, Opposite(Color(room.Game.WhoseTurn)), "checkmate");
                else if (room.Game.IsDraw())
                    Finish(room, "draw", "draw");
                Save();
            }
        }
        await SendState(hub, room);
        if (room.Status == "finished") await hub.Clients.Group(room.RoomId).SendAsync("gameOver", new { winner = room.Winner, reason = room.Reason });
        else await SendClock(hub, room);
        await BroadcastRooms(hub);
        if (timedOut) return Error(409, "Not your turn");
        return Results.Ok(Snapshot(room));
    }

    public async Task<IResult> ResignAsync(ClaimsPrincipal user, string roomId, IHubContext<GameHub> hub)
    {
        MatchRoom room;
        lock (_gate)
        {
            if (!TryGetPlayerRoom(UserId(user), roomId, out room, out var seat)) return Error(404, "Room not found.");
            if (room.Status is "playing" or "reconnecting") Finish(room, Opposite(seat), "resigned");
            Save();
        }
        await SendState(hub, room);
        if (room.Status == "finished") await hub.Clients.Group(room.RoomId).SendAsync("gameOver", new { winner = room.Winner, reason = room.Reason });
        return Results.Ok(Snapshot(room));
    }

    public async Task<IResult> LeaveAsync(ClaimsPrincipal user, string roomId, IHubContext<GameHub> hub)
    {
        MatchRoom room;
        string? whiteConnection;
        string? blackConnection;
        lock (_gate)
        {
            if (!TryGetPlayerRoom(UserId(user), roomId, out room, out var seat)) return Error(404, "Room not found.");
            if (room.BlackId is not null && room.Status != "finished") Finish(room, Opposite(seat), "opponent_left");
            whiteConnection = room.WhiteConnection;
            blackConnection = room.BlackConnection;
            _ = RemoveRoom(room);
            Save();
        }
        await hub.Clients.Group(room.RoomId).SendAsync("roomClosed", new { reason = "Player left" });
        if (whiteConnection is not null) await hub.Groups.RemoveFromGroupAsync(whiteConnection, room.RoomId);
        if (blackConnection is not null) await hub.Groups.RemoveFromGroupAsync(blackConnection, room.RoomId);
        await BroadcastRooms(hub);
        return Results.Ok(new { message = "Left room." });
    }

    public async Task<IResult> RequestRematchAsync(ClaimsPrincipal user, string roomId, IHubContext<GameHub> hub)
    {
        MatchRoom room;
        string id = UserId(user);
        string opponent;
        var agreed = false;
        lock (_gate)
        {
            if (!TryGetPlayerRoom(id, roomId, out room, out var seat) || room.Status != "finished" || room.WhiteId is null || room.BlackId is null)
                return Error(400, "Rematch unavailable");
            if (!room.RematchRequests.Add(id)) return Results.Ok(new { agreed = room.RematchRequests.Count == 2 });
            opponent = seat == "white" ? room.BlackId : room.WhiteId;
            agreed = room.RematchRequests.Count == 2;
            if (agreed) room.Expires = UtcNowMilliseconds() + 30_000;
            Save();
        }
        if (agreed) await hub.Clients.Group(room.RoomId).SendAsync("rematchAgreed", new { });
        else await SendToUser(hub, opponent, "opponentRequestedRematch", new { });
        return Results.Ok(new { agreed });
    }

    public async Task<IResult> ReadyForRematchAsync(ClaimsPrincipal user, string roomId, IHubContext<GameHub> hub)
    {
        MatchRoom room;
        var started = false;
        lock (_gate)
        {
            if (!TryGetPlayerRoom(UserId(user), roomId, out room, out _) ||
                room.Status != "finished" || room.RematchRequests.Count != 2 || room.WhiteId is null || room.BlackId is null)
                return Error(400, "Rematch not agreed");
            room.RematchReady.Add(UserId(user));
            if (room.RematchReady.Count == 2)
            {
                (room.WhiteId, room.BlackId) = (room.BlackId, room.WhiteId);
                (room.WhiteName, room.BlackName) = (room.BlackName, room.WhiteName);
                Start(room);
                Save();
                started = true;
            }
        }
        if (started)
        {
            await SendToUser(hub, room.WhiteId!, "gameStart", new GameStartData(room.RoomId, "white", room.BlackName));
            await SendToUser(hub, room.BlackId!, "gameStart", new GameStartData(room.RoomId, "black", room.WhiteName));
            await SendState(hub, room);
        }
        return Results.Ok(new { started });
    }

    public async Task ConnectedAsync(ClaimsPrincipal user, string connectionId, IHubContext<GameHub> hub)
    {
        var id = UserId(user);
        MatchRoom? room = null;
        string? roomId = null;
        string? oldConnection;
        string? seat = null;
        lock (_gate)
        {
            _connections.TryGetValue(id, out oldConnection);
            _connections[id] = connectionId;
            if (_players.TryGetValue(id, out var activeRoomId) && _rooms.TryGetValue(activeRoomId, out room))
            {
                roomId = room.RoomId;
                seat = room.WhiteId == id ? "white" : "black";
                if (seat == "white") room.WhiteConnection = connectionId;
                else room.BlackConnection = connectionId;
                room.Offline.Remove(seat);
            }
        }
        if (oldConnection is not null && oldConnection != connectionId)
        {
            await hub.Clients.Client(oldConnection).SendAsync("superseded", new { });
            if (roomId is not null) await hub.Groups.RemoveFromGroupAsync(oldConnection, roomId);
        }
        if (room is not null && seat is not null)
        {
            await hub.Groups.AddToGroupAsync(connectionId, room.RoomId);
            if (room.Status == "reconnecting" && room.WhiteConnection is not null && room.BlackConnection is not null)
            {
                lock (_gate) { room.Status = "playing"; room.LastTick = DateTimeOffset.UtcNow; Save(); }
                await hub.Clients.Group(room.RoomId).SendAsync("opponentReconnected", new { });
            }
            if (room.Status == "waiting")
                await hub.Clients.Client(connectionId).SendAsync("roomCreated", new { roomId = room.RoomId });
            else
                await hub.Clients.Client(connectionId).SendAsync("reconnectSuccess", SnapshotWithColor(room, seat));
            await SendState(hub, room);
        }
        else
            await hub.Clients.Client(connectionId).SendAsync("reconnectFailed", new { reason = "No active match" });
        await hub.Clients.Client(connectionId).SendAsync("availableRoomsList", ListRooms());
    }

    public async Task DisconnectedAsync(ClaimsPrincipal? user, string connectionId, IHubContext<GameHub> hub)
    {
        if (user?.Identity?.IsAuthenticated != true) return;
        var id = UserId(user);
        MatchRoom? room = null;
        var removedWaiting = false;
        var timedOut = false;
        lock (_gate)
        {
            if (!_connections.TryGetValue(id, out var active) || active != connectionId) return;
            _connections.Remove(id);
            if (!_players.TryGetValue(id, out var roomId) || !_rooms.TryGetValue(roomId, out room)) return;
            var wasPlaying = room.Status == "playing";
            Tick(room, DateTimeOffset.UtcNow);
            timedOut = wasPlaying && room.Status == "finished";
            var seat = room.WhiteId == id ? "white" : "black";
            if (seat == "white") room.WhiteConnection = null; else room.BlackConnection = null;
            if (room.Status == "waiting")
            {
                RemoveRoom(room);
                removedWaiting = true;
            }
            else if (room.Status is "playing" or "reconnecting")
            {
                room.Status = "reconnecting";
                room.Offline[seat] = DateTimeOffset.UtcNow + _grace;
                Save();
            }
            else if (timedOut)
                Save();
        }
        if (room is not null && !removedWaiting)
        {
            await SendState(hub, room);
            if (timedOut)
                await hub.Clients.Group(room.RoomId).SendAsync("gameOver", new { winner = room.Winner, reason = room.Reason });
            else if (room.Status == "reconnecting")
                await hub.Clients.Group(room.RoomId).SendAsync("opponentDisconnected", new { message = "Connection lost. Waiting for reconnect." });
        }
        await BroadcastRooms(hub);
    }

    public async Task TickAsync(IHubContext<GameHub> hub, CancellationToken cancellationToken)
    {
        List<MatchRoom> changed = [];
        List<MatchRoom> clockUpdates = [];
        List<MatchRoom> expired = [];
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var room in _rooms.Values.ToArray())
            {
                var wasPlaying = room.Status == "playing";
                Tick(room, now);
                if (wasPlaying && room.Status == "finished")
                    changed.Add(room);
                if (room.Status == "reconnecting" && room.Offline.Any(entry => now >= entry.Value))
                {
                    var timedOut = room.Offline.First(entry => now >= entry.Value).Key;
                    Finish(room, room.WhiteConnection is null && room.BlackConnection is null ? "draw" : Opposite(timedOut), "disconnect_timeout");
                    changed.Add(room);
                }
                else if (room.Status == "playing")
                    clockUpdates.Add(room);
                if (room.Status is "waiting" or "finished" && UtcNowMilliseconds() >= room.Expires)
                    expired.Add(room);
            }
            foreach (var room in expired) RemoveRoom(room);
            Save();
        }
        foreach (var room in changed)
        {
            await SendState(hub, room);
            await hub.Clients.Group(room.RoomId).SendAsync("gameOver", new { winner = room.Winner, reason = room.Reason }, cancellationToken);
        }
        foreach (var room in clockUpdates) await SendClock(hub, room);
        foreach (var room in expired)
        {
            await hub.Clients.Group(room.RoomId).SendAsync("roomClosed", new { reason = "Room expired" }, cancellationToken);
            if (room.WhiteConnection is not null) await hub.Groups.RemoveFromGroupAsync(room.WhiteConnection, room.RoomId, cancellationToken);
            if (room.BlackConnection is not null) await hub.Groups.RemoveFromGroupAsync(room.BlackConnection, room.RoomId, cancellationToken);
        }
        if (expired.Count > 0) await BroadcastRooms(hub);
    }

    private async Task SendState(IHubContext<GameHub> hub, MatchRoom room) =>
        await hub.Clients.Group(room.RoomId).SendAsync("stateSync", Snapshot(room));

    private static async Task SendClock(IHubContext<GameHub> hub, MatchRoom room) =>
        await hub.Clients.Group(room.RoomId).SendAsync("clockSync", new { whiteTime = room.WhiteTime, blackTime = room.BlackTime });

    private async Task BroadcastRooms(IHubContext<GameHub> hub) =>
        await hub.Clients.All.SendAsync("availableRoomsList", ListRooms());

    private async Task SendToUser(IHubContext<GameHub> hub, string userId, string eventName, object value)
    {
        string? connection;
        lock (_gate) _connections.TryGetValue(userId, out connection);
        if (connection is not null) await hub.Clients.Client(connection).SendAsync(eventName, value);
    }

    private object SnapshotWithColor(MatchRoom room, string color) =>
        new { roomId = room.RoomId, matchId = room.MatchId, revision = room.Revision, boardState = BoardState(room.Game),
            captured = Captured(room.Game), currentTurn = Color(room.Game.WhoseTurn), whiteTime = room.WhiteTime,
            blackTime = room.BlackTime, legalMoves = LegalMoves(room.Game), status = room.Status,
            winner = room.Winner, reason = room.Reason, color };

    private static MatchSnapshot Snapshot(MatchRoom room) =>
        new(room.RoomId, room.MatchId, room.Revision, BoardState(room.Game), Captured(room.Game),
            Color(room.Game.WhoseTurn), room.WhiteTime, room.BlackTime, LegalMoves(room.Game),
            room.Status, room.Winner, room.Reason);

    private static string BoardState(ChessGame game)
    {
        var rights = game.GetFen().Split(' ')[2];
        var pieces = new List<PieceData>();
        for (var rank = 1; rank <= 8; rank++)
        for (var file = 0; file < 8; file++)
        {
            var square = $"{(char)('a' + file)}{rank}";
            var piece = game.GetPieceAt(new Position(square));
            if (piece is null) continue;
            var fen = piece.GetFenCharacter();
            var lower = char.ToLowerInvariant(fen);
            var type = lower switch { 'p' => 0, 'r' => 1, 'n' => 2, 'b' => 3, 'q' => 4, _ => 5 };
            var isWhite = piece.Owner == Player.White;
            var moved = lower == 'p' ? rank != (isWhite ? 2 : 7) :
                lower == 'k' ? !(isWhite ? rights.Contains('K') || rights.Contains('Q') : rights.Contains('k') || rights.Contains('q')) :
                lower == 'r' && square is "a1" or "h1" or "a8" or "h8"
                    ? !rights.Contains(square switch { "a1" => 'Q', "h1" => 'K', "a8" => 'q', _ => 'k' })
                    : true;
            pieces.Add(new PieceData(rank - 1, file, type, isWhite ? 0 : 1, moved));
        }
        return JsonSerializer.Serialize(new BoardData(pieces, game.WhoseTurn == Player.White ? 0 : 1),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static IReadOnlyList<CapturedPiece> Captured(ChessGame game) =>
        game.Moves.Where(move => move.IsCapture && move.CapturedPiece is not null)
            .Select(move => new CapturedPiece(TypeFromFen(move.CapturedPiece!.GetFenCharacter()), move.Player == Player.White ? 1 : 0))
            .ToArray();

    private static IReadOnlyList<string> LegalMoves(ChessGame game) =>
        game.GetValidMoves(game.WhoseTurn).Select(move =>
            $"{move.OriginalPosition.Rank - 1},{(int)move.OriginalPosition.File}->{move.NewPosition.Rank - 1},{(int)move.NewPosition.File}").ToArray();

    private static bool TryParseMove(string? value, Player player, out Move move)
    {
        move = null!;
        if (value is null) return false;
        var match = System.Text.RegularExpressions.Regex.Match(value,
            "^([0-7]),([0-7])->([0-7]),([0-7])(?:=(Queen|Rook|Bishop|Knight))?$");
        if (!match.Success) return false;
        var from = $"{(char)('a' + int.Parse(match.Groups[2].Value))}{int.Parse(match.Groups[1].Value) + 1}";
        var to = $"{(char)('a' + int.Parse(match.Groups[4].Value))}{int.Parse(match.Groups[3].Value) + 1}";
        char? promotion = match.Groups[5].Value switch
        {
            "Queen" => 'q', "Rook" => 'r', "Bishop" => 'b', "Knight" => 'n', _ => null
        };
        if ((to[1] is '8' or '1') && promotion is null) promotion = 'q';
        move = new Move(from, to, player, promotion);
        return true;
    }

    private bool TryGetPlayerRoom(string id, string roomId, out MatchRoom room, out string seat)
    {
        seat = "";
        if (_players.TryGetValue(id, out var activeRoom) && string.Equals(activeRoom, roomId, StringComparison.OrdinalIgnoreCase) &&
            _rooms.TryGetValue(activeRoom, out room!))
        {
            seat = room.WhiteId == id ? "white" : "black";
            return true;
        }
        room = null!;
        return false;
    }

    private void Start(MatchRoom room)
    {
        room.Game = new ChessGame();
        room.MatchId = Guid.NewGuid().ToString();
        room.Revision = 0;
        room.Status = "playing";
        room.WhiteTime = room.BlackTime = _duration.TotalSeconds;
        room.LastTick = DateTimeOffset.UtcNow;
        room.Offline.Clear();
        room.RematchRequests.Clear();
        room.RematchReady.Clear();
        room.Winner = room.Reason = "";
    }

    private static void Finish(MatchRoom room, string winner, string reason)
    {
        room.Status = "finished";
        room.Winner = winner;
        room.Reason = reason;
        room.Expires = UtcNowMilliseconds() + 120_000;
    }

    private void Tick(MatchRoom room, DateTimeOffset now)
    {
        if (room.Status != "playing") return;
        var elapsed = Math.Max(0, (now - room.LastTick).TotalSeconds);
        if (room.Game.WhoseTurn == Player.White)
            room.WhiteTime = Math.Max(0, room.WhiteTime - elapsed);
        else
            room.BlackTime = Math.Max(0, room.BlackTime - elapsed);
        room.LastTick = now;
        if (room.Game.WhoseTurn == Player.White && room.WhiteTime <= 0)
            Finish(room, "black", "timeout");
        else if (room.Game.WhoseTurn == Player.Black && room.BlackTime <= 0)
            Finish(room, "white", "timeout");
    }

    private string? RemoveRoom(MatchRoom room)
    {
        foreach (var id in new[] { room.WhiteId, room.BlackId })
            if (id is not null && _players.GetValueOrDefault(id) == room.RoomId) _players.Remove(id);
        _rooms.Remove(room.RoomId);
        return room.RoomId;
    }

    private void Load()
    {
        if (!System.IO.File.Exists(_stateFile)) return;
        try
        {
            var matches = JsonSerializer.Deserialize<List<SavedMatch>>(System.IO.File.ReadAllText(_stateFile),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
            foreach (var saved in matches)
            {
                if (saved.Status == "waiting") continue;
                var room = new MatchRoom
                {
                    RoomId = saved.RoomId, MatchId = saved.MatchId, Revision = saved.Revision,
                    WhiteId = saved.WhiteId, BlackId = saved.BlackId, WhiteName = saved.WhiteName,
                    BlackName = saved.BlackName, Status = saved.Status, Winner = saved.Winner,
                    Reason = saved.Reason, WhiteTime = saved.WhiteTime, BlackTime = saved.BlackTime,
                    IsPrivate = saved.IsPrivate, Expires = saved.Expires, Game = new ChessGame()
                };
                foreach (var san in saved.Moves)
                {
                    var candidate = room.Game.GetValidMoves(room.Game.WhoseTurn)
                        .FirstOrDefault(move => PlayAndMatchSan(room.Game, move, san));
                    if (candidate is null) throw new InvalidDataException($"Unable to replay saved SAN move: {san}");
                }
                if (room.Status != "finished" && room.BlackId is not null)
                {
                    room.Status = "reconnecting";
                    room.Offline["white"] = DateTimeOffset.UtcNow + _grace;
                    room.Offline["black"] = DateTimeOffset.UtcNow + _grace;
                }
                _rooms[room.RoomId] = room;
                if (room.WhiteId is not null) _players[room.WhiteId] = room.RoomId;
                if (room.BlackId is not null) _players[room.BlackId] = room.RoomId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to restore saved matches from {StateFile}", _stateFile);
        }
    }

    private static bool PlayAndMatchSan(ChessGame game, Move candidate, string san)
    {
        if (!game.IsValidMove(candidate)) return false;
        game.MakeMove(candidate, true);
        if (game.Moves.Last().SAN == san) return true;
        game.Undo();
        return false;
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_stateFile)!;
            Directory.CreateDirectory(directory);
            var data = _rooms.Values.Select(room => new SavedMatch
            {
                RoomId = room.RoomId, MatchId = room.MatchId, Revision = room.Revision,
                WhiteId = room.WhiteId, BlackId = room.BlackId, WhiteName = room.WhiteName,
                BlackName = room.BlackName, Status = room.Status, Winner = room.Winner,
                Reason = room.Reason, WhiteTime = room.WhiteTime, BlackTime = room.BlackTime,
                IsPrivate = room.IsPrivate, Expires = room.Expires,
                Moves = room.Game.Moves.Select(move => move.SAN).ToList()
            }).ToArray();
            var temporary = _stateFile + ".tmp";
            System.IO.File.WriteAllText(temporary, JsonSerializer.Serialize(data, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            System.IO.File.Move(temporary, _stateFile, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to persist matches to {StateFile}", _stateFile);
        }
    }

    private string NewRoomId()
    {
        string id;
        do { id = Convert.ToHexString(RandomNumberGenerator.GetBytes(4))[..6]; }
        while (_rooms.ContainsKey(id));
        return id;
    }

    private static TimeSpan ReadDuration(IConfiguration configuration)
    {
        var value = int.TryParse(configuration["MATCH_SECONDS"], out var seconds) ? seconds : 600;
        if (value < 1) throw new InvalidOperationException("Invalid MATCH_SECONDS.");
        return TimeSpan.FromSeconds(value);
    }

    private static int TypeFromFen(char value) => char.ToLowerInvariant(value) switch
    {
        'p' => 0, 'r' => 1, 'n' => 2, 'b' => 3, 'q' => 4, _ => 5
    };

    private static string Color(Player player) => player == Player.White ? "white" : "black";
    private static string Opposite(string color) => color == "white" ? "black" : "white";
    private static string UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new HubException("Session expired. Please log in again.");
    private static string UserName(ClaimsPrincipal user) => user.Identity?.Name ?? user.FindFirstValue("name") ?? "Player";
    private static long UtcNowMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static IResult Error(int status, string message) => Results.Json(new { error = message }, statusCode: status);

    private sealed class MatchRoom
    {
        public required string RoomId { get; set; }
        public required string MatchId { get; set; }
        public int Revision { get; set; }
        public string? WhiteId { get; set; }
        public string? BlackId { get; set; }
        public string WhiteName { get; set; } = "";
        public string BlackName { get; set; } = "";
        public string? WhiteConnection { get; set; }
        public string? BlackConnection { get; set; }
        public string Status { get; set; } = "waiting";
        public string Winner { get; set; } = "";
        public string Reason { get; set; } = "";
        public double WhiteTime { get; set; }
        public double BlackTime { get; set; }
        public bool IsPrivate { get; set; }
        public long Expires { get; set; }
        public ChessGame Game { get; set; } = new();
        public DateTimeOffset LastTick { get; set; } = DateTimeOffset.UtcNow;
        public Dictionary<string, DateTimeOffset> Offline { get; } = new();
        public HashSet<string> RematchRequests { get; } = new();
        public HashSet<string> RematchReady { get; } = new();
    }

    private sealed class SavedMatch
    {
        public string RoomId { get; set; } = "";
        public string MatchId { get; set; } = "";
        public int Revision { get; set; }
        public string? WhiteId { get; set; }
        public string? BlackId { get; set; }
        public string WhiteName { get; set; } = "";
        public string BlackName { get; set; } = "";
        public string Status { get; set; } = "";
        public string Winner { get; set; } = "";
        public string Reason { get; set; } = "";
        public double WhiteTime { get; set; }
        public double BlackTime { get; set; }
        public bool IsPrivate { get; set; }
        public long Expires { get; set; }
        public List<string> Moves { get; set; } = [];
    }
}

public sealed class GameHub(MatchService matches, IHubContext<GameHub> hub) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await matches.ConnectedAsync(Context.User!, Context.ConnectionId, hub);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await matches.DisconnectedAsync(Context.User, Context.ConnectionId, hub);
        await base.OnDisconnectedAsync(exception);
    }

}

public sealed class MatchTicker(MatchService matches, IHubContext<GameHub> hub) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await matches.TickAsync(hub, stoppingToken);
    }
}
