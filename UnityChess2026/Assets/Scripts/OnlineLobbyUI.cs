using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;

/// <summary>
/// Online Lobby UI — 2 luồng chính: CREATE và JOIN
/// CREATE → CreatePanel (public/private)
/// JOIN   → JoinPanel (danh sách phòng + Find Private Room)
/// </summary>
public class OnlineLobbyUI : MonoBehaviour
{
    private static OnlineLobbyUI _instance;
    public static OnlineLobbyUI Instance
    {
        get
        {
            if (_instance == null)
                _instance = UnityEngine.Object.FindAnyObjectByType<OnlineLobbyUI>();
            return _instance;
        }
        set { _instance = value; }
    }

    [Header("Lobby State")]
    public bool isLobbyActive = false;

    // -------------------------------------------------------
    // Panels
    // -------------------------------------------------------
    [Header("UI Panels")]
    [Tooltip("Panel chính: CREATE + JOIN + BACK")]
    public GameObject selectionPanel;

    [Tooltip("Panel CREATE: Create Public + Create Private + Back")]
    public GameObject createPanel;

    [Tooltip("Panel JOIN: Danh sách phòng + Find Private Room + Back")]
    public GameObject joinPublicPanel;

    [Tooltip("Panel nhập mã phòng private")]
    public GameObject joinPrivatePanel;

    [Tooltip("Panel chờ đối thủ")]
    public GameObject waitingPanel;

    // -------------------------------------------------------
    // UI Controls
    // -------------------------------------------------------
    [Header("UI Controls")]
    public TMP_InputField roomIdInput;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI roomCodeDisplay;
    [Tooltip("Container ẩn/hiện Room Code (chỉ Private)")]
    public GameObject roomCodeSection;

    // -------------------------------------------------------
    // SelectionPanel Buttons
    // -------------------------------------------------------
    [Header("Selection Panel Buttons")]
    public Button btnCreate;        // → ShowCreatePanel
    public Button btnJoin;          // → ShowJoinPublicPanel
    public Button btnBackToMenu;    // → Back to main menu

    // -------------------------------------------------------
    // CreatePanel Buttons
    // -------------------------------------------------------
    [Header("Create Panel Buttons")]
    public Button btnCreatePublic;   // → CreateRoom(false)
    public Button btnCreatePrivate;  // → CreateRoom(true)
    public Button btnBackFromCreate; // → ShowSelectionPanel

    // -------------------------------------------------------
    // JoinPublicPanel Buttons
    // -------------------------------------------------------
    [Header("Join Public Panel Buttons")]
    public Button btnRefreshRooms;
    public Button btnFindPrivateRoom; // → ShowJoinPrivatePanel
    public Button btnBackFromJoin;    // → ShowSelectionPanel
    public RectTransform roomListContent;
    public GameObject roomItemPrefab;

    // -------------------------------------------------------
    // JoinPrivatePanel Buttons
    // -------------------------------------------------------
    [Header("Join Private Panel Buttons")]
    public Button btnSubmitPrivateJoin;
    public Button btnBackFromJoinPrivate; // → ShowJoinPublicPanel

    // -------------------------------------------------------
    // WaitingPanel Buttons
    // -------------------------------------------------------
    [Header("Waiting Panel Buttons")]
    public Button btnCancelWait;

    // -------------------------------------------------------
    // Camera
    // -------------------------------------------------------
    [Header("Camera")]
    [Tooltip("VCam_Menu — priority 20 khi lobby mở, 0 khi đóng")]
    public CinemachineCamera lobbyVCam;

    // -------------------------------------------------------
    // Private State
    // -------------------------------------------------------
    private TMP_FontAsset cachedFont;
    private bool _currentRoomIsPrivate = false;

    // -------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------
    private void Awake()
    {
        if (_instance == null) _instance = this;
        else if (_instance != this) { Destroy(gameObject); return; }
    }

    private void Start()
    {
        EnsureSocketManager();
        SubscribeEvents();
        SetupButtonListeners();
        HideLobby();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        UnsubscribeEvents();
    }

    // -------------------------------------------------------
    // Public API
    // -------------------------------------------------------

