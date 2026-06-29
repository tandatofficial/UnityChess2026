using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Online Lobby UI — Manages sảnh chờ trực tuyến.
/// Thay vì tự tạo giao diện bằng code, phiên bản này cho phép gán trực tiếp các Panel, Button, InputField 
/// trong Unity Editor để lập trình viên dễ dàng tùy chỉnh bố cục bằng tay.
/// </summary>
public class OnlineLobbyUI : MonoBehaviour
{
    private static OnlineLobbyUI _instance;
    public static OnlineLobbyUI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = UnityEngine.Object.FindFirstObjectByType<OnlineLobbyUI>();
            }
            return _instance;
        }
        set
        {
            _instance = value;
        }
    }

    [Header("Lobby State")]
    public bool isLobbyActive = false;

    [Header("UI Panels (Gán từ Unity Editor)")]
    [Tooltip("Panel toàn màn hình hiển thị lựa chọn (CREATE ROOM / JOIN ROOM / BACK)")]
    public GameObject selectionPanel;

    [Tooltip("Panel toàn màn hình hiển thị nhập mã room và danh sách các public room")]
    public GameObject joinPanel;

    [Tooltip("Panel toàn màn hình hiển thị mã code của phòng trong lúc chờ đối thủ")]
    public GameObject waitingPanel;

    [Header("UI Controls (Gán từ Unity Editor)")]
    public TMP_InputField roomIdInput;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI roomCodeDisplay;
    public Button createRoomButton;
    public Button joinRoomButton;
    public Button backButton;
    public Button cancelWaitButton;
    public Button refreshRoomsButton;

    [Header("Room List Settings")]
    [Tooltip("Vùng chứa (Content) của ScrollRect để sinh các dòng phòng chờ")]
    public RectTransform roomListContent;
    [Tooltip("Prefab cho từng dòng phòng (Tùy chọn, nếu để trống sẽ tự tạo dòng mặc định bằng code)")]
    public GameObject roomItemPrefab;

    // Font reference để dự phòng khi tự sinh text
    private TMP_FontAsset cachedFont;

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            transform.SetParent(null); // Đảm bảo nằm ở root để DontDestroyOnLoad hoạt động tốt
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        EnsureSocketManager();
        SubscribeEvents();
        SetupButtonListeners();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        UnsubscribeEvents();
    }

    // ------------------------------------------------
    // Public API / Navigation
    // ------------------------------------------------

    public void ShowLobby()
    {
        isLobbyActive = true;

        EnsureSocketManager();
        SubscribeEvents();

        // Ẩn hiện các Panel theo đúng trạng thái sảnh chờ chính
        if (selectionPanel != null) selectionPanel.SetActive(true);
        if (joinPanel != null) joinPanel.SetActive(false);
        if (waitingPanel != null) waitingPanel.SetActive(false);

        SetupButtonListeners();

        // Tự động kết nối tới Server
        string playerName = "Player";
        if (AccountManager.Instance != null && AccountManager.Instance.IsLoggedIn)
        {
            playerName = AccountManager.Instance.CurrentAccount.Username;
        }
        else
        {
            playerName = PlayerPrefs.GetString("loggedInUser", "Player_" + Random.Range(1000, 9999));
        }

        if (SocketManager.Instance != null)
        {
            if (!SocketManager.Instance.IsConnected)
            {
                SetInteractable(false);
                if (statusText != null) statusText.text = "Connecting to server...";
                SocketManager.Instance.ConnectToServer(playerName);
            }
            else
            {
                SetInteractable(true);
                if (statusText != null) statusText.text = "<color=#55FF55>Connected!</color>";
            }
        }
    }

    public void HideLobby()
    {
        isLobbyActive = false;

        if (selectionPanel != null) selectionPanel.SetActive(false);
        if (joinPanel != null) joinPanel.SetActive(false);
        if (waitingPanel != null) waitingPanel.SetActive(false);
    }

    public void ShowJoinScreen()
    {
        if (selectionPanel != null) selectionPanel.SetActive(false);
        if (joinPanel != null) joinPanel.SetActive(true);
        if (waitingPanel != null) waitingPanel.SetActive(false);

        if (SocketManager.Instance != null && SocketManager.Instance.IsConnected)
        {
            SocketManager.Instance.GetAvailableRooms();
        }
    }

    public void ShowSelectionScreen()
    {
        if (selectionPanel != null) selectionPanel.SetActive(true);
        if (joinPanel != null) joinPanel.SetActive(false);
        if (waitingPanel != null) waitingPanel.SetActive(false);
    }

    private void SetInteractable(bool state)
    {
        if (createRoomButton != null) createRoomButton.interactable = state;
        if (joinRoomButton != null) joinRoomButton.interactable = state;
        if (roomIdInput != null) roomIdInput.interactable = state;
    }

    private void SetupButtonListeners()
    {
        // Gán sự kiện cho các nút bấm bằng code (để phòng hờ lập trình viên quên kéo thả OnClick trong Unity Editor)
        if (createRoomButton != null)
        {
            createRoomButton.onClick.RemoveListener(OnCreateRoomClicked);
            createRoomButton.onClick.AddListener(OnCreateRoomClicked);
        }
        if (joinRoomButton != null)
        {
            joinRoomButton.onClick.RemoveListener(ShowJoinScreen);
            joinRoomButton.onClick.AddListener(ShowJoinScreen);
        }
        if (backButton != null)
        {
            backButton.onClick.RemoveListener(OnBackClicked);
            backButton.onClick.AddListener(OnBackClicked);
        }
        if (cancelWaitButton != null)
        {
            cancelWaitButton.onClick.RemoveListener(OnCancelWaitClicked);
            cancelWaitButton.onClick.AddListener(OnCancelWaitClicked);
        }
        if (refreshRoomsButton != null)
        {
            refreshRoomsButton.onClick.RemoveListener(OnRefreshRoomsClicked);
            refreshRoomsButton.onClick.AddListener(OnRefreshRoomsClicked);
        }
    }

    private void EnsureSocketManager()
    {
        if (SocketManager.Instance == null)
        {
            GameObject smGo = new GameObject("SocketManager");
            smGo.AddComponent<SocketManager>();
        }
    }

    private bool HasBoardPieces()
    {
        if (GameManager.Instance == null || GameManager.Instance.pieceBoard == null)
            return false;

        foreach (ChessPiece piece in GameManager.Instance.pieceBoard)
        {
            if (piece != null) return true;
        }
        return false;
    }

    // ------------------------------------------------
    // Event Subscriptions
    // ------------------------------------------------

    private void SubscribeEvents()
    {
        if (SocketManager.Instance == null) return;

        UnsubscribeEvents();

        SocketManager.Instance.OnRoomCreated += HandleRoomCreated;
        SocketManager.Instance.OnGameStarted += HandleGameStarted;
        SocketManager.Instance.OnJoinError += HandleJoinError;
        SocketManager.Instance.OnGameOver += HandleGameOver;
        SocketManager.Instance.OnOpponentDisconnected += HandleOpponentDisconnected;
        SocketManager.Instance.OnOpponentReconnected += HandleOpponentReconnected;
        SocketManager.Instance.OnReconnectSuccess += HandleReconnectSuccess;
        SocketManager.Instance.OnReconnectFailed += HandleReconnectFailed;
        SocketManager.Instance.OnConnected += HandleConnected;
        SocketManager.Instance.OnDisconnected += HandleDisconnected;
        SocketManager.Instance.OnAvailableRoomsListReceived += HandleAvailableRoomsListReceived;
    }

    private void UnsubscribeEvents()
    {
        if (SocketManager.Instance == null) return;

        SocketManager.Instance.OnRoomCreated -= HandleRoomCreated;
        SocketManager.Instance.OnGameStarted -= HandleGameStarted;
        SocketManager.Instance.OnJoinError -= HandleJoinError;
        SocketManager.Instance.OnGameOver -= HandleGameOver;
        SocketManager.Instance.OnOpponentDisconnected -= HandleOpponentDisconnected;
        SocketManager.Instance.OnOpponentReconnected -= HandleOpponentReconnected;
        SocketManager.Instance.OnReconnectSuccess -= HandleReconnectSuccess;
        SocketManager.Instance.OnReconnectFailed -= HandleReconnectFailed;
        SocketManager.Instance.OnConnected -= HandleConnected;
        SocketManager.Instance.OnDisconnected -= HandleDisconnected;
        SocketManager.Instance.OnAvailableRoomsListReceived -= HandleAvailableRoomsListReceived;
    }

    // ------------------------------------------------
    // Event Handlers từ SocketManager
    // ------------------------------------------------

    private void HandleConnected()
    {
        SetInteractable(true);
        if (statusText != null)
        {
            statusText.text = "<color=#55FF55>Connected to server!</color>";
        }
        if (SocketManager.Instance != null)
        {
            SocketManager.Instance.GetAvailableRooms();
        }
    }

    private void HandleDisconnected()
    {
        SetInteractable(false);
        if (statusText != null)
        {
            statusText.text = "<color=#FF6B6B>Cannot connect to server. Run: cd chess-server && npm start</color>";
        }
    }

    private void HandleRoomCreated(string roomId)
    {
        Debug.Log($"[OnlineLobbyUI] Room created: {roomId}");

        if (selectionPanel != null) selectionPanel.SetActive(false);
        if (joinPanel != null) joinPanel.SetActive(false);
        
        ShowWaitingScreen(roomId);
    }

    private void ShowWaitingScreen(string roomId)
    {
        if (waitingPanel != null) waitingPanel.SetActive(true);
        if (roomCodeDisplay != null) roomCodeDisplay.text = roomId;
    }

    private void HandleGameStarted(string opponentName, string color)
    {
        Debug.Log($"[OnlineLobbyUI] Game started! Opponent: {opponentName}, Color: {color}");

        if (GameManager.Instance != null)
        {
            GameManager.Instance.currentGameMode = GameMode.Online;
            GameManager.Instance.currentTurn = PieceColor.White;
            GameManager.Instance.UpdatePieceColliders();
        }

        if (CameraSwitcher.Instance != null && SocketManager.Instance != null)
        {
            CameraSwitcher.Instance.SetCameraFor(SocketManager.Instance.assignedColor);
        }

        HideLobby();
        StartCoroutine(StartOnlineGame(null));
    }

    [System.Serializable]
    private class ReconnectData
    {
        public string roomId;
        public string color;
        public string boardState;
        public string currentTurn;
    }

    private void HandleReconnectSuccess(string json)
    {
        Debug.Log($"[OnlineLobbyUI] Reconnect Success!");
        var data = JsonUtility.FromJson<ReconnectData>(json);

        if (SocketManager.Instance != null)
        {
            SocketManager.Instance.currentRoomId = data.roomId;
            SocketManager.Instance.assignedColor = (data.color == "white") ? PieceColor.White : PieceColor.Black;
            SocketManager.Instance.isOnlineMode = true;
        }

        HideLobby();

        StartCoroutine(StartOnlineGame(() => {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.LoadBoardState(data.boardState);
                GameManager.Instance.isPausedForReconnect = false;
            }
        }));
    }

    private void HandleReconnectFailed(string json)
    {
        Debug.Log($"[OnlineLobbyUI] Reconnect Failed.");
        ShowLobby();
        ShowToastMessage("Reconnect failed or match expired.");
    }

    private void HandleOpponentReconnected(string json)
    {
        Debug.Log($"[OnlineLobbyUI] Opponent Reconnected.");
        if (GameManager.Instance != null)
        {
            GameManager.Instance.isPausedForReconnect = false;
        }
        ShowToastMessage("Opponent reconnected!");
    }

    private void HandleOpponentDisconnected(string message)
    {
        Debug.Log($"[OnlineLobbyUI] Opponent Disconnected: {message}");
        if (GameManager.Instance != null)
        {
            GameManager.Instance.isPausedForReconnect = true;
        }
        ShowToastMessage("Opponent disconnected. Waiting 30s...");
    }

    private void ShowToastMessage(string msg)
    {
        Debug.Log(msg);
    }

    private IEnumerator StartOnlineGame(System.Action onSceneLoaded)
    {
        bool loadedScene = SceneManager.GetActiveScene().name != "PvPOff";

        if (loadedScene)
            SceneManager.LoadScene("PvPOff");

        float timeout = 5f;
        while (timeout > 0f)
        {
            GameManager gm = GameManager.Get();
            if (gm != null && (!loadedScene || HasBoardPieces()))
                break;

            yield return null;
            timeout -= Time.deltaTime;
        }

        GameManager gameManager = GameManager.Get();
        if (gameManager == null)
        {
            Debug.LogError("[OnlineLobbyUI] GameManager not found — cannot start online game.");
            yield break;
        }

        if (loadedScene)
        {
            gameManager.currentGameMode = GameMode.Online;
            gameManager.currentTurn = PieceColor.White;
            gameManager.UpdatePieceColliders();
        }
        else
        {
            gameManager.PrepareForOnlineGame();
        }

        if (CameraSwitcher.Instance != null && SocketManager.Instance != null)
        {
            CameraSwitcher.Instance.SetCameraFor(SocketManager.Instance.assignedColor);
        }

        GameMenu gameMenu = FindObjectOfType<GameMenu>();
        if (gameMenu != null)
        {
            if (gameMenu.titleMenuPanel != null) gameMenu.titleMenuPanel.SetActive(false);
            if (gameMenu.menuPanel != null) gameMenu.menuPanel.SetActive(false);
            if (gameMenu.logoImage != null) gameMenu.logoImage.SetActive(false);
            if (gameMenu.backButtonLayer != null) gameMenu.backButtonLayer.SetActive(false);
            if (gameMenu.settingsPanel != null) gameMenu.settingsPanel.SetActive(false);
            if (gameMenu.logoutButton != null) gameMenu.logoutButton.SetActive(true);

            if (gameMenu.pauseButton != null) gameMenu.pauseButton.SetActive(true);
            if (gameMenu.pauseMenuManager != null) gameMenu.pauseMenuManager.SetPauseInputEnabled(true);

            if (gameMenu.easyModeOnlyUI != null)
            {
                foreach (GameObject uiElement in gameMenu.easyModeOnlyUI)
                {
                    if (uiElement != null) uiElement.SetActive(false);
                }
            }
        }

        if (onSceneLoaded != null)
        {
            onSceneLoaded.Invoke();
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.UpdatePieceColliders();
        }
    }

    private void HandleJoinError(string error)
    {
        Debug.LogWarning($"[OnlineLobbyUI] Join error: {error}");

        if (statusText != null)
        {
            statusText.text = $"<color=#FF6B6B>{error}</color>";
        }
    }

    private void HandleGameOver(string winner, string reason)
    {
        Debug.Log($"[OnlineLobbyUI] Game Over. Winner: {winner}, Reason: {reason}");

        if (GameManager.Instance != null)
        {
            GameManager.Instance.HandleNetworkGameOver(winner, reason);
        }
    }

    [System.Serializable]
    private class AvailableRoomData
    {
        public string roomId;
        public string creatorName;
    }

    [System.Serializable]
    private class AvailableRoomsListWrapper
    {
        public System.Collections.Generic.List<AvailableRoomData> rooms;
    }

    private void HandleAvailableRoomsListReceived(string json)
    {
        Debug.Log($"[OnlineLobbyUI] Received available rooms: {json}");
        try
        {
            AvailableRoomsListWrapper data = JsonUtility.FromJson<AvailableRoomsListWrapper>(json);
            UpdateRoomListUI(data.rooms);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineLobbyUI] Error parsing available rooms list: {ex}");
        }
    }

    private void UpdateRoomListUI(System.Collections.Generic.List<AvailableRoomData> roomsList)
    {
        if (roomListContent == null) return;

        // Xóa danh sách phòng cũ
        foreach (Transform child in roomListContent)
        {
            Destroy(child.gameObject);
        }

        TMP_FontAsset font = GetFont();

        if (roomsList == null || roomsList.Count == 0)
        {
            GameObject emptyLabelGo = new GameObject("EmptyLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            emptyLabelGo.transform.SetParent(roomListContent, false);
            
            TextMeshProUGUI tmp = emptyLabelGo.GetComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.text = "No active rooms found.\nCreate a room to start!";
            tmp.fontSize = 14;
            tmp.fontStyle = FontStyles.Italic;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.6f, 0.6f, 0.6f, 0.7f);
            return;
        }

        foreach (var room in roomsList)
        {
            if (roomItemPrefab != null)
            {
                // Dùng Prefab nếu lập trình viên gán sẵn trong Inspector
                GameObject item = Instantiate(roomItemPrefab, roomListContent);
                item.SetActive(true);

                TextMeshProUGUI labelTmp = item.GetComponentInChildren<TextMeshProUGUI>();
                if (labelTmp != null)
                {
                    labelTmp.text = $"{room.creatorName} ({room.roomId})";
                }

                Button joinBtn = item.GetComponentInChildren<Button>();
                if (joinBtn != null)
                {
                    string targetRoomId = room.roomId;
                    joinBtn.onClick.RemoveAllListeners();
                    joinBtn.onClick.AddListener(() => {
                        if (SocketManager.Instance != null && SocketManager.Instance.IsConnected)
                        {
                            if (statusText != null) statusText.text = $"Joining room {targetRoomId}...";
                            SocketManager.Instance.JoinRoom(targetRoomId);
                        }
                    });
                }
            }
            else
            {
                // Dự phòng: Tự tạo dòng phòng bằng code nếu không gán Prefab
                GameObject item = new GameObject("RoomItem_" + room.roomId, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(HorizontalLayoutGroup));
                item.transform.SetParent(roomListContent, false);

                RectTransform itemRt = item.GetComponent<RectTransform>();
                itemRt.sizeDelta = new Vector2(340, 50);

                Image itemBg = item.GetComponent<Image>();
                itemBg.color = new Color(0.18f, 0.2f, 0.28f, 1f);

                HorizontalLayoutGroup hlg = item.GetComponent<HorizontalLayoutGroup>();
                hlg.padding = new RectOffset(15, 15, 5, 5);
                hlg.spacing = 10f;
                hlg.childAlignment = TextAnchor.MiddleCenter;
                hlg.childControlHeight = true;
                hlg.childControlWidth = true;
                hlg.childForceExpandHeight = false;
                hlg.childForceExpandWidth = false;

                GameObject labelGo = new GameObject("DetailsText", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
                labelGo.transform.SetParent(item.transform, false);
                
                LayoutElement textLe = labelGo.GetComponent<LayoutElement>();
                textLe.flexibleWidth = 1f;

                TextMeshProUGUI labelTmp = labelGo.GetComponent<TextMeshProUGUI>();
                labelTmp.font = font;
                labelTmp.text = $"{room.creatorName} ({room.roomId})";
                labelTmp.fontSize = 14;
                labelTmp.fontStyle = FontStyles.Bold;
                labelTmp.alignment = TextAlignmentOptions.Left;
                labelTmp.color = Color.white;
                labelTmp.verticalAlignment = VerticalAlignmentOptions.Middle;

                Button joinBtn = CreateButton(item.transform, "JOIN", font, Vector2.zero, new Vector2(80, 36), new Color(0.2f, 0.45f, 0.75f, 1f));
                LayoutElement btnLe = joinBtn.gameObject.AddComponent<LayoutElement>();
                btnLe.preferredWidth = 80f;
                btnLe.preferredHeight = 36f;

                TextMeshProUGUI btnTxt = joinBtn.GetComponentInChildren<TextMeshProUGUI>();
                if (btnTxt != null) btnTxt.fontSize = 12;

                string targetRoomId = room.roomId;
                joinBtn.onClick.AddListener(() => {
                    if (SocketManager.Instance != null && SocketManager.Instance.IsConnected)
                    {
                        if (statusText != null) statusText.text = $"Joining room {targetRoomId}...";
                        SocketManager.Instance.JoinRoom(targetRoomId);
                    }
                });
            }
        }
    }

    // ------------------------------------------------
    // Button Handlers / Public Methods (Gán sự kiện từ Inspector)
    // ------------------------------------------------

    public void OnCreateRoomClicked()
    {
        if (SocketManager.Instance == null || !SocketManager.Instance.IsConnected)
        {
            if (statusText != null)
                statusText.text = "<color=#FF6B6B>Not connected to server!</color>";
            return;
        }

        if (statusText != null)
            statusText.text = "Creating room...";

        SocketManager.Instance.CreateRoom();
    }

    public void OnJoinRoomClicked()
    {
        if (SocketManager.Instance == null || !SocketManager.Instance.IsConnected)
        {
            if (statusText != null)
                statusText.text = "<color=#FF6B6B>Not connected to server!</color>";
            return;
        }

        string roomId = roomIdInput != null ? roomIdInput.text.Trim().ToUpper() : "";
        if (string.IsNullOrEmpty(roomId))
        {
            if (statusText != null)
                statusText.text = "<color=#FF6B6B>Please enter a Room Code!</color>";
            return;
        }

        if (statusText != null)
            statusText.text = $"Joining room {roomId}...";

        SocketManager.Instance.JoinRoom(roomId);
    }

    public void OnBackClicked()
    {
        if (SocketManager.Instance != null)
        {
            SocketManager.Instance.Disconnect();
        }

        HideLobby();

        GameMenu gameMenu = FindObjectOfType<GameMenu>();
        if (gameMenu != null)
        {
            gameMenu.BackToMainMenuFromDifficultyMenu();

            if (gameMenu.titleMenuPanel != null) gameMenu.titleMenuPanel.SetActive(true);
            if (gameMenu.logoImage != null) gameMenu.logoImage.SetActive(true);
        }
    }

    public void OnCancelWaitClicked()
    {
        if (SocketManager.Instance != null)
        {
            SocketManager.Instance.LeaveRoom();
        }

        if (waitingPanel != null)
        {
            waitingPanel.SetActive(false);
        }

        ShowSelectionScreen();
    }

    public void OnRefreshRoomsClicked()
    {
        if (SocketManager.Instance != null && SocketManager.Instance.IsConnected)
        {
            SocketManager.Instance.GetAvailableRooms();
        }
    }

    // ------------------------------------------------
    // UI Factory Helpers (Phục vụ phần sinh phòng công khai)
    // ------------------------------------------------

    private TMP_FontAsset GetFont()
    {
        if (cachedFont != null) return cachedFont;

        TextMeshProUGUI[] tmps = FindObjectsOfType<TextMeshProUGUI>(true);
        foreach (var tmp in tmps)
        {
            if (tmp != null && tmp.font != null)
            {
                cachedFont = tmp.font;
                return cachedFont;
            }
        }

        cachedFont = TMP_Settings.defaultFontAsset;
        return cachedFont;
    }

    private Button CreateButton(Transform parent, string label, TMP_FontAsset font,
                                 Vector2 position, Vector2 size, Color bgColor)
    {
        GameObject go = new GameObject("Btn_" + label.Replace(" ", ""), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = position;

        Image img = go.GetComponent<Image>();
        img.color = bgColor;
        img.raycastTarget = true; // Rất quan trọng để Button có thể click được!

        Button btn = go.GetComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.highlightedColor = new Color(bgColor.r + 0.1f, bgColor.g + 0.1f, bgColor.b + 0.1f, 1f);
        colors.pressedColor = new Color(bgColor.r - 0.1f, bgColor.g - 0.1f, bgColor.b - 0.1f, 1f);
        btn.colors = colors;

        GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);

        RectTransform textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.sizeDelta = Vector2.zero;
        textRt.anchoredPosition = Vector2.zero;

        TextMeshProUGUI textTmp = textGo.GetComponent<TextMeshProUGUI>();
        textTmp.font = font;
        textTmp.text = label;
        textTmp.fontSize = 18;
        textTmp.fontStyle = FontStyles.Bold;
        textTmp.alignment = TextAlignmentOptions.Center;
        textTmp.color = Color.white;
        textTmp.raycastTarget = false;

        return btn;
    }
}
