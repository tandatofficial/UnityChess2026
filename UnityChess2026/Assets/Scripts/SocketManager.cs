using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public static class ServerConfig
{
    public const string DEFAULT_SERVER_URL = "https://unitychess2026-wjjq.onrender.com";

    public static string GetBaseUrl(string inspectorOverride = null)
    {
        if (PlayerPrefs.HasKey("CustomServerUrl"))
        {
            string prefUrl = PlayerPrefs.GetString("CustomServerUrl", "").Trim();
            if (!string.IsNullOrEmpty(prefUrl)) return NormalizeUrl(prefUrl);
        }
        if (!string.IsNullOrWhiteSpace(inspectorOverride))
        {
            string normalizedOverride = NormalizeUrl(inspectorOverride);
            if (!normalizedOverride.Equals("http://localhost:11100", StringComparison.OrdinalIgnoreCase)) return normalizedOverride;
        }
        if (SocketManager.Instance != null && !string.IsNullOrWhiteSpace(SocketManager.Instance.serverUrl))
        {
            string normalizedSocketUrl = NormalizeUrl(SocketManager.Instance.serverUrl);
            if (!normalizedSocketUrl.Equals("http://localhost:11100", StringComparison.OrdinalIgnoreCase)) return normalizedSocketUrl;
        }
        return NormalizeUrl(DEFAULT_SERVER_URL);
    }

    public static string NormalizeUrl(string url) =>
        string.IsNullOrWhiteSpace(url) ? DEFAULT_SERVER_URL : url.Trim().TrimEnd('/');
}

public class SocketManager : MonoBehaviour
{
    private static SocketManager _instance;
    public static SocketManager Instance
    {
        get
        {
            if (_instance == null) _instance = UnityEngine.Object.FindFirstObjectByType<SocketManager>();
            return _instance;
        }
        set { _instance = value; }
    }

    [Header("Network State")]
    public PieceColor assignedColor;
    public bool isSpectator;
    public bool isOnlineMode;
    public string currentRoomId;
    public string opponentName;

    [Header("Connection Settings")]
    public string serverUrl = ServerConfig.DEFAULT_SERVER_URL;

    [Header("UI References (Optional - Legacy)")]
    public TMP_InputField usernameInput;
    public TextMeshProUGUI playerNameText;
    public Button joinButton;

    public event Action<string> OnRoomCreated;
    public event Action<string, string> OnGameStarted;
    public event Action<string> OnJoinError;
    public event Action<string> OnMoveReceived;
    public event Action<string, string> OnGameOver;
    public event Action<string> OnOpponentDisconnected;
    public event Action<string> OnOpponentReconnected;
    public event Action<string> OnReconnectSuccess;
    public event Action<string> OnReconnectFailed;
    public event Action OnConnected;
    public event Action OnDisconnected;
    public event Action OnOpponentRequestedRematch;
    public event Action OnRematchAgreed;
    public event Action<string> OnAvailableRoomsListReceived;

    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    private readonly Queue<string> pendingNetworkMoves = new Queue<string>();
    private ClientWebSocket socket;
    private CancellationTokenSource socketCancellation;
    private readonly SemaphoreSlim socketSendLock = new SemaphoreSlim(1, 1);
    private string playerName;
    private bool isConnected;
    private bool applicationQuitting;
    private bool manualDisconnect;
    private bool reconnectScheduled;
    private ServerState latestState;
    private bool boardReadyForSync;
    private bool stateDirty;
    private bool awaitingMove;
    private float moveSentAt;
    private string endedMatch;

    public string playerId { get; private set; }
    public bool CanSubmitMove => IsConnected && boardReadyForSync && !awaitingMove &&
                                 latestState != null && latestState.status == "playing" && !isSpectator &&
                                 latestState.currentTurn == (assignedColor == PieceColor.White ? "white" : "black");

    public void MarkBoardReady()
    {
        boardReadyForSync = true;
        stateDirty = latestState != null;
    }