    public void ShowLobby()
    {
        isLobbyActive = true;
        EnsureSocketManager();
        SubscribeEvents();
        SetupButtonListeners();

        ShowSelectionPanel();

        // Kích hoạt camera menu (close-up board)
        if (lobbyVCam != null) lobbyVCam.Priority = 20;

        // Kết nối server
        string playerName = "Player";
        if (AccountManager.Instance != null && AccountManager.Instance.IsLoggedIn)
            playerName = AccountManager.Instance.CurrentAccount.Username;
        else
            playerName = PlayerPrefs.GetString("loggedInUser", "Player_" + Random.Range(1000, 9999));

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
        if (createPanel != null) createPanel.SetActive(false);
        if (joinPublicPanel != null) joinPublicPanel.SetActive(false);
        if (joinPrivatePanel != null) joinPrivatePanel.SetActive(false);
        if (waitingPanel != null) waitingPanel.SetActive(false);

        // Giải phóng camera menu
        if (lobbyVCam != null) lobbyVCam.Priority = 0;
    }

    // -------------------------------------------------------
    // Navigation
    // -------------------------------------------------------

    public void ShowSelectionPanel()
    {
        SetPanel(selectionPanel);
    }

    public void ShowCreatePanel()
    {
        SetPanel(createPanel);
    }

    public void ShowJoinPublicPanel()
    {
        SetPanel(joinPublicPanel);
        if (SocketManager.Instance != null && SocketManager.Instance.IsConnected)
            SocketManager.Instance.GetAvailableRooms();
    }

    public void ShowJoinPrivatePanel()
    {
        SetPanel(joinPrivatePanel);
        if (roomIdInput != null) roomIdInput.text = "";
    }

    private void SetPanel(GameObject targetPanel)
    {
        if (selectionPanel != null) selectionPanel.SetActive(selectionPanel == targetPanel);
        if (createPanel != null) createPanel.SetActive(createPanel == targetPanel);
        if (joinPublicPanel != null) joinPublicPanel.SetActive(joinPublicPanel == targetPanel);
        if (joinPrivatePanel != null) joinPrivatePanel.SetActive(joinPrivatePanel == targetPanel);
        if (waitingPanel != null) waitingPanel.SetActive(waitingPanel == targetPanel);
    }

    // -------------------------------------------------------
    // Interactability
    // -------------------------------------------------------

    private void SetInteractable(bool state)
    {
        if (btnCreatePublic != null) btnCreatePublic.interactable = state;
        if (btnCreatePrivate != null) btnCreatePrivate.interactable = state;
        if (btnJoin != null) btnJoin.interactable = state;
        if (btnFindPrivateRoom != null) btnFindPrivateRoom.interactable = state;
        if (roomIdInput != null) roomIdInput.interactable = state;
    }

    // -------------------------------------------------------
    // Button Listener Setup
    // -------------------------------------------------------

    private void SetupButtonListeners()
    {
        AddListener(btnCreate, ShowCreatePanel);
        AddListener(btnJoin, ShowJoinPublicPanel);
        AddListener(btnBackToMenu, OnBackToMenuClicked);
        AddListener(btnCreatePublic, OnCreatePublicClicked);
        AddListener(btnCreatePrivate, OnCreatePrivateClicked);
        AddListener(btnBackFromCreate, ShowSelectionPanel);
        AddListener(btnRefreshRooms, OnRefreshRoomsClicked);
        AddListener(btnFindPrivateRoom, ShowJoinPrivatePanel);
        AddListener(btnBackFromJoin, ShowSelectionPanel);
        AddListener(btnSubmitPrivateJoin, OnSubmitPrivateJoinClicked);
        AddListener(btnBackFromJoinPrivate, ShowJoinPublicPanel);
        AddListener(btnCancelWait, OnCancelWaitClicked);
    }

    private void AddListener(Button btn, UnityEngine.Events.UnityAction action)
    {
        if (btn == null) return;
        btn.onClick.RemoveListener(action);
        btn.onClick.AddListener(action);
    }

    // -------------------------------------------------------
    // Socket Events
    // -------------------------------------------------------

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

    // -------------------------------------------------------
    // Event Handlers
    // -------------------------------------------------------

