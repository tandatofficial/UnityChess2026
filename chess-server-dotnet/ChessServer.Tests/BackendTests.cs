using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChessDotNet;
using ChessServer.Auth;
using ChessServer.Game;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ChessServer.Tests;

public sealed class BackendTests
{
    [Fact]
    public async Task Auth_health_docs_and_room_authorization_are_wired()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.PostAsJsonAsync("/api/auth/login", new { usernameOrEmail = "alice", password = "password" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/rooms")).StatusCode);
    }

    [Fact]
    public async Task Rooms_accept_only_server_validated_current_revision_moves()
    {
        using var factory = new TestFactory();
        using var alice = factory.ClientFor("alice", "Alice");
        using var bob = factory.ClientFor("bob", "Bob");

        var created = await alice.PostAsJsonAsync("/api/rooms", new { isPrivate = false });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var roomId = (await created.Content.ReadFromJsonAsync<RoomCreated>())!.RoomId;
        var listed = await bob.GetFromJsonAsync<RoomList>("/api/rooms");
        Assert.Contains(listed!.Rooms, room => room.RoomId == roomId);

        var joined = await bob.PostAsJsonAsync($"/api/rooms/{roomId}/join", new { });
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        var state = await joined.Content.ReadFromJsonAsync<MatchSnapshot>();
        Assert.Equal("playing", state!.Status);
        Assert.Equal(0, state.Revision);

        var e4 = await alice.PostAsJsonAsync($"/api/rooms/{roomId}/moves",
            new MoveRequest(state.MatchId, 0, "1,4->3,4"));
        Assert.Equal(HttpStatusCode.OK, e4.StatusCode);
        state = await e4.Content.ReadFromJsonAsync<MatchSnapshot>();
        Assert.Equal(1, state!.Revision);
        Assert.Equal("black", state.CurrentTurn);

        var wrongTurn = await alice.PostAsJsonAsync($"/api/rooms/{roomId}/moves",
            new MoveRequest(state.MatchId, 1, "1,3->3,3"));
        Assert.Equal(HttpStatusCode.Conflict, wrongTurn.StatusCode);

        var stale = await bob.PostAsJsonAsync($"/api/rooms/{roomId}/moves",
            new MoveRequest(state.MatchId, 0, "6,4->4,4"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var e5 = await bob.PostAsJsonAsync($"/api/rooms/{roomId}/moves",
            new MoveRequest(state.MatchId, 1, "6,4->4,4"));
        Assert.Equal(HttpStatusCode.OK, e5.StatusCode);
        state = await e5.Content.ReadFromJsonAsync<MatchSnapshot>();
        Assert.Equal(2, state!.Revision);
        Assert.Equal("white", state.CurrentTurn);
    }

    [Fact]
    public async Task Checkmate_is_decided_by_the_server()
    {
        using var factory = new TestFactory();
        using var alice = factory.ClientFor("mate-white", "White");
        using var bob = factory.ClientFor("mate-black", "Black");
        var created = await alice.PostAsJsonAsync("/api/rooms", new { isPrivate = true });
        var roomId = (await created.Content.ReadFromJsonAsync<RoomCreated>())!.RoomId;
        var state = await (await bob.PostAsJsonAsync($"/api/rooms/{roomId}/join", new { })).Content.ReadFromJsonAsync<MatchSnapshot>();

        foreach (var (client, move) in new[]
        {
            (alice, "1,5->2,5"), (bob, "6,4->4,4"),
            (alice, "1,6->3,6"), (bob, "7,3->3,7")
        })
        {
            var response = await client.PostAsJsonAsync($"/api/rooms/{roomId}/moves",
                new MoveRequest(state!.MatchId, state.Revision, move));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            state = await response.Content.ReadFromJsonAsync<MatchSnapshot>();
        }

        Assert.Equal("finished", state!.Status);
        Assert.Equal("black", state.Winner);
        Assert.Equal("checkmate", state.Reason);
    }

    [Fact]
    public async Task Saved_san_history_restores_a_paused_match()
    {
        var stateFile = Path.Combine(AppContext.BaseDirectory, $"restore-{Guid.NewGuid():N}.json");
        var firstFactory = new TestFactory(stateFile, cleanupStateFile: false);
        try
        {
            string roomId;
            string matchId;
            using (var alice = firstFactory.ClientFor("restore-white", "White"))
            using (var bob = firstFactory.ClientFor("restore-black", "Black"))
            {
                var created = await alice.PostAsJsonAsync("/api/rooms", new { isPrivate = true });
                roomId = (await created.Content.ReadFromJsonAsync<RoomCreated>())!.RoomId;
                var joined = await bob.PostAsJsonAsync($"/api/rooms/{roomId}/join", new { });
                var initial = await joined.Content.ReadFromJsonAsync<MatchSnapshot>();
                matchId = initial!.MatchId;
                var moved = await alice.PostAsJsonAsync($"/api/rooms/{roomId}/moves",
                    new MoveRequest(matchId, 0, "1,4->3,4"));
                Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
            }
            firstFactory.Dispose();

            using var restoredFactory = new TestFactory(stateFile, cleanupStateFile: false);
            using var restoredAlice = restoredFactory.ClientFor("restore-white", "White");
            var restored = await restoredAlice.GetFromJsonAsync<MatchSnapshot>($"/api/rooms/{roomId}");
            Assert.Equal("reconnecting", restored!.Status);
            Assert.Equal(matchId, restored.MatchId);
            Assert.Equal("black", restored.CurrentTurn);
            using var board = System.Text.Json.JsonDocument.Parse(restored.BoardState);
            Assert.Contains(board.RootElement.GetProperty("pieces").EnumerateArray(),
                piece => piece.GetProperty("r").GetInt32() == 3 &&
                         piece.GetProperty("c").GetInt32() == 4 &&
                         piece.GetProperty("type").GetInt32() == 0 &&
                         piece.GetProperty("color").GetInt32() == 0);
        }
        finally
        {
            firstFactory.Dispose();
            if (System.IO.File.Exists(stateFile)) System.IO.File.Delete(stateFile);
        }
    }

    [Fact]
    public async Task Resignation_requires_mutual_rematch_and_each_player_ready()
    {
        using var factory = new TestFactory();
        using var alice = factory.ClientFor("rematch-white", "White");
        using var bob = factory.ClientFor("rematch-black", "Black");
        var created = await alice.PostAsJsonAsync("/api/rooms", new { isPrivate = true });
        var roomId = (await created.Content.ReadFromJsonAsync<RoomCreated>())!.RoomId;
        var joined = await bob.PostAsJsonAsync($"/api/rooms/{roomId}/join", new { });
        var initial = await joined.Content.ReadFromJsonAsync<MatchSnapshot>();

        var resigned = await alice.PostAsJsonAsync($"/api/rooms/{roomId}/resign", new { });
        var finished = await resigned.Content.ReadFromJsonAsync<MatchSnapshot>();
        Assert.Equal("finished", finished!.Status);
        Assert.Equal("black", finished.Winner);
        Assert.Equal("resigned", finished.Reason);

        var firstRequest = await alice.PostAsJsonAsync($"/api/rooms/{roomId}/rematch", new { });
        Assert.False((await firstRequest.Content.ReadFromJsonAsync<RematchResponse>())!.Agreed);
        var secondRequest = await bob.PostAsJsonAsync($"/api/rooms/{roomId}/rematch", new { });
        Assert.True((await secondRequest.Content.ReadFromJsonAsync<RematchResponse>())!.Agreed);

        var firstReady = await alice.PostAsJsonAsync($"/api/rooms/{roomId}/ready", new { });
        Assert.False((await firstReady.Content.ReadFromJsonAsync<RematchResponse>())!.Started);
        var duplicateReady = await alice.PostAsJsonAsync($"/api/rooms/{roomId}/ready", new { });
        Assert.False((await duplicateReady.Content.ReadFromJsonAsync<RematchResponse>())!.Started);
        var secondReady = await bob.PostAsJsonAsync($"/api/rooms/{roomId}/ready", new { });
        Assert.True((await secondReady.Content.ReadFromJsonAsync<RematchResponse>())!.Started);

        var rematch = await alice.GetFromJsonAsync<MatchSnapshot>($"/api/rooms/{roomId}");
        Assert.Equal("playing", rematch!.Status);
        Assert.NotEqual(initial!.MatchId, rematch.MatchId);
        Assert.Equal(0, rematch.Revision);
    }

    [Fact]
    public async Task Server_clock_finishes_an_unanswered_turn()
    {
        using var factory = new TestFactory(durationSeconds: 1);
        using var alice = factory.ClientFor("timeout-white", "White");
        using var bob = factory.ClientFor("timeout-black", "Black");
        var created = await alice.PostAsJsonAsync("/api/rooms", new { isPrivate = true });
        var roomId = (await created.Content.ReadFromJsonAsync<RoomCreated>())!.RoomId;
        await bob.PostAsJsonAsync($"/api/rooms/{roomId}/join", new { });

        await Task.Delay(TimeSpan.FromMilliseconds(1250));
        var state = await alice.GetFromJsonAsync<MatchSnapshot>($"/api/rooms/{roomId}");
        Assert.Equal("finished", state!.Status);
        Assert.Equal("black", state.Winner);
        Assert.Equal("timeout", state.Reason);
        Assert.Equal(0, state.WhiteTime);
    }

    [Fact]
    public void Chess_engine_supports_castling_en_passant_and_all_promotion_choices()
    {
        var castle = new ChessGame("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        var castleMove = new Move("e1", "g1", Player.White);
        Assert.True(castle.IsValidMove(castleMove));
        castle.MakeMove(castleMove, true);
        Assert.Equal('R', castle.GetPieceAt(new Position("f1"))!.GetFenCharacter());

        var enPassant = new ChessGame();
        foreach (var move in new[]
        {
            new Move("e2", "e4", Player.White), new Move("a7", "a6", Player.Black),
            new Move("e4", "e5", Player.White), new Move("d7", "d5", Player.Black),
            new Move("e5", "d6", Player.White)
        })
        {
            Assert.True(enPassant.IsValidMove(move));
            enPassant.MakeMove(move, true);
        }
        Assert.Null(enPassant.GetPieceAt(new Position("d5")));
        Assert.Equal('P', enPassant.GetPieceAt(new Position("d6"))!.GetFenCharacter());

        foreach (var (promotion, expected) in new[] { ('q', 'Q'), ('r', 'R'), ('b', 'B'), ('n', 'N') })
        {
            var game = new ChessGame("7k/P7/8/8/8/8/8/7K w - - 0 1");
            var move = new Move("a7", "a8", Player.White, promotion);
            Assert.True(game.IsValidMove(move));
            game.MakeMove(move, true);
            Assert.Equal(expected, game.GetPieceAt(new Position("a8"))!.GetFenCharacter());
        }
    }

    private sealed class TestFactory : WebApplicationFactory<Program>
    {
        private readonly string _stateFile;
        private readonly bool _cleanupStateFile;

        private readonly int _durationSeconds;

        public TestFactory(string? stateFile = null, bool cleanupStateFile = true, int durationSeconds = 600)
        {
            _stateFile = stateFile ?? Path.Combine(AppContext.BaseDirectory, $"matches-{Guid.NewGuid():N}.json");
            _cleanupStateFile = cleanupStateFile;
            _durationSeconds = durationSeconds;
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SESSION_SECRET"] = "test-session-secret-at-least-thirty-two-characters",
                ["MATCH_STATE_FILE"] = _stateFile,
                ["MATCH_SECONDS"] = _durationSeconds.ToString(),
                ["SUPABASE_URL"] = "",
                ["SUPABASE_KEY"] = ""
            }));
            return base.CreateHost(builder);
        }

        public HttpClient ClientFor(string id, string name)
        {
            var tokens = new SessionTokens(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["SESSION_SECRET"] = "test-session-secret-at-least-thirty-two-characters" }).Build());
            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                tokens.Issue(new UserRecord(id, name, $"{id}@example.test", "", "", true)));
            return client;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && _cleanupStateFile && System.IO.File.Exists(_stateFile)) System.IO.File.Delete(_stateFile);
        }
    }

    private sealed record RoomCreated(string RoomId);
    private sealed record RematchResponse(bool Agreed, bool Started);
}
