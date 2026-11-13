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
    public const string DEFAULT_SERVER_URL = "https://unity-chess-server-dotnet.onrender.com";

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
                                 latestState != null && latestState.status == "playing";

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
            catch (Exception ex) { Debug.LogError($"[SignalR] Could not apply remote move: {ex}"); }
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
            Path = baseUri.AbsolutePath.TrimEnd('/') + "/hubs/game",
            Query = "access_token=" + Uri.EscapeDataString(OnlineSession.Token)
        };
        Task connect = socket.ConnectAsync(builder.Uri, socketCancellation.Token);
        while (!connect.IsCompleted) yield return null;
        if (connect.IsFaulted)
        {
            HandleConnectionError(connect.Exception?.GetBaseException().Message ?? "Could not connect to server.");
            yield break;
        }

        string handshake = JsonConvert.SerializeObject(new { protocol = "json", version = 1 }) + "\u001e";
        Task send = socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(handshake)),
            WebSocketMessageType.Text, true, socketCancellation.Token);
        while (!send.IsCompleted) yield return null;
        if (send.IsFaulted)
        {
            HandleConnectionError(send.Exception?.GetBaseException().Message ?? "SignalR handshake failed.");
            yield break;
        }

        _ = ReceiveLoopAsync(socket, socketCancellation.Token);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket connection, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var receiveBuffer = new StringBuilder();
        var handshakeReceived = false;
        try
        {
            while (connection.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await connection.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close) break;
                receiveBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                var text = receiveBuffer.ToString();
                var start = 0;
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] != '\u001e') continue;
                    string record = text.Substring(start, i - start);
                    start = i + 1;
                    if (!handshakeReceived)
                    {
                        handshakeReceived = true;
                        if (!string.IsNullOrWhiteSpace(record) && record != "{}")
                            throw new InvalidOperationException(record);
                        EnqueueMainThread(() =>
                        {
                            isConnected = true;
                            reconnectScheduled = false;
                            OnConnected?.Invoke();
                        });
                    }
                    else if (!string.IsNullOrWhiteSpace(record))
                        await ProcessHubRecordAsync(connection, record, cancellationToken);
                }
                receiveBuffer.Clear();
                if (start < text.Length) receiveBuffer.Append(text.Substring(start));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            EnqueueMainThread(() => HandleConnectionError(ex.GetBaseException().Message));
        }
        finally
        {
            EnqueueMainThread(() =>
            {
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

    private async Task ProcessHubRecordAsync(ClientWebSocket connection, string record, CancellationToken cancellationToken)
    {
        var message = JObject.Parse(record);
        int type = message.Value<int?>("type") ?? 0;
        if (type == 6)
        {
            await SendHubMessageAsync(connection, new { type = 6 }, cancellationToken);
            return;
        }
        if (type == 7) throw new InvalidOperationException(message.Value<string>("error") ?? "SignalR connection closed.");
        if (type != 1) return;
        string target = message.Value<string>("target");
        var args = message["arguments"] as JArray;
        if (string.IsNullOrEmpty(target) || args == null || args.Count == 0) return;
        string payload = args[0].ToString(Formatting.None);
        EnqueueMainThread(() => HandleHubEvent(target, payload));
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

    private async Task SendHubMessageAsync(ClientWebSocket connection, object message, CancellationToken token)
    {
        if (connection == null || connection.State != WebSocketState.Open) return;
        string payload = JsonConvert.SerializeObject(message) + "\u001e";
        await connection.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload)), WebSocketMessageType.Text, true, token);
    }

    private void HandleConnectionError(string error)
    {
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

    public void GetAvailableRooms()
    {
        if (IsConnected)
            StartCoroutine(SendRest("GET", "/api/rooms", null, response => OnAvailableRoomsListReceived?.Invoke(response)));
    }

    public void CreateRoom(bool isPrivate = false)
    {
        if (!IsConnected) { OnJoinError?.Invoke("Not connected to server."); return; }
        StartCoroutine(SendRest("POST", "/api/rooms", JsonConvert.SerializeObject(new { isPrivate }), null));
    }

    public void JoinRoom(string roomId)
    {
        if (!IsConnected) { OnJoinError?.Invoke("Not connected to server."); return; }
        StartCoroutine(SendRest("POST", "/api/rooms/" + Uri.EscapeDataString(roomId.Trim().ToUpperInvariant()) + "/join", "{}", null));
    }

    public void RequestRematch()
    {
        if (IsConnected && !string.IsNullOrEmpty(currentRoomId))
            StartCoroutine(SendRest("POST", RoomPath("rematch"), "{}", null));
    }

    public void SendPlayerReadyForRematch()
    {
        if (IsConnected && !string.IsNullOrEmpty(currentRoomId))
            StartCoroutine(SendRest("POST", RoomPath("ready"), "{}", null));
    }

    public void SendMove(string move)
    {
        if (!isOnlineMode || !IsConnected || !CanSubmitMove) return;
        var request = new NetworkMoveData
        {
            move = move,
            matchId = latestState.matchId,
            revision = latestState.revision
        };
        awaitingMove = true;
        moveSentAt = Time.realtimeSinceStartup;
        StartCoroutine(SendRest("POST", RoomPath("moves"), JsonConvert.SerializeObject(request),
            response => ApplyStatePayload(response)));
    }

    public void SendGameOver(string winner, string reason) { }

    public void Resign()
    {
        if (isOnlineMode && IsConnected && !string.IsNullOrEmpty(currentRoomId))
            StartCoroutine(SendRest("POST", RoomPath("resign"), "{}", response => ApplyStatePayload(response)));
    }

    public void LeaveRoom()
    {
        if (IsConnected && !string.IsNullOrEmpty(currentRoomId))
            StartCoroutine(SendRest("POST", RoomPath("leave"), "{}", null));
        ResetOnlineState();
    }

    private void RequestState()
    {
        if (IsConnected && !string.IsNullOrEmpty(currentRoomId))
            StartCoroutine(SendRest("GET", "/api/rooms/" + Uri.EscapeDataString(currentRoomId),
                null, response => ApplyStatePayload(response)));
    }

    private string RoomPath(string action) =>
        "/api/rooms/" + Uri.EscapeDataString(currentRoomId) + "/" + action;

    private System.Collections.IEnumerator SendRest(string method, string path, string body, Action<string> success)
    {
        var request = new UnityWebRequest(ServerConfig.GetBaseUrl(serverUrl) + path, method)
        {
            downloadHandler = new DownloadHandlerBuffer()
        };
        if (body != null) request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        request.SetRequestHeader("Authorization", "Bearer " + OnlineSession.Token);
        if (body != null) request.SetRequestHeader("Content-Type", "application/json");
        yield return request.SendWebRequest();
        if (request.result == UnityWebRequest.Result.Success)
            success?.Invoke(request.downloadHandler.text);
        else
        {
            string message = ReadError(request.downloadHandler?.text ?? request.error);
            awaitingMove = false;
            OnJoinError?.Invoke(message);
            if (method == "POST" && path.EndsWith("/moves", StringComparison.Ordinal)) RequestState();
        }
        request.Dispose();
    }

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