    private void HandleConnected()
    {
        SetInteractable(true);
        if (statusText != null) statusText.text = "<color=#55FF55>Connected to server!</color>";
        if (SocketManager.Instance != null) SocketManager.Instance.GetAvailableRooms();
    }

    private void HandleDisconnected()
    {
        SetInteractable(false);
        if (statusText != null)
        {
            statusText.text = "<color=#FF6B6B>Cannot connect to server. Please check server status or connection.</color>";
        }
    }

    private void HandleRoomCreated(string roomId)
    {
        Debug.Log($"[OnlineLobbyUI] Room created: {roomId}");
        SetPanel(waitingPanel);

        // Chỉ hiện mã phòng với Private room
        bool showCode = _currentRoomIsPrivate;
        if (roomCodeSection != null) roomCodeSection.SetActive(showCode);
        if (roomCodeDisplay != null) roomCodeDisplay.text = showCode ? roomId : "";
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
            CameraSwitcher.Instance.SetCameraFor(SocketManager.Instance.assignedColor);

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
        public float whiteTime;
        public float blackTime;
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
        StartCoroutine(StartOnlineGame(() =>
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.LoadBoardState(data.boardState);
                if (!string.IsNullOrEmpty(data.currentTurn))
                {
                    GameManager.Instance.currentTurn = (data.currentTurn == "black") ? PieceColor.Black : PieceColor.White;
                    GameManager.Instance.UpdatePieceColliders();
                }
                GameManager.Instance.isPausedForReconnect = false;
            }
            if (ChessClock.Instance != null && (data.whiteTime > 0f || data.blackTime > 0f))
            {
                ChessClock.Instance.SyncClockTimes(data.whiteTime, data.blackTime);
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
        if (GameManager.Instance != null) GameManager.Instance.isPausedForReconnect = false;
        ShowToastMessage("Opponent reconnected!");
    }

    private void HandleOpponentDisconnected(string message)
    {
        Debug.Log($"[OnlineLobbyUI] Opponent Disconnected: {message}");
        if (GameManager.Instance != null) GameManager.Instance.isPausedForReconnect = true;
        ShowToastMessage("Opponent disconnected. Waiting 30s...");
    }

    private void ShowToastMessage(string msg) => Debug.Log(msg);

    private void HandleJoinError(string error)
    {
        Debug.LogWarning($"[OnlineLobbyUI] Join error: {error}");
        if (statusText != null) statusText.text = $"<color=#FF6B6B>{error}</color>";
    }

    private void HandleGameOver(string winner, string reason)
    {
        Debug.Log($"[OnlineLobbyUI] Game Over. Winner: {winner}, Reason: {reason}");
        if (GameManager.Instance != null) GameManager.Instance.HandleNetworkGameOver(winner, reason);
    }

    [System.Serializable] private class AvailableRoomData { public string roomId; public string creatorName; }
    [System.Serializable] private class AvailableRoomsListWrapper { public List<AvailableRoomData> rooms; }

    private void HandleAvailableRoomsListReceived(string json)
    {
        Debug.Log($"[OnlineLobbyUI] Received available rooms: {json}");
        try
        {
            var data = JsonUtility.FromJson<AvailableRoomsListWrapper>(json);
            UpdateRoomListUI(data.rooms);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineLobbyUI] Error parsing rooms: {ex}");
        }
    }

    private void UpdateRoomListUI(List<AvailableRoomData> roomsList)
    {
        if (roomListContent == null) return;

        foreach (Transform child in roomListContent) Destroy(child.gameObject);

        TMP_FontAsset font = GetFont();

        if (roomsList == null || roomsList.Count == 0)
        {
            var emptyGo = new GameObject("EmptyLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            emptyGo.transform.SetParent(roomListContent, false);
            var tmp = emptyGo.GetComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.text = "No public rooms.\nCreate one to get started!";
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
                var item = Instantiate(roomItemPrefab, roomListContent);
                item.SetActive(true);
                var labelTmp = item.GetComponentInChildren<TextMeshProUGUI>();
                if (labelTmp != null) labelTmp.text = $"{room.creatorName}";
                var joinBtn = item.GetComponentInChildren<Button>();
                if (joinBtn != null)
                {
                    string id = room.roomId;
                    joinBtn.onClick.RemoveAllListeners();
                    joinBtn.onClick.AddListener(() =>
                    {
                        if (SocketManager.Instance != null && SocketManager.Instance.IsConnected)
                        {
                            if (statusText != null) statusText.text = $"Joining room {id}...";
                            SocketManager.Instance.JoinRoom(id);
                        }
                    });
                }
            }
            else
            {
                // Fallback: tạo item bằng code
                var item = new GameObject("Room_" + room.roomId, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(HorizontalLayoutGroup));
                item.transform.SetParent(roomListContent, false);
                var itemRt = item.GetComponent<RectTransform>(); itemRt.sizeDelta = new Vector2(340, 50);
                var itemBg = item.GetComponent<Image>(); itemBg.color = new Color(0.098f, 0.094f, 0.090f, 0.902f);
                var hlg = item.GetComponent<HorizontalLayoutGroup>();
                hlg.padding = new RectOffset(15, 15, 5, 5); hlg.spacing = 10f;
                hlg.childAlignment = TextAnchor.MiddleCenter;
                hlg.childControlHeight = true; hlg.childControlWidth = true;

                var labelGo = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
                labelGo.transform.SetParent(item.transform, false);
                labelGo.GetComponent<LayoutElement>().flexibleWidth = 1f;
                var labelTmp = labelGo.GetComponent<TextMeshProUGUI>();
                labelTmp.font = font; labelTmp.text = room.creatorName; labelTmp.fontSize = 14;
                labelTmp.fontStyle = FontStyles.Bold; labelTmp.alignment = TextAlignmentOptions.Left;
                labelTmp.color = Color.white; labelTmp.verticalAlignment = VerticalAlignmentOptions.Middle;

                var joinBtn = CreateButton(item.transform, "JOIN", font, Vector2.zero, new Vector2(80, 36), new Color(0.2f, 0.45f, 0.75f, 1f));
                var le = joinBtn.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = 80f; le.preferredHeight = 36f;
                var btnTxt = joinBtn.GetComponentInChildren<TextMeshProUGUI>(); if (btnTxt != null) btnTxt.fontSize = 12;

                string targetId = room.roomId;
                joinBtn.onClick.AddListener(() =>
                {
                    if (SocketManager.Instance != null && SocketManager.Instance.IsConnected)
                    {
                        if (statusText != null) statusText.text = $"Joining {targetId}...";
                        SocketManager.Instance.JoinRoom(targetId);
                    }
                });
            }
        }
    }

    // -------------------------------------------------------
    // Button Click Handlers
    // -------------------------------------------------------

    private void OnCreatePublicClicked() => CreateRoom(false);
    private void OnCreatePrivateClicked() => CreateRoom(true);

    private void CreateRoom(bool isPrivate)
    {
        if (SocketManager.Instance == null || !SocketManager.Instance.IsConnected)
        {
            if (statusText != null) statusText.text = "<color=#FF6B6B>Not connected to server!</color>";
            return;
        }
        if (statusText != null) statusText.text = isPrivate ? "Creating private room..." : "Creating public room...";
        _currentRoomIsPrivate = isPrivate;
        SocketManager.Instance.CreateRoom(isPrivate);
    }

    private void OnSubmitPrivateJoinClicked()
    {
        if (SocketManager.Instance == null || !SocketManager.Instance.IsConnected)
        {
            if (statusText != null) statusText.text = "<color=#FF6B6B>Not connected!</color>";
            return;
        }
        string roomId = roomIdInput != null ? roomIdInput.text.Trim().ToUpper() : "";
        if (string.IsNullOrEmpty(roomId))
        {
            if (statusText != null) statusText.text = "<color=#FF6B6B>Please enter a Room Code!</color>";
            return;
        }
        if (statusText != null) statusText.text = $"Joining room {roomId}...";
        SocketManager.Instance.JoinRoom(roomId);
    }

    private void OnBackToMenuClicked()
    {
        if (SocketManager.Instance != null) SocketManager.Instance.Disconnect();
        HideLobby();
        GameMenu gameMenu = FindObjectOfType<GameMenu>();
        if (gameMenu != null)
        {
            gameMenu.BackToMainMenuFromDifficultyMenu();
            if (gameMenu.titleMenuPanel != null) gameMenu.titleMenuPanel.SetActive(true);
            if (gameMenu.logoImage != null) gameMenu.logoImage.SetActive(true);
        }
    }

    private void OnCancelWaitClicked()
    {
        if (SocketManager.Instance != null) SocketManager.Instance.LeaveRoom();
        if (waitingPanel != null) waitingPanel.SetActive(false);
        ShowSelectionPanel();
    }

    private void OnRefreshRoomsClicked()
    {
        if (SocketManager.Instance != null && SocketManager.Instance.IsConnected)
            SocketManager.Instance.GetAvailableRooms();
    }

    // -------------------------------------------------------
    // Online Game Startup
    // -------------------------------------------------------

    private bool HasBoardPieces()
    {
        if (GameManager.Instance == null || GameManager.Instance.pieceBoard == null) return false;
        foreach (ChessPiece piece in GameManager.Instance.pieceBoard) if (piece != null) return true;
        return false;
    }

    private IEnumerator StartOnlineGame(System.Action onSceneLoaded)
    {
        bool loadedScene = SceneManager.GetActiveScene().name != "PvPOff";
        if (loadedScene) SceneManager.LoadScene("PvPOff");

        float timeout = 5f;
        while (timeout > 0f)
        {
            GameManager gm = GameManager.Get();
            if (gm != null && (!loadedScene || HasBoardPieces())) break;
            yield return null;
            timeout -= Time.deltaTime;
        }

        GameManager gameManager = GameManager.Get();
        if (gameManager == null) { Debug.LogError("[OnlineLobbyUI] GameManager not found!"); yield break; }

        if (loadedScene) { gameManager.currentGameMode = GameMode.Online; gameManager.currentTurn = PieceColor.White; gameManager.UpdatePieceColliders(); }
        else gameManager.PrepareForOnlineGame();

        if (CameraSwitcher.Instance != null && SocketManager.Instance != null)
            CameraSwitcher.Instance.SetCameraFor(SocketManager.Instance.assignedColor);

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
                foreach (GameObject uiElement in gameMenu.easyModeOnlyUI)
                    if (uiElement != null) uiElement.SetActive(false);
        }

        onSceneLoaded?.Invoke();
        if (GameManager.Instance != null) GameManager.Instance.UpdatePieceColliders();
    }

