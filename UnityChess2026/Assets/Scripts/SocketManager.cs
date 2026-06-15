using SocketIOClient;
using SocketIOClient.Newtonsoft.Json;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static SocketIOUnity;

public class SocketManager : MonoBehaviour
{
    private static SocketManager _instance;
    public static SocketManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = UnityEngine.Object.FindFirstObjectByType<SocketManager>();
            }
            return _instance;
        }
        set
        {
            _instance = value;
        }
    }
    private SocketIOUnity socket;

    [Header("Network State")]
    public PieceColor assignedColor;
    public bool isSpectator = false;
    public bool isOnlineMode = false;
    public string currentRoomId;
    public string opponentName;

    [Header("Connection Settings")]
    public string serverUrl = "http://localhost:11100";

    [Header("UI References (Optional - Legacy)")]
    public TMP_InputField usernameInput;
    public TextMeshProUGUI playerNameText;
    public Button joinButton;

    private string playerName;
    private bool isConnected = false;

    // --- Events for UI ---
    public event Action<string> OnRoomCreated;          // roomId
    public event Action<string, string> OnGameStarted;  // opponentName, assignedColor
    public event Action<string> OnJoinError;            // errorMessage
    public event Action<string> OnMoveReceived;        // move
    public event Action<string, string> OnGameOver;     // winner, reason
    public event Action<string> OnOpponentDisconnected; // message
    public event Action<string> OnOpponentReconnected;
    public event Action<string> OnReconnectSuccess;
    public event Action<string> OnReconnectFailed;
    public event Action OnConnected;
    public event Action OnDisconnected;

    private bool applicationQuitting = false;
    public string playerId { get; private set; }

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            transform.SetParent(null); // Ensure at root for DontDestroyOnLoad
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    private void OnApplicationQuit()
    {
        applicationQuitting = true;
        Disconnect();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        if (applicationQuitting)
        {
            Disconnect();
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> mainThreadActions = new();
    private readonly Queue<string> pendingNetworkMoves = new Queue<string>();

    private bool IsBoardReady()
    {
        if (GameManager.Instance == null) return false;
        if (GameManager.Instance.currentGameMode != GameMode.Online) return false;
        if (GameManager.Instance.pieceBoard == null) return false;
        
        // Check if there is at least one piece instantiated to confirm initialization
        foreach (var piece in GameManager.Instance.pieceBoard)
        {
            if (piece != null) return true;
        }
        return false;
    }

    private void Update()
    {
        while (mainThreadActions.TryDequeue(out Action action))
        {
            action?.Invoke();
        }

        if (pendingNetworkMoves.Count > 0 && IsBoardReady())
        {
            string move = pendingNetworkMoves.Dequeue();
            try
            {
                Debug.Log($"[SocketManager] Dequeued move: {move}. Applying...");
                GameManager.Instance.ApplyMoveFromNetwork(move);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SocketManager] Exception applying dequeued move: {ex}");
            }
        }
    }

    private void EnqueueMainThread(Action action)
    {
        mainThreadActions.Enqueue(action);
    }

    /// <summary>
    /// Connect to the server with the given player name.
    /// Must be called before CreateRoom/JoinRoom.
    /// </summary>
    public void ConnectToServer(string name)
    {
        if (isConnected && socket != null && socket.Connected)
        {
            Debug.Log("🔗 Already connected to server.");
            return;
        }

        // Clean up old socket if exists
        if (socket != null)
        {
            try { socket.Dispose(); } catch { }
            socket = null;
        }

        playerName = name;

        if (string.IsNullOrWhiteSpace(playerName))
        {
            playerName = "Player_" + UnityEngine.Random.Range(1000, 9999);
        }

        if (!PlayerPrefs.HasKey("playerIdBase"))
        {
            PlayerPrefs.SetString("playerIdBase", System.Guid.NewGuid().ToString());
            PlayerPrefs.Save();
        }
        string sessionId = System.Guid.NewGuid().ToString().Substring(0, 8);
        playerId = PlayerPrefs.GetString("playerIdBase") + "_" + Application.dataPath.GetHashCode() + "_" + sessionId;

        var uri = new Uri(serverUrl);
        socket = new SocketIOUnity(uri, new SocketIOOptions
        {
            Query = new Dictionary<string, string> {
                { "token", "UNITY" },
                { "name", playerName },
                { "playerId", playerId }
            },
            Transport = SocketIOClient.Transport.TransportProtocol.WebSocket,
            Reconnection = true,
            ReconnectionAttempts = 5,
            ReconnectionDelay = 1000,
        });

        socket.JsonSerializer = new NewtonsoftJsonSerializer();
        // Do NOT use UnityThreadScope.Update because it creates a hidden GameObject that dies on scene load
        // socket.unityThreadScope = UnityThreadScope.Update;

        RegisterSocketEvents();

        Debug.Log($"🔗 Connecting to server as '{playerName}'...");
        socket.Connect();
    }

    private void RegisterSocketEvents()
    {
        // --- ROOM CREATED ---
        socket.On("roomCreated", (res) =>
        {
            EnqueueMainThread(() =>
            {
                string json = res.GetValue<string>();
                var data = JsonUtility.FromJson<RoomCreatedData>(json);
                currentRoomId = data.roomId;

                Debug.Log($"🏠 Room created: {currentRoomId}");
                OnRoomCreated?.Invoke(currentRoomId);
            });
        });

        // --- JOIN ERROR ---
        socket.On("joinError", (res) =>
        {
            EnqueueMainThread(() =>
            {
                string json = res.GetValue<string>();
                var data = JsonUtility.FromJson<ErrorData>(json);

                Debug.LogWarning($"❌ Join error: {data.error}");
                OnJoinError?.Invoke(data.error);
            });
        });

        // --- GAME START ---
        socket.On("gameStart", (res) =>
        {
            EnqueueMainThread(() =>
            {
                string json = res.GetValue<string>();
                var data = JsonUtility.FromJson<GameStartData>(json);

                currentRoomId = data.roomId;
                opponentName = data.opponentName;
                assignedColor = (data.color == "white") ? PieceColor.White : PieceColor.Black;
                isOnlineMode = true;
                isSpectator = false;

                Debug.Log($"🎮 Game started! Room: {currentRoomId}, Color: {data.color}, Opponent: {opponentName}");
                OnGameStarted?.Invoke(data.opponentName, data.color);
            });
        });

        // --- MOVE RECEIVED ---
        socket.On("move", (res) =>
        {
            EnqueueMainThread(() =>
            {
                try 
                {
                    string json = "";
                    try {
                        json = res.GetValue<string>();
                    } catch {
                        json = res.GetValue<object>().ToString();
                    }
                    Debug.Log($"[SocketManager] Raw move received: {json}");
                    
                    var data = JsonUtility.FromJson<MoveData>(json);

                    if (data == null || string.IsNullOrEmpty(data.move))
                    {
                        // Fallback: maybe the server sent {"move":"1,5->3,5"} directly as an object, and ToString() gave JSON
                        // Or maybe it's just the string itself "1,5->3,5"
                        if (json.Contains("->") && !json.Contains("{")) {
                            data = new MoveData { move = json };
                        } else {
                            Debug.LogError($"[SocketManager] Could not parse move from json: {json}");
                            return;
                        }
                    }

                    Debug.Log($"♟️ Move received from opponent: {data.move}");
                    OnMoveReceived?.Invoke(data.move);

                    if (IsBoardReady())
                    {
                        Debug.Log("[SocketManager] Board is ready, applying move directly.");
                        GameManager.Instance.ApplyMoveFromNetwork(data.move);
                    }
                    else
                    {
                        Debug.Log("[SocketManager] Board not ready, enqueuing move.");
                        pendingNetworkMoves.Enqueue(data.move);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SocketManager] Error processing move event: {ex}");
                }
            });
        });

        // --- GAME OVER ---
        socket.On("gameOver", (res) =>
        {
            EnqueueMainThread(() =>
            {
                string json = res.GetValue<string>();
                var data = JsonUtility.FromJson<GameOverData>(json);

                Debug.Log($"🏁 Game over: {data.reason} | Winner: {data.winner}");
                OnGameOver?.Invoke(data.winner, data.reason);
            });
        });

        // --- OPPONENT DISCONNECTED ---
        socket.On("opponentDisconnected", (res) =>
        {
            EnqueueMainThread(() =>
            {
                string json = res.GetValue<string>();
                var data = JsonUtility.FromJson<DisconnectData>(json);

                Debug.LogWarning($"⚠️ {data.message}");
                OnOpponentDisconnected?.Invoke(data.message);
            });
        });

        // --- CONNECTION EVENTS ---
        socket.OnConnected += (sender, e) =>
        {
            isConnected = true;
            Debug.Log("✅ Connected to server.");
            EnqueueMainThread(() => { OnConnected?.Invoke(); });
        };

        socket.On("connect_error", (res) =>
        {
            EnqueueMainThread(() =>
            {
                isConnected = false;
                string msg = "Could not connect to server. Is chess-server running on port 11100?";
                try { msg = res.GetValue<string>(); } catch { }
                Debug.LogError($"❌ Connection error: {msg}");
                OnDisconnected?.Invoke();
            });
        });

        // --- DISCONNECT / RECONNECT ---
        socket.OnDisconnected += (sender, e) =>
        {
            EnqueueMainThread(() =>
            {
                isConnected = false;
                Debug.LogWarning($"❌ Disconnected from server. Reason: {e}");
                OnDisconnected?.Invoke();
            });
        };

        socket.OnReconnected += (sender, e) =>
        {
            EnqueueMainThread(() =>
            {
                Debug.Log($"🔄 Socket.IO Reconnected (attempt {e}). Waiting for server sync...");
            });
        };

        socket.On("reconnectSuccess", (res) =>
        {
            EnqueueMainThread(() =>
            {
                string json = res.GetValue<string>();
                Debug.Log($"🔄 Reconnect Success: {json}");
                OnReconnectSuccess?.Invoke(json);
            });
        });

        socket.On("reconnectFailed", (res) =>
        {
            EnqueueMainThread(() =>
            {
                string json = res.GetValue<string>();
                Debug.LogWarning($"❌ Reconnect Failed: {json}");
                OnReconnectFailed?.Invoke(json);
            });
        });

        socket.On("opponentReconnected", (res) =>
        {
            EnqueueMainThread(() =>
            {
                string json = res.GetValue<string>();
                Debug.Log($"🔄 Opponent Reconnected: {json}");
                OnOpponentReconnected?.Invoke(json);
            });
        });
    }

    // ------------------------------------------------
    // Public API
    // ------------------------------------------------

    /// <summary>
    /// Request the server to create a new room.
    /// The result will come back via OnRoomCreated event.
    /// </summary>
    public void CreateRoom()
    {
        if (socket == null || !socket.Connected)
        {
            Debug.LogWarning("⚠️ Not connected to server!");
            return;
        }

        socket.Emit("createRoom", "{}");
        Debug.Log("📤 Requesting room creation...");
    }

    /// <summary>
    /// Join an existing room by ID.
    /// The result will come back via OnGameStarted or OnJoinError event.
    /// </summary>
    public void JoinRoom(string roomId)
    {
        if (socket == null || !socket.Connected)
        {
            Debug.LogWarning("⚠️ Not connected to server!");
            return;
        }

        string json = JsonUtility.ToJson(new JoinRoomData { roomId = roomId.ToUpper().Trim() });
        socket.Emit("joinRoom", json);
        Debug.Log($"📤 Requesting to join room: {roomId}");
    }

    /// <summary>
    /// Send a chess move to the server.
    /// Format: "fromRow,fromCol->toRow,toCol"
    /// </summary>
    public void SendMove(string move)
    {
        if (!isOnlineMode) return;

        if (socket == null || !socket.Connected)
        {
            Debug.LogWarning("⚠️ Cannot send move: not connected.");
            return;
        }

        // move is already a JSON string created by GameManager
        socket.Emit("move", move);
        Debug.Log($"📤 Move sent: {move}");
    }

    /// <summary>
    /// Notify the server that the game has ended (checkmate, stalemate, etc.)
    /// </summary>
    public void SendGameOver(string winner, string reason)
    {
        if (!isOnlineMode) return;

        if (socket == null || !socket.Connected) return;

        string json = JsonUtility.ToJson(new GameOverData { winner = winner, reason = reason });
        socket.Emit("gameOver", json);
        Debug.Log($"📤 Game over sent: {reason}");
    }

    /// <summary>
    /// Resign from the current game.
    /// </summary>
    public void Resign()
    {
        if (!isOnlineMode) return;

        if (socket == null || !socket.Connected) return;

        socket.Emit("resign", "{}");
        Debug.Log("🏳️ Resign sent.");
    }

    /// <summary>
    /// Leave the current room without disconnecting from the server.
    /// Useful when canceling matchmaking.
    /// </summary>
    public void LeaveRoom()
    {
        if (socket == null || !socket.Connected) return;

        socket.Emit("leaveRoom", "{}");
        Debug.Log("🚪 Leaving room...");
        ResetOnlineState();
    }

    /// <summary>
    /// Disconnect from the server cleanly.
    /// </summary>
    public void Disconnect()
    {
        if (socket != null)
        {
            if (socket.Connected) socket.Disconnect();
            try { socket.Dispose(); } catch { }
            socket = null;
        }
        isConnected = false;
        isOnlineMode = false;
        currentRoomId = null;
        opponentName = null;
    }

    /// <summary>
    /// Reset online state (when returning to menu).
    /// </summary>
    public void ResetOnlineState()
    {
        isOnlineMode = false;
        currentRoomId = null;
        opponentName = null;
        isSpectator = false;
    }

    public bool IsConnected => isConnected && socket != null && socket.Connected;

    // ------------------------------------------------
    // JSON Data Classes
    // ------------------------------------------------
    [Serializable] private class RoomCreatedData { public string roomId; }
    [Serializable] private class JoinRoomData { public string roomId; }
    [Serializable] private class ErrorData { public string error; }
    [Serializable] private class GameStartData { public string roomId; public string color; public string opponentName; }
    [Serializable] private class MoveData { public string move; }
    [Serializable] private class GameOverData { public string winner; public string reason; }
    [Serializable] private class DisconnectData { public string message; }
}
