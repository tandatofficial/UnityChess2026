using SocketIOClient;
using SocketIOClient.Newtonsoft.Json;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SocketIOUnity;

public class SocketManager : MonoBehaviour
{
    public static SocketManager Instance;
    private SocketIOUnity socket;

    public PieceColor assignedColor;
    public bool isSpectator = false;

    [Header("UI References")]
    public TMP_InputField usernameInput;
    public TextMeshProUGUI playerNameText;
    public Button joinButton;

    private string playerName;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        joinButton.onClick.AddListener(JoinServer);
    }

    public void JoinServer()
    {
        playerName = usernameInput.text.Trim();

        if (string.IsNullOrWhiteSpace(playerName))
        {
            Debug.LogWarning("⚠️ Tên người chơi không được để trống!");
            return;
        }

        playerNameText.text = $"👤 Người chơi: {playerName}";

        var uri = new Uri("http://localhost:11100"); // Thay bằng IP nếu dùng server online
        socket = new SocketIOUnity(uri, new SocketIOOptions
        {
            Query = new Dictionary<string, string> {
                { "token", "UNITY" },
                { "name", playerName }
            },
            Transport = SocketIOClient.Transport.TransportProtocol.WebSocket
        });

        UIManager.Instance.HideUserPanel();

        socket.JsonSerializer = new NewtonsoftJsonSerializer();
        socket.unityThreadScope = UnityThreadScope.Update;

        // Nhận màu từ server
        socket.OnUnityThread("assignColor", (res) =>
        {
            string colorStr = res.GetValue<string>();
            Debug.Log($"🎨 Server assigned: {colorStr}");

            if (colorStr == "spectator")
            {
                isSpectator = true;
                Debug.LogWarning("👀 Bạn là khán giả, không thể chơi.");
                return;
            }

            assignedColor = (colorStr == "white") ? PieceColor.White : PieceColor.Black;

            CameraSwitcher.Instance.SetCameraFor(assignedColor);

            // Chỉ người trắng khởi động lượt đầu
            if (assignedColor == PieceColor.White)
                GameManager.Instance.currentTurn = PieceColor.White;

            Debug.Log($"✅ Assigned color: {assignedColor} to {playerName}");
        });

        socket.OnUnityThread("hello", (res) =>
        {
            string msg = res.GetValue<string>();
            Debug.Log("📩 Server says: " + msg);
        });

        socket.OnUnityThread("move", (res) =>
        {
            string move = res.GetValue<string>();
            Debug.Log($"♟️ Move received from opponent: {move}");

            GameManager.Instance.ApplyMoveFromNetwork(move);
        });

        socket.Connect();
        Debug.Log("🔗 Connecting to server...");
    }

    public void SendMove(string move)
    {
        if (!isSpectator && socket.Connected)
        {
            Debug.Log($"📤 Sending move: {move} (Player: {playerName})");
            socket.Emit("move", move);
        }
    }

    public void Disconnect()
    {
        if (socket != null && socket.Connected)
            socket.Disconnect();
    }
}
