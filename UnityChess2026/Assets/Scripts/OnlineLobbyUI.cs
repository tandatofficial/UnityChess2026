using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

/// <summary>
/// Online Lobby UI — Manages room creation, joining, and waiting for opponent.
/// This script creates its own UI programmatically and hooks into SocketManager events.
/// Attach to an empty GameObject in the PvPOff scene, or it will be spawned dynamically by GameMenu.
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

    // UI Root
    private GameObject lobbyPanel;
    private GameObject waitingPanel;

    // Lobby Controls
    private TMP_InputField roomIdInput;
    private TextMeshProUGUI statusText;
    private TextMeshProUGUI roomCodeDisplay;
    private Button createRoomButton;
    private Button joinRoomButton;
    private Button backButton;
    private Button cancelWaitButton;

    // Font reference
    private TMP_FontAsset cachedFont;

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

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        UnsubscribeEvents();
    }

    // ------------------------------------------------
    // Public API
    // ------------------------------------------------

    public void ShowLobby()
    {
        isLobbyActive = true;

        // --- CLEANUP PHANTOM PANELS ---
        // Ensure no leftover panels from previous scene loads exist
        if (lobbyPanel != null) Destroy(lobbyPanel);
        if (waitingPanel != null) Destroy(waitingPanel);
        
        GameObject[] oldLobbies = GameObject.FindGameObjectsWithTag("Untagged");
        foreach (var go in oldLobbies)
        {
            if (go.name == "OnlineLobbyPanel" || go.name == "WaitingPanel")
            {
                Destroy(go);
            }
        }

        lobbyPanel = null;
        waitingPanel = null;

        // Ensure connected to server
        string playerName = "Player";
        if (AccountManager.Instance != null && AccountManager.Instance.IsLoggedIn)
        {
            playerName = AccountManager.Instance.CurrentAccount.Username;
        }
        else
        {
            playerName = PlayerPrefs.GetString("loggedInUser", "Player_" + Random.Range(1000, 9999));
        }

        EnsureSocketManager();
        SubscribeEvents();
        CreateLobbyUI();

        if (lobbyPanel != null) lobbyPanel.SetActive(true);
        if (waitingPanel != null) waitingPanel.SetActive(false);

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

    private void SetInteractable(bool state)
    {
        if (createRoomButton != null) createRoomButton.interactable = state;
        if (joinRoomButton != null) joinRoomButton.interactable = state;
        if (roomIdInput != null) roomIdInput.interactable = state;
    }

    /// <summary>
    /// Hide the lobby and clean up UI.
    /// </summary>
    public void HideLobby()
    {
        isLobbyActive = false;

        if (lobbyPanel != null) Destroy(lobbyPanel);
        if (waitingPanel != null) Destroy(waitingPanel);

        lobbyPanel = null;
        waitingPanel = null;
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
    }

    // ------------------------------------------------
    // Event Handlers
    // ------------------------------------------------

    private void HandleConnected()
    {
        SetInteractable(true);
        if (statusText != null)
        {
            statusText.text = "<color=#55FF55>Connected to server!</color>";
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

        // Switch to waiting screen
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        ShowWaitingScreen(roomId);
    }

    private void HandleGameStarted(string opponentName, string color)
    {
        Debug.Log($"[OnlineLobbyUI] Game started! Opponent: {opponentName}, Color: {color}");

        // Set GameManager to Online mode
        if (GameManager.Instance != null)
        {
            GameManager.Instance.currentGameMode = GameMode.Online;
            GameManager.Instance.currentTurn = PieceColor.White;
            GameManager.Instance.UpdatePieceColliders();
        }

        // Set camera for assigned color
        if (CameraSwitcher.Instance != null && SocketManager.Instance != null)
        {
            CameraSwitcher.Instance.SetCameraFor(SocketManager.Instance.assignedColor);
        }

        // Close lobby UI
        HideLobby();

        // If we are on the game scene, hide game menus and start the game
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
        // Implement actual UI toast if desired
    }

    private IEnumerator StartOnlineGame(System.Action onSceneLoaded)
    {
        bool loadedScene = SceneManager.GetActiveScene().name != "PvPOff";

        // Avoid reloading PvPOff — that destroys GameManager and breaks clicks mid-transition
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

        // Hide the title menu / difficulty menu if visible
        GameMenu gameMenu = FindObjectOfType<GameMenu>();
        if (gameMenu != null)
        {
            if (gameMenu.titleMenuPanel != null) gameMenu.titleMenuPanel.SetActive(false);
            if (gameMenu.menuPanel != null) gameMenu.menuPanel.SetActive(false);
            if (gameMenu.logoImage != null) gameMenu.logoImage.SetActive(false);
            if (gameMenu.backButtonLayer != null) gameMenu.backButtonLayer.SetActive(false);
            if (gameMenu.settingsPanel != null) gameMenu.settingsPanel.SetActive(false);
            if (gameMenu.logoutButton != null) gameMenu.logoutButton.SetActive(true);

            // Show pause button
            if (gameMenu.pauseButton != null) gameMenu.pauseButton.SetActive(true);
            if (gameMenu.pauseMenuManager != null) gameMenu.pauseMenuManager.SetPauseInputEnabled(true);

            // Hide undo/hint in online mode
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

        // Ensure GameManager colliders are correct
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


    // ------------------------------------------------
    // UI Creation
    // ------------------------------------------------

    private TMP_FontAsset GetFont()
    {
        if (cachedFont != null) return cachedFont;

        // Try to find a font from existing TextMeshPro components in the scene
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

    private void CreateLobbyUI()
    {
        if (lobbyPanel != null) return; // Already created

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[OnlineLobbyUI] No Canvas found in scene!");
            return;
        }

        TMP_FontAsset font = GetFont();

        // --- OVERLAY BACKGROUND ---
        lobbyPanel = new GameObject("OnlineLobbyPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        lobbyPanel.transform.SetParent(canvas.transform, false);

        RectTransform panelRt = lobbyPanel.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.sizeDelta = Vector2.zero;
        panelRt.anchoredPosition = Vector2.zero;

        Image panelBg = lobbyPanel.GetComponent<Image>();
        panelBg.color = new Color(0.05f, 0.06f, 0.1f, 0.95f);
        panelBg.raycastTarget = true;

        lobbyPanel.transform.SetAsLastSibling();

        // --- CARD CONTAINER ---
        GameObject card = CreateCard(lobbyPanel.transform, 420f, 460f);

        float yOffset = 170f;

        // --- TITLE ---
        CreateLabel(card.transform, "♟ PLAY ONLINE", font, 28, FontStyles.Bold,
                    new Vector2(0, yOffset), Color.white);
        yOffset -= 60f;

        // --- CREATE ROOM BUTTON ---
        createRoomButton = CreateButton(card.transform, "CREATE ROOM", font,
                                         new Vector2(0, yOffset), new Vector2(280, 50),
                                         new Color(0.2f, 0.65f, 0.45f, 1f));
        createRoomButton.onClick.AddListener(OnCreateRoomClicked);
        yOffset -= 70f;

        // --- SEPARATOR ---
        CreateLabel(card.transform, "── or join a room ──", font, 14, FontStyles.Normal,
                    new Vector2(0, yOffset), new Color(0.6f, 0.6f, 0.6f, 0.8f));
        yOffset -= 50f;

        // --- ROOM ID INPUT ---
        roomIdInput = CreateInputField(card.transform, "Enter Room Code...", font,
                                        new Vector2(0, yOffset), new Vector2(280, 50));
        yOffset -= 65f;

        // --- JOIN ROOM BUTTON ---
        joinRoomButton = CreateButton(card.transform, "JOIN ROOM", font,
                                       new Vector2(0, yOffset), new Vector2(280, 50),
                                       new Color(0.25f, 0.48f, 0.85f, 1f));
        joinRoomButton.onClick.AddListener(OnJoinRoomClicked);
        yOffset -= 70f;

        // --- STATUS TEXT ---
        statusText = CreateLabel(card.transform, "", font, 14, FontStyles.Normal,
                                  new Vector2(0, yOffset), new Color(0.9f, 0.9f, 0.5f, 1f));
        yOffset -= 50f;

        // --- BACK BUTTON ---
        backButton = CreateButton(card.transform, "← BACK", font,
                                   new Vector2(0, yOffset), new Vector2(200, 45),
                                   new Color(0.35f, 0.35f, 0.4f, 1f));
        backButton.onClick.AddListener(OnBackClicked);
    }

    private void ShowWaitingScreen(string roomId)
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        TMP_FontAsset font = GetFont();

        if (waitingPanel != null) Destroy(waitingPanel);

        // --- OVERLAY ---
        waitingPanel = new GameObject("WaitingPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        waitingPanel.transform.SetParent(canvas.transform, false);

        RectTransform panelRt = waitingPanel.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.sizeDelta = Vector2.zero;

        Image bg = waitingPanel.GetComponent<Image>();
        bg.color = new Color(0.05f, 0.06f, 0.1f, 0.95f);
        bg.raycastTarget = true;

        waitingPanel.transform.SetAsLastSibling();

        // --- CARD ---
        GameObject card = CreateCard(waitingPanel.transform, 400f, 320f);

        float y = 100f;

        // --- WAITING TEXT ---
        CreateLabel(card.transform, "⏳ Waiting for opponent...", font, 22, FontStyles.Bold,
                    new Vector2(0, y), Color.white);
        y -= 60f;

        // --- ROOM CODE ---
        CreateLabel(card.transform, "Room Code:", font, 14, FontStyles.Normal,
                    new Vector2(0, y), new Color(0.7f, 0.7f, 0.7f, 1f));
        y -= 40f;

        roomCodeDisplay = CreateLabel(card.transform, roomId, font, 36, FontStyles.Bold,
                                       new Vector2(0, y), new Color(0.3f, 0.85f, 0.6f, 1f));
        y -= 35f;

        CreateLabel(card.transform, "(Share this code!)", font, 12, FontStyles.Italic,
                    new Vector2(0, y), new Color(0.6f, 0.6f, 0.6f, 0.8f));
        y -= 60f;

        // --- CANCEL BUTTON ---
        cancelWaitButton = CreateButton(card.transform, "CANCEL", font,
                                         new Vector2(0, y), new Vector2(200, 45),
                                         new Color(0.7f, 0.3f, 0.3f, 1f));
        cancelWaitButton.onClick.AddListener(OnCancelWaitClicked);
    }

    // ------------------------------------------------
    // Button Handlers
    // ------------------------------------------------

    private void OnCreateRoomClicked()
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

    private void OnJoinRoomClicked()
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

    private void OnBackClicked()
    {
        if (SocketManager.Instance != null)
        {
            SocketManager.Instance.Disconnect();
        }

        HideLobby();

        // Show the title menu again
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
        if (SocketManager.Instance != null)
        {
            SocketManager.Instance.LeaveRoom();
        }

        if (waitingPanel != null)
        {
            Destroy(waitingPanel);
            waitingPanel = null;
        }

        // Show lobby again
        if (lobbyPanel != null)
        {
            lobbyPanel.SetActive(true);
            if (statusText != null) statusText.text = "";
        }
        else
        {
            ShowLobby();
        }
    }

    // ------------------------------------------------
    // UI Factory Helpers
    // ------------------------------------------------

    private GameObject CreateCard(Transform parent, float width, float height)
    {
        GameObject card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(parent, false);

        RectTransform rt = card.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(width, height);
        rt.anchoredPosition = Vector2.zero;

        Image img = card.GetComponent<Image>();
        img.color = new Color(0.12f, 0.13f, 0.18f, 0.95f);

        // Add subtle outline
        Outline outline = card.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.1f);
        outline.effectDistance = new Vector2(2f, -2f);

        return card;
    }

    private TextMeshProUGUI CreateLabel(Transform parent, string text, TMP_FontAsset font,
                                         float fontSize, FontStyles style, Vector2 position, Color color)
    {
        GameObject go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(360, fontSize + 15);
        rt.anchoredPosition = position;

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.font = font;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;

        return tmp;
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

        Button btn = go.GetComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.highlightedColor = new Color(bgColor.r + 0.1f, bgColor.g + 0.1f, bgColor.b + 0.1f, 1f);
        colors.pressedColor = new Color(bgColor.r - 0.1f, bgColor.g - 0.1f, bgColor.b - 0.1f, 1f);
        btn.colors = colors;

        // Button text
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

        return btn;
    }

    private TMP_InputField CreateInputField(Transform parent, string placeholder, TMP_FontAsset font,
                                             Vector2 position, Vector2 size)
    {
        GameObject go = new GameObject("InputField_RoomId", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = position;

        Image img = go.GetComponent<Image>();
        img.color = new Color(0.18f, 0.19f, 0.25f, 1f);

        // Text area
        GameObject textArea = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        textArea.transform.SetParent(go.transform, false);
        RectTransform taRt = textArea.GetComponent<RectTransform>();
        taRt.anchorMin = Vector2.zero;
        taRt.anchorMax = Vector2.one;
        taRt.offsetMin = new Vector2(15, 5);
        taRt.offsetMax = new Vector2(-15, -5);

        // Main text
        GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(textArea.transform, false);
        RectTransform txtRt = textGo.GetComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.sizeDelta = Vector2.zero;
        txtRt.anchoredPosition = Vector2.zero;

        TextMeshProUGUI textComp = textGo.GetComponent<TextMeshProUGUI>();
        textComp.font = font;
        textComp.fontSize = 22;
        textComp.fontStyle = FontStyles.Bold;
        textComp.alignment = TextAlignmentOptions.Center;
        textComp.color = Color.white;

        // Placeholder text
        GameObject phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
        phGo.transform.SetParent(textArea.transform, false);
        RectTransform phRt = phGo.GetComponent<RectTransform>();
        phRt.anchorMin = Vector2.zero;
        phRt.anchorMax = Vector2.one;
        phRt.sizeDelta = Vector2.zero;
        phRt.anchoredPosition = Vector2.zero;

        TextMeshProUGUI phComp = phGo.GetComponent<TextMeshProUGUI>();
        phComp.font = font;
        phComp.text = placeholder;
        phComp.fontSize = 18;
        phComp.fontStyle = FontStyles.Italic;
        phComp.alignment = TextAlignmentOptions.Center;
        phComp.color = new Color(0.5f, 0.5f, 0.55f, 0.8f);

        // Configure InputField
        TMP_InputField inputField = go.GetComponent<TMP_InputField>();
        inputField.textComponent = textComp;
        inputField.placeholder = phComp;
        inputField.textViewport = taRt;
        inputField.characterLimit = 6;
        inputField.contentType = TMP_InputField.ContentType.Alphanumeric;
        inputField.onValueChanged.AddListener((val) => {
            inputField.text = val.ToUpper();
        });

        return inputField;
    }
}