    // -------------------------------------------------------
    // Utilities
    // -------------------------------------------------------

    private void EnsureSocketManager()
    {
        if (SocketManager.Instance == null)
        {
            var smGo = new GameObject("SocketManager");
            smGo.AddComponent<SocketManager>();
        }
    }

    private TMP_FontAsset GetFont()
    {
        if (cachedFont != null) return cachedFont;
        foreach (var tmp in FindObjectsOfType<TextMeshProUGUI>(true))
            if (tmp != null && tmp.font != null) { cachedFont = tmp.font; return cachedFont; }
        cachedFont = TMP_Settings.defaultFontAsset;
        return cachedFont;
    }

    private Button CreateButton(Transform parent, string label, TMP_FontAsset font, Vector2 position, Vector2 size, Color bgColor)
    {
        var go = new GameObject("Btn_" + label.Replace(" ", ""), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size; rt.anchoredPosition = position;
        var img = go.GetComponent<Image>(); img.color = bgColor; img.raycastTarget = true;
        var btn = go.GetComponent<Button>();
        var c = btn.colors; c.highlightedColor = new Color(bgColor.r + 0.1f, bgColor.g + 0.1f, bgColor.b + 0.1f, 1f);
        c.pressedColor = new Color(bgColor.r - 0.1f, bgColor.g - 0.1f, bgColor.b - 0.1f, 1f); btn.colors = c;
        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);
        var textRt = textGo.GetComponent<RectTransform>(); textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one; textRt.sizeDelta = Vector2.zero;
        var textTmp = textGo.GetComponent<TextMeshProUGUI>();
        textTmp.font = font; textTmp.text = label; textTmp.fontSize = 18; textTmp.fontStyle = FontStyles.Bold;
        textTmp.alignment = TextAlignmentOptions.Center; textTmp.color = Color.white; textTmp.raycastTarget = false;
        return btn;
    }
}