    public List<Vector2Int> LegalTargets(int row, int col)
    {
        var result = new List<Vector2Int>();
        if (latestState == null || latestState.legalMoves == null) return result;
        string prefix = row + "," + col + "->";
        foreach (string move in latestState.legalMoves)
        {
            if (!move.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string[] target = move.Substring(prefix.Length).Split(',');
            var point = new Vector2Int(int.Parse(target[0]), int.Parse(target[1]));
            if (!result.Contains(point)) result.Add(point);
        }
        return result;
    }

    [Serializable]
    private class ServerState
    {
        public string roomId, matchId, boardState, currentTurn, status, winner, reason, color;
        public int revision;
        public float whiteTime, blackTime;
        public string[] legalMoves;
        public NetworkPieceData[] captured;
    }

    [Serializable] private class NetworkPieceData { public int type, color; }
    [Serializable] private class ErrorData { public string error; }
    [Serializable] private class RoomCreatedData { public string roomId; }
    [Serializable] private class GameStartData { public string roomId, color, opponentName; }
    [Serializable] private class MoveData { public string move; public float whiteTime, blackTime; }
    [Serializable] private class DisconnectData { public string message; }
    [Serializable] private class NetworkMoveData { public string matchId, move; public int revision; }

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this) Destroy(gameObject);
    }

    private void OnApplicationQuit()
    {
        applicationQuitting = true;
        Disconnect();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (applicationQuitting) Disconnect();
    }

    private void Update()
    {
        while (mainThreadActions.TryDequeue(out Action action)) action?.Invoke();
        ApplyServerState();

        if (awaitingMove && IsConnected && Time.realtimeSinceStartup - moveSentAt > 5f)
        {
            moveSentAt = Time.realtimeSinceStartup;
            RequestState();
        }

        if (pendingNetworkMoves.Count > 0 && IsBoardReady())
        {
            string move = pendingNetworkMoves.Dequeue();
            try { GameManager.Instance.ApplyMoveFromNetwork(move); }
            catch (Exception ex) { Debug.LogError($"[Socket.IO] Could not apply remote move: {ex}"); }
        }
    }

    private void EnqueueMainThread(Action action) => mainThreadActions.Enqueue(action);

    private bool IsBoardReady()
    {
        if (GameManager.Instance == null || GameManager.Instance.currentGameMode != GameMode.Online ||
            GameManager.Instance.pieceBoard == null) return false;
        foreach (var piece in GameManager.Instance.pieceBoard) if (piece != null) return true;
        return false;
    }

    private void ApplyServerState()
    {
        if (!stateDirty || !boardReadyForSync || !IsBoardReady() || latestState == null) return;
        stateDirty = false;
        var manager = GameManager.Instance;
        manager.ApplyOnlineState(latestState.boardState, latestState.status != "playing");
        if (CaptureManager.Instance != null) CaptureManager.Instance.SyncOnlineCaptured(ConvertCapturedPieces(latestState.captured));
        if (ChessClock.Instance != null) ChessClock.Instance.SyncClockTimes(latestState.whiteTime, latestState.blackTime);
        if (latestState.status == "finished" && endedMatch != latestState.matchId)
        {
            endedMatch = latestState.matchId;
            manager.HandleNetworkGameOver(latestState.winner, latestState.reason);
            OnGameOver?.Invoke(latestState.winner, latestState.reason);
        }
    }

    private static PieceData[] ConvertCapturedPieces(NetworkPieceData[] networkPieces)
    {
        if (networkPieces == null) return null;

        var pieces = new PieceData[networkPieces.Length];
        for (int i = 0; i < networkPieces.Length; i++)
        {
            pieces[i] = new PieceData
            {
                type = (PieceType)networkPieces[i].type,
                color = (PieceColor)networkPieces[i].color
            };
        }

        return pieces;
    }

    public void ConnectToServer(string name)
    {
        if (IsConnected) return;
        playerName = string.IsNullOrWhiteSpace(name) ? "Player" : name;
        if (string.IsNullOrEmpty(OnlineSession.Token))
        {
            OnJoinError?.Invoke("Please log in again to play online.");
            return;
        }
        manualDisconnect = false;
        if (!PlayerPrefs.HasKey("playerIdBase"))
        {
            PlayerPrefs.SetString("playerIdBase", Guid.NewGuid().ToString());
            PlayerPrefs.Save();
        }
        playerId = PlayerPrefs.GetString("playerIdBase");
        StartCoroutine(ConnectCoroutine());
    }

    private System.Collections.IEnumerator ConnectCoroutine()
    {
        CloseSocket();
        serverUrl = ServerConfig.GetBaseUrl(serverUrl);
        socketCancellation = new CancellationTokenSource();
        socket = new ClientWebSocket();
        var baseUri = new Uri(serverUrl);
        var builder = new UriBuilder(baseUri)
        {
            Scheme = baseUri.Scheme == "https" ? "wss" : "ws",
            Path = baseUri.AbsolutePath.TrimEnd('/') + "/socket.io/",
            Query = "EIO=4&transport=websocket"
        };
        Debug.Log($"[Socket.IO] Connecting to {serverUrl}/socket.io/");
        socketCancellation.CancelAfter(TimeSpan.FromSeconds(30));
        var connectingSocket = socket;
        var connectingCancellation = socketCancellation;
        Task connect = connectingSocket.ConnectAsync(builder.Uri, connectingCancellation.Token);
        while (!connect.IsCompleted) yield return null;
        if (socket != connectingSocket) yield break;
        if (connect.IsFaulted || connect.IsCanceled)
        {
            HandleConnectionError(connect.Exception?.GetBaseException().Message ?? "Connection timed out.");
            yield break;
        }

        socketCancellation.CancelAfter(Timeout.Infinite);
        _ = ReceiveLoopAsync(socket, socketCancellation.Token);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket connection, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        try
        {
            while (connection.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using (var message = new System.IO.MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await connection.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        message.Write(buffer, 0, result.Count);
                        if (message.Length > 1024 * 1024) throw new InvalidOperationException("Socket message too large.");
                    } while (!result.EndOfMessage);
                    if (result.MessageType != WebSocketMessageType.Text) continue;
                    string packet = Encoding.UTF8.GetString(message.ToArray());
                    if (packet.StartsWith("0", StringComparison.Ordinal))
                        await SendSocketPacketAsync(connection, "40" + JsonConvert.SerializeObject(new { token = OnlineSession.Token }), cancellationToken);
                    else if (packet.StartsWith("2", StringComparison.Ordinal))
                        await SendSocketPacketAsync(connection, "3" + packet.Substring(1), cancellationToken);
                    else if (packet.StartsWith("40", StringComparison.Ordinal))
                    {
                        JObject.Parse(packet.Substring(2));
                        EnqueueMainThread(() => {
                            if (socket != connection) return;
                            // Keep the existing stable player ID across reconnects.
                            isConnected = true;
                            reconnectScheduled = false;
                            Debug.Log("[Socket.IO] Connected and authenticated");
                            OnConnected?.Invoke();
                        });
                    }
                    else if (packet.StartsWith("42", StringComparison.Ordinal))
                    {
                        var args = JArray.Parse(packet.Substring(2));
                        string target = args[0].Value<string>();
                        string payload = args.Count < 2 ? "{}" : args[1].Type == JTokenType.String ? args[1].Value<string>() : args[1].ToString(Formatting.None);
                        EnqueueMainThread(() => { if (socket == connection) HandleHubEvent(target, payload); });
                    }
                    else if (packet.StartsWith("44", StringComparison.Ordinal))
                        throw new InvalidOperationException(JObject.Parse(packet.Substring(2)).Value<string>("message") ?? "Socket authentication failed.");
                    else if (packet == "41" || packet == "1")
                        throw new InvalidOperationException("Server disconnected the socket.");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            EnqueueMainThread(() => { if (socket == connection) HandleConnectionError(ex.GetBaseException().Message); });
        }
        finally
        {
            EnqueueMainThread(() =>
            {
                if (socket != connection) return;
                if (isConnected)
                {
                    isConnected = false;
                    if (GameManager.Instance != null && isOnlineMode) GameManager.Instance.isPausedForReconnect = true;
                    OnDisconnected?.Invoke();
                }
                if (!manualDisconnect && !applicationQuitting && !reconnectScheduled)
                {
                    reconnectScheduled = true;
                    StartCoroutine(ReconnectCoroutine());
                }
            });
        }
    }

    private System.Collections.IEnumerator ReconnectCoroutine()
    {
        for (int attempt = 0; attempt < 30 && !manualDisconnect && !applicationQuitting; attempt++)
        {
            yield return new WaitForSecondsRealtime(1f);
            if (IsConnected) yield break;
            StartCoroutine(ConnectCoroutine());
            float timeout = 5f;
            while (timeout > 0f && !IsConnected)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }
            if (IsConnected) yield break;
        }
        reconnectScheduled = false;
        if (isOnlineMode)
        {
            ResetOnlineState();
            OnReconnectFailed?.Invoke("Connection lost. Please reconnect from the lobby.");
        }
    }

    private void HandleHubEvent(string target, string payload)
    {
        switch (target)
        {
            case "roomCreated":
                currentRoomId = JsonUtility.FromJson<RoomCreatedData>(payload)?.roomId;
                OnRoomCreated?.Invoke(currentRoomId);
                break;
            case "gameStart":
                var start = JsonUtility.FromJson<GameStartData>(payload);
                if (start == null) break;
                boardReadyForSync = false;
                latestState = null;
                awaitingMove = false;
                endedMatch = null;
                pendingNetworkMoves.Clear();
                currentRoomId = start.roomId;
                opponentName = start.opponentName;
                assignedColor = start.color == "white" ? PieceColor.White : PieceColor.Black;
                isOnlineMode = true;
                isSpectator = false;
                OnGameStarted?.Invoke(start.opponentName, start.color);
                break;
            case "stateSync":
                ApplyStatePayload(payload);
                break;
            case "reconnectSuccess":
                var reconnect = JsonUtility.FromJson<ServerState>(payload);
                if (reconnect != null)
                {
                    currentRoomId = reconnect.roomId;
                    assignedColor = reconnect.color == "white" ? PieceColor.White : PieceColor.Black;
                    isOnlineMode = true;
                    latestState = reconnect;
                    stateDirty = true;
                    awaitingMove = false;
                    OnReconnectSuccess?.Invoke(payload);
                }
                break;
            case "reconnectFailed":
                bool wasPlaying = isOnlineMode;
                ResetOnlineState();
                if (wasPlaying) OnReconnectFailed?.Invoke(payload);
                break;
            case "superseded":
                manualDisconnect = true;
                CloseSocket();
                isConnected = false;
                OnDisconnected?.Invoke();
                break;
            case "clockSync":
                var clock = JsonUtility.FromJson<MoveData>(payload);
                if (isOnlineMode && clock != null && ChessClock.Instance != null)
                    ChessClock.Instance.SyncClockTimes(clock.whiteTime, clock.blackTime);
                break;
            case "moveRejected":
            case "joinError":
                awaitingMove = false;
                OnJoinError?.Invoke(ReadError(payload));
                break;
            case "roomClosed":
                ApplyServerState();
                ResetOnlineState();
                OnReconnectFailed?.Invoke(ReadError(payload));
                break;
            case "opponentDisconnected":
                OnOpponentDisconnected?.Invoke(JsonUtility.FromJson<DisconnectData>(payload)?.message);
                break;
            case "opponentReconnected":
                OnOpponentReconnected?.Invoke(payload);
                break;
            case "opponentRequestedRematch":
                OnOpponentRequestedRematch?.Invoke();
                break;
            case "rematchAgreed":
                OnRematchAgreed?.Invoke();
                break;
            case "availableRoomsList":
                OnAvailableRoomsListReceived?.Invoke(payload);
                break;
        }
    }

    private void ApplyStatePayload(string payload)
    {
        var incoming = JsonUtility.FromJson<ServerState>(payload);
        if (incoming == null || (currentRoomId != null && incoming.roomId != currentRoomId)) return;
        currentRoomId = incoming.roomId;
        if (latestState != null && incoming.matchId == latestState.matchId && incoming.revision < latestState.revision) return;
        latestState = incoming;
        stateDirty = true;
        awaitingMove = false;
    }

    private async Task SendSocketPacketAsync(ClientWebSocket connection, string packet, CancellationToken token)
    {
        await socketSendLock.WaitAsync(token);
        try
        {
            if (connection.State != WebSocketState.Open) throw new InvalidOperationException("Socket is closed.");
            await connection.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(packet)), WebSocketMessageType.Text, true, token);
        }
        finally { socketSendLock.Release(); }
    }

    private async Task EmitSocketEventAsync(string eventName, object data)
    {
        var connection = socket;
        var token = socketCancellation?.Token ?? CancellationToken.None;
        try { await SendSocketPacketAsync(connection, "42" + JsonConvert.SerializeObject(new object[] { eventName, data }), token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { EnqueueMainThread(() => { if (socket == connection) HandleConnectionError(ex.Message); }); }
    }

    private void HandleConnectionError(string error)
    {
        Debug.LogWarning($"[Socket.IO] {error}");
        isConnected = false;
        if (GameManager.Instance != null && isOnlineMode) GameManager.Instance.isPausedForReconnect = true;
        OnJoinError?.Invoke(error);
        OnDisconnected?.Invoke();
        if (!manualDisconnect && !applicationQuitting && !reconnectScheduled)
        {
            reconnectScheduled = true;
            StartCoroutine(ReconnectCoroutine());
        }
    }

    private static string ReadError(string payload)
    {
        try
        {
            var value = JsonUtility.FromJson<ErrorData>(payload);
            if (!string.IsNullOrEmpty(value?.error)) return value.error;
        }
        catch { }
        try
        {
            var value = JObject.Parse(payload);
            return value.Value<string>("error") ?? value.Value<string>("reason") ?? payload;
        }
        catch { return payload; }
    }

    public void GetAvailableRooms() { if (IsConnected) _ = EmitSocketEventAsync("getAvailableRooms", new { }); }
    public void CreateRoom(bool isPrivate = false)
    {
        if (!IsConnected) { OnJoinError?.Invoke("Not connected to server."); return; }
        _ = EmitSocketEventAsync("createRoom", new { isPrivate });
    }
    public void JoinRoom(string roomId)
    {
        if (!IsConnected) { OnJoinError?.Invoke("Not connected to server."); return; }
        _ = EmitSocketEventAsync("joinRoom", new { roomId = roomId.Trim().ToUpperInvariant() });
    }
    public void RequestRematch() { if (IsConnected) _ = EmitSocketEventAsync("requestRematch", new { }); }
    public void SendPlayerReadyForRematch() { if (IsConnected) _ = EmitSocketEventAsync("playerReadyForRematch", new { }); }
    public void SendMove(string move)
    {
        if (!isOnlineMode || !IsConnected || !CanSubmitMove) return;
        awaitingMove = true;
        moveSentAt = Time.realtimeSinceStartup;
        _ = EmitSocketEventAsync("move", new NetworkMoveData { move = move, matchId = latestState.matchId, revision = latestState.revision });
    }
    public void SendGameOver(string winner, string reason) { }
    public void Resign() { if (isOnlineMode && IsConnected) _ = EmitSocketEventAsync("resign", new { }); }
    public void LeaveRoom()
    {
        if (IsConnected && !string.IsNullOrEmpty(currentRoomId)) _ = EmitSocketEventAsync("leaveRoom", new { });
        ResetOnlineState();
    }
    private void RequestState() { if (IsConnected) _ = EmitSocketEventAsync("requestState", new { }); }

    public void Disconnect()
    {
        manualDisconnect = true;
        CloseSocket();
        isConnected = false;
        isOnlineMode = false;
        currentRoomId = null;
        opponentName = null;
    }

    private void CloseSocket()
    {
        if (socketCancellation != null)
        {
            socketCancellation.Cancel();
            socketCancellation.Dispose();
            socketCancellation = null;
        }
        if (socket != null)
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                    _ = socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closed", CancellationToken.None);
                socket.Dispose();
            }
            catch { }
            socket = null;
        }
    }

    public void ResetOnlineState()
    {
        latestState = null;
        stateDirty = false;
        boardReadyForSync = false;
        awaitingMove = false;
        endedMatch = null;
        pendingNetworkMoves.Clear();
        isOnlineMode = false;
        currentRoomId = null;
        opponentName = null;
        isSpectator = false;
    }

    public bool IsConnected => isConnected && socket != null && socket.State == WebSocketState.Open;
}
