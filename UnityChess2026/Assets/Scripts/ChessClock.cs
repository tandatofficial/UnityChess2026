using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class ChessClock : MonoBehaviour
{
    public static ChessClock Instance;

    public enum ClockMode
    {
        SuddenDeath,
        FischerIncrement,
        SimpleDelay
    }

    [Header("Clock Configuration")]
    public bool isClockEnabled = true;
    public ClockMode clockMode = ClockMode.SuddenDeath;
    public float baseTimeSeconds = 600f; // 10 minutes
    public float incrementSeconds = 5f;
    public bool autoInjectButton = false; // We disabled auto-injection on Main Menu as requested

    [Header("Manual UI Bindings (Optional)")]
    public Toggle manualEnabledToggle;
    public TMP_Dropdown manualModeDropdown;
    public Slider manualBaseTimeSlider;
    public TextMeshProUGUI manualBaseTimeText;
    public Slider manualIncrementSlider;
    public TextMeshProUGUI manualIncrementText;

    [Header("Runtime State")]
    public float whiteTimeRemaining;
    public float blackTimeRemaining;
    public bool isClockRunning = false;

    private PieceColor lastTurn = PieceColor.White;
    private float delayTimeRemaining = 0f;
    private float lastTickSecondWhite = -1f;
    private float lastTickSecondBlack = -1f;

    // Procedural Audio Clips
    private AudioClip tickSound;
    private AudioClip alarmSound;

    // UI Elements
    private GameObject hudContainer;
    private Image whiteBackground;
    private Image blackBackground;
    private TextMeshProUGUI whiteLabel;
    private TextMeshProUGUI blackLabel;
    private TextMeshProUGUI whiteTimeText;
    private TextMeshProUGUI blackTimeText;
    private TextMeshProUGUI whiteDelayText;
    private TextMeshProUGUI blackDelayText;

    private GameObject clockSetupButton;
    private GameObject settingsPanelInstance;
    private GameObject subPanelInstance;

    public GameObject clockSettingsPrefab;

    private T FindChildComponent<T>(Transform root, string path) where T : Component
    {
        Transform child = root.Find(path);
        if (child != null) return child.GetComponent<T>();
        Debug.LogWarning($"[ChessClock] Could not find child at path: {path}");
        return null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void OnSceneLoaded()
    {
        if (Instance == null)
        {
            GameObject clockGo = new GameObject("ChessClockManager");
            clockGo.AddComponent<ChessClock>();
            DontDestroyOnLoad(clockGo);
        }
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            CreateProceduralSounds();
            LoadSettings();
            
            // Tự động tìm nạp Prefab từ thư mục Assets/Resources/
            clockSettingsPrefab = Resources.Load<GameObject>("ClockSettingsOverlay");
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoadedEvent;
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoadedEvent;
    }

    private void OnSceneLoadedEvent(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        subPanelInstance = null;
        settingsPanelInstance = null;
        StartCoroutine(InjectClockButtonToDifficultyMenuCoroutine());
    }

    private void Start()
    {
        StartCoroutine(InjectClockButtonToDifficultyMenuCoroutine());
    }

    private IEnumerator InjectClockButtonToDifficultyMenuCoroutine()
    {
        yield return new WaitForSeconds(0.25f);

        GameMenu gameMenu = FindObjectOfType<GameMenu>();
        if (gameMenu == null || gameMenu.menuPanel == null)
        {
            yield break;
        }

        // Check if button already exists in difficulty menu
        if (gameMenu.menuPanel.transform.Find("Btn_ClockSettings") != null)
        {
            yield break;
        }

        // Find reference button to clone (like Easy/Medium/Hard button)
        Button refButton = gameMenu.menuPanel.GetComponentInChildren<Button>(true);
        if (refButton == null) yield break;

        // Clone reference button
        GameObject clockBtnGo = Instantiate(refButton.gameObject, gameMenu.menuPanel.transform);
        clockBtnGo.name = "Btn_ClockSettings";

        // Setup scale and Z position to prevent click shifting parallax bugs
        RectTransform rt = clockBtnGo.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.localScale = Vector3.one;
            rt.anchoredPosition3D = new Vector3(rt.anchoredPosition.x, rt.anchoredPosition.y, 0f);
        }

        // Strip Inspector-defined onClick listeners by resetting the onClick event
        Button btn = clockBtnGo.GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener(OpenSettingsPopup);
        }

        // Remove any custom C# scripts attached to avoid interference
        MonoBehaviour[] scripts = clockBtnGo.GetComponents<MonoBehaviour>();
        foreach (MonoBehaviour script in scripts)
        {
            if (script != null && 
                !(script is Button) && 
                !(script is Image) && 
                !(script is CanvasRenderer) && 
                !(script is RectTransform) && 
                !(script is LayoutElement))
            {
                Destroy(script);
            }
        }

        // Change Text to English
        TextMeshProUGUI tmp = clockBtnGo.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp != null)
        {
            tmp.text = "CLOCK SETTINGS";
        }
        else
        {
            Text legacyText = clockBtnGo.GetComponentInChildren<Text>(true);
            if (legacyText != null) legacyText.text = "CLOCK SETTINGS";
        }

        // Ensure button is active
        clockBtnGo.SetActive(true);

        Debug.Log("Successfully injected 'CLOCK SETTINGS' button into the Difficulty Menu panel.");
    }

    public bool FindAndInitializeCustomUI()
    {
        GameMenu gameMenu = FindObjectOfType<GameMenu>();
        if (gameMenu == null || gameMenu.settingsPanel == null) return false;

        Transform settingsTransform = gameMenu.settingsPanel.transform;
        Transform toggleT = FindChildRecursive(settingsTransform, "Clock_EnabledToggle");
        
        if (toggleT != null)
        {
            manualEnabledToggle = toggleT.GetComponent<Toggle>();
            
            Transform modeD = FindChildRecursive(settingsTransform, "Clock_ModeDropdown");
            if (modeD != null) manualModeDropdown = modeD.GetComponent<TMP_Dropdown>();
            
            Transform baseS = FindChildRecursive(settingsTransform, "Clock_BaseTimeSlider");
            if (baseS != null) manualBaseTimeSlider = baseS.GetComponent<Slider>();
            
            Transform baseTxt = FindChildRecursive(settingsTransform, "Clock_BaseTimeText");
            if (baseTxt != null) manualBaseTimeText = baseTxt.GetComponent<TextMeshProUGUI>();
            
            Transform incS = FindChildRecursive(settingsTransform, "Clock_IncrementSlider");
            if (incS != null) manualIncrementSlider = incS.GetComponent<Slider>();
            
            Transform incTxt = FindChildRecursive(settingsTransform, "Clock_IncrementText");
            if (incTxt != null) manualIncrementText = incTxt.GetComponent<TextMeshProUGUI>();

            InitializeManualUI();
            Debug.Log("Successfully found and bound hand-crafted Clock UI elements in SettingsPanel!");
            return true;
        }
        return false;
    }

    private Transform FindChildRecursive(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform found = FindChildRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private bool IsMatchActive()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "PvPOff")
            return false;

        GameManager gm = GameManager.Instance;
        GameMenu menu = UnityEngine.Object.FindFirstObjectByType<GameMenu>();
        if (gm == null || menu == null)
            return false;

        if (menu.titleMenuPanel != null && menu.titleMenuPanel.activeSelf) return false;
        if (menu.menuPanel != null && menu.menuPanel.activeSelf) return false;
        if (menu.settingsPanel != null && menu.settingsPanel.activeSelf) return false;

        if (OnlineLobbyUI.Instance != null && OnlineLobbyUI.Instance.isLobbyActive)
            return false;

        if (GameObject.Find("OnlineLobbyPanel") != null || GameObject.Find("WaitingPanel") != null)
            return false;

        // In online mode, the match is only active once GameManager is set to Online
        if (SocketManager.Instance != null && SocketManager.Instance.isOnlineMode)
        {
            if (gm.currentGameMode != GameMode.Online)
                return false;
        }

        return true;
    }

    private void Update()
    {
        bool isMatchActive = IsMatchActive();

        if (isMatchActive && isClockEnabled)
        {
            bool shouldRunClock = Time.timeScale > 0f;

            if (shouldRunClock)
            {
                if (!isClockRunning || hudContainer == null)
                {
                    StartClock();
                }
            }
            else
            {
                if (isClockRunning)
                {
                    isClockRunning = false;
                }
            }
        }
        else
        {
            if (isClockRunning)
            {
                isClockRunning = false;
            }
        }

        if (isClockRunning)
        {
            // Detect turn switch
            PieceColor currentTurn = GameManager.Instance.currentTurn;
            if (currentTurn != lastTurn)
            {
                OnTurnChanged(lastTurn, currentTurn);
                lastTurn = currentTurn;
            }

            // Count down active player
            if (currentTurn == PieceColor.White)
            {
                if (clockMode == ClockMode.SimpleDelay && delayTimeRemaining > 0f)
                {
                    delayTimeRemaining -= Time.deltaTime;
                }
                else
                {
                    whiteTimeRemaining -= Time.deltaTime;
                    if (whiteTimeRemaining < 0f) whiteTimeRemaining = 0f;

                    // Warning Tick Sound for White
                    if (whiteTimeRemaining <= 10f && whiteTimeRemaining > 0f)
                    {
                        float currentSecond = Mathf.Floor(whiteTimeRemaining);
                        if (currentSecond != lastTickSecondWhite)
                        {
                            PlayTickSound();
                            lastTickSecondWhite = currentSecond;
                        }
                    }

                    if (whiteTimeRemaining <= 0f)
                    {
                        FlagFall(PieceColor.White);
                    }
                }
            }
            else
            {
                if (clockMode == ClockMode.SimpleDelay && delayTimeRemaining > 0f)
                {
                    delayTimeRemaining -= Time.deltaTime;
                }
                else
                {
                    blackTimeRemaining -= Time.deltaTime;
                    if (blackTimeRemaining < 0f) blackTimeRemaining = 0f;

                    // Warning Tick Sound for Black
                    if (blackTimeRemaining <= 10f && blackTimeRemaining > 0f)
                    {
                        float currentSecond = Mathf.Floor(blackTimeRemaining);
                        if (currentSecond != lastTickSecondBlack)
                        {
                            PlayTickSound();
                            lastTickSecondBlack = currentSecond;
                        }
                    }

                    if (blackTimeRemaining <= 0f)
                    {
                        FlagFall(PieceColor.Black);
                    }
                }
            }

            UpdateClockUI();
        }

        // Manage HUD display visibility
        if (hudContainer != null)
        {
            hudContainer.SetActive(isClockEnabled && isMatchActive);
        }
    }

    public void StartClock()
    {
        if (!isClockEnabled) return;

        // If starting a fresh match (or recreating HUD), initialize times if they aren't initialized yet
        if (!isClockRunning || whiteTimeRemaining <= 0f)
        {
            whiteTimeRemaining = baseTimeSeconds;
            blackTimeRemaining = baseTimeSeconds;
        }
        
        isClockRunning = true;
        lastTurn = GameManager.Instance != null ? GameManager.Instance.currentTurn : PieceColor.White;
        delayTimeRemaining = (clockMode == ClockMode.SimpleDelay) ? incrementSeconds : 0f;
        lastTickSecondWhite = -1f;
        lastTickSecondBlack = -1f;

        CreateGameplayClockHUD();
        UpdateClockUI();
        Debug.Log("Chess Clock HUD created & clock started.");
    }

    public void StopClock()
    {
        isClockRunning = false;
    }

    public void ResumeClock()
    {
        if (isClockEnabled)
        {
            isClockRunning = true;
        }
    }

    private void OnTurnChanged(PieceColor fromTurn, PieceColor toTurn)
    {
        // 1. Apply increment/bonus if Fischer Mode
        if (clockMode == ClockMode.FischerIncrement)
        {
            if (fromTurn == PieceColor.White)
            {
                whiteTimeRemaining += incrementSeconds;
                if (whiteTimeRemaining > baseTimeSeconds)
                {
                    whiteTimeRemaining = baseTimeSeconds;
                }
            }
            else
            {
                blackTimeRemaining += incrementSeconds;
                if (blackTimeRemaining > baseTimeSeconds)
                {
                    blackTimeRemaining = baseTimeSeconds;
                }
            }
        }

        // 2. Set up Delay if Simple Delay Mode
        if (clockMode == ClockMode.SimpleDelay)
        {
            delayTimeRemaining = incrementSeconds;
        }

        // Reset tick trackers
        lastTickSecondWhite = -1f;
        lastTickSecondBlack = -1f;

        UpdateClockUI();
    }

    private void FlagFall(PieceColor loser)
    {
        StopClock();
        PlayAlarmSound();

        PieceColor winner = (loser == PieceColor.White) ? PieceColor.Black : PieceColor.White;
        Debug.Log($"⏰ Hết giờ! {loser} đã thua trên thời gian.");

        if (GameManager.Instance != null)
        {
            GameManager.Instance.EndGame(winner);
            
            // Override game over UI description
            if (UIManager.Instance != null && UIManager.Instance.gameOverText != null)
            {
                string loserName = (loser == PieceColor.White) ? "Trắng" : "Đen";
                string winnerName = (winner == PieceColor.White) ? "Trắng" : "Đen";
                UIManager.Instance.gameOverText.text = $"{loserName} hết giờ!\n{winnerName} Thắng!";
            }

            if (GameManager.Instance.currentGameMode == GameMode.Online && SocketManager.Instance != null)
            {
                string winnerStr = winner == PieceColor.White ? "white" : "black";
                SocketManager.Instance.SendGameOver(winnerStr, "timeout");
            }
        }
    }

    public void LoadSettings()
    {
        if (PlayerPrefs.GetInt("Clock_Initialized", 0) == 0)
        {
            PlayerPrefs.SetInt("Clock_Enabled", 1);
            PlayerPrefs.SetInt("Clock_Mode", 1); // FischerIncrement
            PlayerPrefs.SetFloat("Clock_BaseTime", 600f);
            PlayerPrefs.SetFloat("Clock_Increment", 5f);
            PlayerPrefs.SetInt("Clock_Initialized", 1);
            PlayerPrefs.Save();
        }

        isClockEnabled = PlayerPrefs.GetInt("Clock_Enabled", 1) == 1;
        clockMode = (ClockMode)PlayerPrefs.GetInt("Clock_Mode", 1);
        baseTimeSeconds = PlayerPrefs.GetFloat("Clock_BaseTime", 600f);
        incrementSeconds = PlayerPrefs.GetFloat("Clock_Increment", 5f);
    }

    public void SaveSettings(bool enabled, ClockMode mode, float baseTime, float increment)
    {
        isClockEnabled = enabled;
        clockMode = mode;
        baseTimeSeconds = baseTime;
        incrementSeconds = increment;

        PlayerPrefs.SetInt("Clock_Enabled", enabled ? 1 : 0);
        PlayerPrefs.SetInt("Clock_Mode", (int)mode);
        PlayerPrefs.SetFloat("Clock_BaseTime", baseTime);
        PlayerPrefs.SetFloat("Clock_Increment", increment);
        PlayerPrefs.Save();

        Debug.Log($"Clock Settings Saved: Enabled={enabled}, Mode={mode}, Base={baseTime}s, Inc={increment}s");
    }

    #region Manual UI Bindings Logic
    private void InitializeManualUI()
    {
        // 1. Initial values from PlayerPrefs
        manualEnabledToggle.isOn = isClockEnabled;
        if (manualModeDropdown != null) manualModeDropdown.value = (int)clockMode;
        if (manualBaseTimeSlider != null) manualBaseTimeSlider.value = baseTimeSeconds / 60f;
        if (manualIncrementSlider != null) manualIncrementSlider.value = incrementSeconds;

        UpdateManualUITexts();

        // 2. Add Listeners
        manualEnabledToggle.onValueChanged.RemoveAllListeners();
        manualEnabledToggle.onValueChanged.AddListener((val) => {
            isClockEnabled = val;
            PlayerPrefs.SetInt("Clock_Enabled", val ? 1 : 0);
            PlayerPrefs.Save();
        });

        if (manualModeDropdown != null)
        {
            manualModeDropdown.onValueChanged.RemoveAllListeners();
            manualModeDropdown.onValueChanged.AddListener((val) => {
                clockMode = (ClockMode)val;
                PlayerPrefs.SetInt("Clock_Mode", val);
                PlayerPrefs.Save();
            });
        }

        if (manualBaseTimeSlider != null)
        {
            manualBaseTimeSlider.onValueChanged.RemoveAllListeners();
            manualBaseTimeSlider.onValueChanged.AddListener((val) => {
                baseTimeSeconds = val * 60f;
                PlayerPrefs.SetFloat("Clock_BaseTime", baseTimeSeconds);
                PlayerPrefs.Save();
                UpdateManualUITexts();
            });
        }

        if (manualIncrementSlider != null)
        {
            manualIncrementSlider.onValueChanged.RemoveAllListeners();
            manualIncrementSlider.onValueChanged.AddListener((val) => {
                incrementSeconds = val;
                PlayerPrefs.SetFloat("Clock_Increment", val);
                PlayerPrefs.Save();
                UpdateManualUITexts();
            });
        }
    }

    private void UpdateManualUITexts()
    {
        if (manualBaseTimeText != null && manualBaseTimeSlider != null)
        {
            manualBaseTimeText.text = $"{manualBaseTimeSlider.value} Phút";
        }
        if (manualIncrementText != null && manualIncrementSlider != null)
        {
            manualIncrementText.text = $"{manualIncrementSlider.value} Giây";
        }
    }
    #endregion

    #region Helper for safe font retrieving
    private TMP_FontAsset GetDefaultFont()
    {
        TMP_FontAsset font = null;
        TextMeshProUGUI[] tmps = FindObjectsOfType<TextMeshProUGUI>(true);
        foreach (var tmp in tmps)
        {
            if (tmp != null && tmp.font != null)
            {
                font = tmp.font;
                break;
            }
        }
        if (font == null)
        {
            font = TMP_Settings.defaultFontAsset;
        }
        return font;
    }
    #endregion

    #region Procedural Audio Generator
    private void CreateProceduralSounds()
    {
        int sampleRate = 44100;

        // 1. Procedural Tick Sound (Short High-Pitch Sine Decaying)
        float frequency = 1200f;
        int tickSamples = (int)(sampleRate * 0.03f); // 30ms
        float[] tickData = new float[tickSamples];
        for (int i = 0; i < tickSamples; i++)
        {
            float t = (float)i / sampleRate;
            tickData[i] = Mathf.Sin(2 * Mathf.PI * frequency * t) * Mathf.Exp(-150f * t);
        }
        tickSound = AudioClip.Create("ClockTick", tickSamples, 1, sampleRate, false);
        tickSound.SetData(tickData, 0);

        // 2. Procedural Alarm Sound (Double Beep tone)
        int alarmSamples = (int)(sampleRate * 0.45f); // 450ms
        float[] alarmData = new float[alarmSamples];
        for (int i = 0; i < alarmSamples; i++)
        {
            float t = (float)i / sampleRate;
            float envelope = 1.0f - ((float)i / alarmSamples);
            // Rapid double-beep effect by modulating volume with a fast square wave
            float modulation = Mathf.PingPong(t * 15f, 1f) > 0.5f ? 1f : 0.1f;
            alarmData[i] = Mathf.Sin(2 * Mathf.PI * 880f * t) * envelope * modulation;
        }
        alarmSound = AudioClip.Create("ClockAlarm", alarmSamples, 1, sampleRate, false);
        alarmSound.SetData(alarmData, 0);
    }

    private void PlayTickSound()
    {
        if (tickSound != null && Camera.main != null)
        {
            AudioSource.PlayClipAtPoint(tickSound, Camera.main.transform.position, 0.4f);
        }
    }

    private void PlayAlarmSound()
    {
        if (alarmSound != null && Camera.main != null)
        {
            AudioSource.PlayClipAtPoint(alarmSound, Camera.main.transform.position, 0.9f);
        }
    }
    #endregion

    #region Gameplay UI HUD Creation
    private void CreateGameplayClockHUD()
    {
        if (hudContainer != null)
        {
            Destroy(hudContainer);
        }

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        TMP_FontAsset font = GetDefaultFont();

        // HUD Parent Panel
        hudContainer = new GameObject("ChessClockHUD", typeof(RectTransform));
        hudContainer.transform.SetParent(canvas.transform, false);

        RectTransform mainRt = hudContainer.GetComponent<RectTransform>();
        mainRt.anchorMin = new Vector2(1, 1);
        mainRt.anchorMax = new Vector2(1, 1);
        mainRt.pivot = new Vector2(1, 1);
        mainRt.sizeDelta = new Vector2(220, 160);
        mainRt.anchoredPosition = new Vector2(-29f, -28f); // Placed beautifully on top-right below standard overlays

        // Glassmorphism Main Background
        Image mainBg = hudContainer.AddComponent<Image>();
        mainBg.color = new Color(0.12f, 0.12f, 0.16f, 0.85f);
        
        Outline outline = hudContainer.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.15f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        // --- BLACK PLAYER PANEL ---
        GameObject blackPanel = new GameObject("BlackTimerPanel", typeof(RectTransform), typeof(Image));
        blackPanel.transform.SetParent(hudContainer.transform, false);
        RectTransform blackRt = blackPanel.GetComponent<RectTransform>();
        blackRt.anchorMin = new Vector2(0, 1);
        blackRt.anchorMax = new Vector2(1, 1);
        blackRt.pivot = new Vector2(0.5f, 1);
        blackRt.sizeDelta = new Vector2(-20, 60);
        blackRt.anchoredPosition = new Vector2(0, -10);

        blackBackground = blackPanel.GetComponent<Image>();
        blackBackground.color = new Color(0.18f, 0.18f, 0.22f, 0.5f);

        // Black Label
        GameObject bLabelGo = new GameObject("BlackLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        bLabelGo.transform.SetParent(blackPanel.transform, false);
        RectTransform bLabelRt = bLabelGo.GetComponent<RectTransform>();
        bLabelRt.anchorMin = new Vector2(0, 1);
        bLabelRt.anchorMax = new Vector2(1, 1);
        bLabelRt.pivot = new Vector2(0.5f, 1);
        bLabelRt.sizeDelta = new Vector2(0, 20);
        bLabelRt.anchoredPosition = new Vector2(10, -3);
        
        blackLabel = bLabelGo.GetComponent<TextMeshProUGUI>();
        blackLabel.font = font;
        blackLabel.text = "BLACK";
        blackLabel.fontSize = 11;
        blackLabel.fontStyle = FontStyles.Bold;
        blackLabel.alignment = TextAlignmentOptions.Left;
        blackLabel.color = new Color(0.7f, 0.7f, 0.7f, 0.8f);

        // Black Time Text
        GameObject bTimeGo = new GameObject("BlackTimeText", typeof(RectTransform), typeof(TextMeshProUGUI));
        bTimeGo.transform.SetParent(blackPanel.transform, false);
        RectTransform bTimeRt = bTimeGo.GetComponent<RectTransform>();
        bTimeRt.anchorMin = Vector2.zero;
        bTimeRt.anchorMax = Vector2.one;
        bTimeRt.offsetMin = new Vector2(10, 5);
        bTimeRt.offsetMax = new Vector2(-10, -5);

        blackTimeText = bTimeGo.GetComponent<TextMeshProUGUI>();
        blackTimeText.font = font;
        blackTimeText.text = "10:00";
        blackTimeText.fontSize = 28;
        blackTimeText.fontStyle = FontStyles.Bold;
        blackTimeText.alignment = TextAlignmentOptions.Right;
        blackTimeText.color = Color.white;

        // Black Delay Text
        GameObject bDelayGo = new GameObject("BlackDelayText", typeof(RectTransform), typeof(TextMeshProUGUI));
        bDelayGo.transform.SetParent(blackPanel.transform, false);
        RectTransform bDelayRt = bDelayGo.GetComponent<RectTransform>();
        bDelayRt.anchorMin = new Vector2(0, 0);
        bDelayRt.anchorMax = new Vector2(1, 0);
        bDelayRt.pivot = new Vector2(0, 0);
        bDelayRt.sizeDelta = new Vector2(0, 20);
        bDelayRt.anchoredPosition = new Vector2(10, 5);

        blackDelayText = bDelayGo.GetComponent<TextMeshProUGUI>();
        blackDelayText.font = font;
        blackDelayText.text = "";
        blackDelayText.fontSize = 11;
        blackDelayText.fontStyle = FontStyles.Bold;
        blackDelayText.alignment = TextAlignmentOptions.Left;
        blackDelayText.color = new Color(0.9f, 0.45f, 0f, 1f);

        // --- WHITE PLAYER PANEL ---
        GameObject whitePanel = new GameObject("WhiteTimerPanel", typeof(RectTransform), typeof(Image));
        whitePanel.transform.SetParent(hudContainer.transform, false);
        RectTransform whiteRt = whitePanel.GetComponent<RectTransform>();
        whiteRt.anchorMin = new Vector2(0, 1);
        whiteRt.anchorMax = new Vector2(1, 1);
        whiteRt.pivot = new Vector2(0.5f, 1);
        whiteRt.sizeDelta = new Vector2(-20, 60);
        whiteRt.anchoredPosition = new Vector2(0, -90);

        whiteBackground = whitePanel.GetComponent<Image>();
        whiteBackground.color = new Color(0.18f, 0.18f, 0.22f, 0.5f);

        // White Label
        GameObject wLabelGo = new GameObject("WhiteLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        wLabelGo.transform.SetParent(whitePanel.transform, false);
        RectTransform wLabelRt = wLabelGo.GetComponent<RectTransform>();
        wLabelRt.anchorMin = new Vector2(0, 1);
        wLabelRt.anchorMax = new Vector2(1, 1);
        wLabelRt.pivot = new Vector2(0.5f, 1);
        wLabelRt.sizeDelta = new Vector2(0, 20);
        wLabelRt.anchoredPosition = new Vector2(10, -3);

        whiteLabel = wLabelGo.GetComponent<TextMeshProUGUI>();
        whiteLabel.font = font;
        whiteLabel.text = "WHITE";
        whiteLabel.fontSize = 11;
        whiteLabel.fontStyle = FontStyles.Bold;
        whiteLabel.alignment = TextAlignmentOptions.Left;
        whiteLabel.color = new Color(0.7f, 0.7f, 0.7f, 0.8f);

        // White Time Text
        GameObject wTimeGo = new GameObject("WhiteTimeText", typeof(RectTransform), typeof(TextMeshProUGUI));
        wTimeGo.transform.SetParent(whitePanel.transform, false);
        RectTransform wTimeRt = wTimeGo.GetComponent<RectTransform>();
        wTimeRt.anchorMin = Vector2.zero;
        wTimeRt.anchorMax = Vector2.one;
        wTimeRt.offsetMin = new Vector2(10, 5);
        wTimeRt.offsetMax = new Vector2(-10, -5);

        whiteTimeText = wTimeGo.GetComponent<TextMeshProUGUI>();
        whiteTimeText.font = font;
        whiteTimeText.text = "10:00";
        whiteTimeText.fontSize = 28;
        whiteTimeText.fontStyle = FontStyles.Bold;
        whiteTimeText.alignment = TextAlignmentOptions.Right;
        whiteTimeText.color = Color.white;

        // White Delay Text
        GameObject wDelayGo = new GameObject("WhiteDelayText", typeof(RectTransform), typeof(TextMeshProUGUI));
        wDelayGo.transform.SetParent(whitePanel.transform, false);
        RectTransform wDelayRt = wDelayGo.GetComponent<RectTransform>();
        wDelayRt.anchorMin = new Vector2(0, 0);
        wDelayRt.anchorMax = new Vector2(1, 0);
        wDelayRt.pivot = new Vector2(0, 0);
        wDelayRt.sizeDelta = new Vector2(0, 20);
        wDelayRt.anchoredPosition = new Vector2(10, 5);

        whiteDelayText = wDelayGo.GetComponent<TextMeshProUGUI>();
        whiteDelayText.font = font;
        whiteDelayText.text = "";
        whiteDelayText.fontSize = 11;
        whiteDelayText.fontStyle = FontStyles.Bold;
        whiteDelayText.alignment = TextAlignmentOptions.Left;
        whiteDelayText.color = new Color(0.9f, 0.45f, 0f, 1f);

        hudContainer.transform.SetAsLastSibling();
    }

    public void UpdateClockUI()
    {
        if (whiteTimeText == null || blackTimeText == null) return;

        whiteTimeText.text = FormatTime(whiteTimeRemaining);
        blackTimeText.text = FormatTime(blackTimeRemaining);

        if (whiteDelayText != null) whiteDelayText.text = "";
        if (blackDelayText != null) blackDelayText.text = "";

        if (GameManager.Instance == null) return;

        PieceColor activeTurn = GameManager.Instance.currentTurn;

        // 1. White Styling (Active vs Inactive)
        if (activeTurn == PieceColor.White)
        {
            if (whiteTimeRemaining <= 30f)
            {
                // Flashing warning red color
                float pingPong = Mathf.PingPong(Time.time * 3f, 1f);
                whiteBackground.color = Color.Lerp(new Color(0.85f, 0.15f, 0.15f, 0.9f), new Color(0.4f, 0.05f, 0.05f, 0.9f), pingPong);
                whiteTimeText.color = Color.white;
                whiteLabel.color = new Color(1f, 1f, 1f, 0.8f);
                if (whiteDelayText != null) whiteDelayText.color = Color.yellow;
            }
            else
            {
                // Active High Contrast White Layout
                whiteBackground.color = new Color(0.95f, 0.95f, 0.95f, 0.95f);
                whiteTimeText.color = new Color(0.08f, 0.08f, 0.08f, 1f);
                whiteLabel.color = new Color(0.15f, 0.15f, 0.15f, 0.7f);
                if (whiteDelayText != null) whiteDelayText.color = new Color(0.85f, 0.35f, 0f, 1f);
            }

            if (clockMode == ClockMode.SimpleDelay && delayTimeRemaining > 0f && whiteDelayText != null)
            {
                whiteDelayText.text = $"(Delay: {delayTimeRemaining:0.0}s)";
            }
        }
        else
        {
            // Inactive style
            whiteBackground.color = new Color(0.18f, 0.18f, 0.22f, 0.4f);
            whiteTimeText.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            whiteLabel.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        }

        // 2. Black Styling (Active vs Inactive)
        if (activeTurn == PieceColor.Black)
        {
            if (blackTimeRemaining <= 30f)
            {
                // Flashing warning red color
                float pingPong = Mathf.PingPong(Time.time * 3f, 1f);
                blackBackground.color = Color.Lerp(new Color(0.85f, 0.15f, 0.15f, 0.9f), new Color(0.4f, 0.05f, 0.05f, 0.9f), pingPong);
                blackTimeText.color = Color.white;
                blackLabel.color = new Color(1f, 1f, 1f, 0.8f);
                if (blackDelayText != null) blackDelayText.color = Color.yellow;
            }
            else
            {
                // Active High Contrast Black Layout
                blackBackground.color = new Color(0.95f, 0.95f, 0.95f, 0.95f);
                blackTimeText.color = new Color(0.08f, 0.08f, 0.08f, 1f);
                blackLabel.color = new Color(0.15f, 0.15f, 0.15f, 0.7f);
                if (blackDelayText != null) blackDelayText.color = new Color(0.85f, 0.35f, 0f, 1f);
            }

            if (clockMode == ClockMode.SimpleDelay && delayTimeRemaining > 0f && blackDelayText != null)
            {
                blackDelayText.text = $"(Delay: {delayTimeRemaining:0.0}s)";
            }
        }
        else
        {
            // Inactive style
            blackBackground.color = new Color(0.18f, 0.18f, 0.22f, 0.4f);
            blackTimeText.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            blackLabel.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        }
    }

    private string FormatTime(float timeInSeconds)
    {
        if (timeInSeconds <= 0f) return "00:00.0";

        if (timeInSeconds > 20f)
        {
            // Standard MM:SS format
            int minutes = Mathf.FloorToInt(timeInSeconds / 60f);
            int seconds = Mathf.FloorToInt(timeInSeconds % 60f);
            return $"{minutes:00}:{seconds:00}";
        }
        else
        {
            // Premium digital SS.f format for time pressure
            int seconds = Mathf.FloorToInt(timeInSeconds);
            int tenths = Mathf.FloorToInt((timeInSeconds - seconds) * 10f);
        return $"{seconds:00}.{tenths:0}";
        }
    }
    #endregion

    #region Auto-Injected UI Settings
    public void EnsureSettingsInjected()
    {
        // Check if hand-crafted custom UI is present in settings panel first
        if (manualEnabledToggle != null || FindAndInitializeCustomUI())
        {
            return;
        }

        if (subPanelInstance != null) return;

        GameMenu gameMenu = FindObjectOfType<GameMenu>();
        if (gameMenu == null || gameMenu.settingsPanel == null) return;

        Transform existing = gameMenu.settingsPanel.transform.Find("ClockSettingsSubPanel");
        if (existing != null)
        {
            subPanelInstance = existing.gameObject;
            return;
        }

        StartCoroutine(InjectSettingsPanelUICoroutine(0f));
    }

    private IEnumerator InjectSettingsPanelUICoroutine(float delay = 0.2f)
    {
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        GameMenu gameMenu = FindObjectOfType<GameMenu>();
        if (gameMenu == null || gameMenu.settingsPanel == null)
        {
            yield break;
        }

        Transform settingsTransform = gameMenu.settingsPanel.transform;

        if (subPanelInstance != null)
        {
            yield break;
        }

        Transform existingSub = settingsTransform.Find("ClockSettingsSubPanel");
        if (existingSub != null)
        {
            subPanelInstance = existingSub.gameObject;
            yield break;
        }

        TMP_FontAsset font = GetDefaultFont();

        // A. Separate original children: TitleText, Back Button, and Skin Settings
        System.Collections.Generic.List<Transform> itemsToMove = new System.Collections.Generic.List<Transform>();
        Transform backButton = null;
        Transform titleText = null;

        for (int i = 0; i < settingsTransform.childCount; i++)
        {
            Transform child = settingsTransform.GetChild(i);
            if (child.name == "ClockSettingsSubPanel" || child.name == "SkinSettingsSubPanel")
            {
                continue;
            }

            // Identify back/close button: has Button component and is not dropdown
            if (child.GetComponent<Button>() != null && child.GetComponent<TMP_Dropdown>() == null)
            {
                backButton = child;
                continue;
            }

            // Identify title text: index 0 or name contains Title/Text
            if (child.name.ToLower().Contains("title") || child.name.ToLower().Contains("text") || i == 0)
            {
                titleText = child;
                continue;
            }

            // Move all other elements (labels, dropdowns)
            itemsToMove.Add(child);
        }

        // B. Create SkinSettingsSubPanel on the left side
        GameObject skinPanel = null;
        Transform existingSkin = settingsTransform.Find("SkinSettingsSubPanel");
        if (existingSkin != null)
        {
            skinPanel = existingSkin.gameObject;
        }
        else
        {
            skinPanel = new GameObject("SkinSettingsSubPanel", typeof(RectTransform), typeof(Image));
            skinPanel.transform.SetParent(settingsTransform, false);

            RectTransform skinRt = skinPanel.GetComponent<RectTransform>();
            skinRt.anchorMin = new Vector2(0.04f, 0.12f);
            skinRt.anchorMax = new Vector2(0.48f, 0.88f);
            skinRt.pivot = new Vector2(0.5f, 0.5f);
            skinRt.offsetMin = Vector2.zero;
            skinRt.offsetMax = Vector2.zero;

            Image skinImg = skinPanel.GetComponent<Image>();
            skinImg.color = new Color(0.08f, 0.08f, 0.12f, 0.85f); // Matching dark layout

            Outline skinOutline = skinPanel.AddComponent<Outline>();
            skinOutline.effectColor = new Color(1f, 1f, 1f, 0.05f);

            LayoutElement skinLayout = skinPanel.AddComponent<LayoutElement>();
            skinLayout.ignoreLayout = true;

            VerticalLayoutGroup skinVlg = skinPanel.AddComponent<VerticalLayoutGroup>();
            skinVlg.spacing = 15;
            skinVlg.padding = new RectOffset(25, 25, 30, 25);
            skinVlg.childAlignment = TextAnchor.UpperCenter;
            skinVlg.childControlWidth = true;
            skinVlg.childControlHeight = false;
            skinVlg.childForceExpandWidth = true;
            skinVlg.childForceExpandHeight = false;
        }

        // Move skin elements
        foreach (Transform item in itemsToMove)
        {
            item.SetParent(skinPanel.transform, false);
        }

        // C. Position Title Text at top-center
        if (titleText != null)
        {
            LayoutElement titleLayout = titleText.GetComponent<LayoutElement>();
            if (titleLayout == null) titleLayout = titleText.gameObject.AddComponent<LayoutElement>();
            titleLayout.ignoreLayout = true;

            RectTransform titleRt = titleText.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 0.94f);
            titleRt.anchorMax = new Vector2(0.5f, 0.94f);
            titleRt.pivot = new Vector2(0.5f, 0.5f);
            titleRt.anchoredPosition = new Vector2(0, 0);

            TextMeshProUGUI tmpText = titleText.GetComponent<TextMeshProUGUI>();
            if (tmpText != null)
            {
                tmpText.text = "CÀI ĐẶT GAME";
                tmpText.fontSize = 24;
                tmpText.fontStyle = FontStyles.Bold;
                tmpText.alignment = TextAlignmentOptions.Center;
            }
        }

        // D. Position Back Button at bottom-center
        if (backButton != null)
        {
            LayoutElement backLayout = backButton.GetComponent<LayoutElement>();
            if (backLayout == null) backLayout = backButton.gameObject.AddComponent<LayoutElement>();
            backLayout.ignoreLayout = true;

            RectTransform backRt = backButton.GetComponent<RectTransform>();
            backRt.anchorMin = new Vector2(0.5f, 0.06f);
            backRt.anchorMax = new Vector2(0.5f, 0.06f);
            backRt.pivot = new Vector2(0.5f, 0.5f);
            backRt.anchoredPosition = new Vector2(0, 0);
        }

        // E. Disable original VerticalLayoutGroup on SettingsPanel to free positioning
        VerticalLayoutGroup mainVlg = settingsTransform.GetComponent<VerticalLayoutGroup>();
        if (mainVlg != null)
        {
            mainVlg.enabled = false;
        }

        // 1. Sub-panel container
        GameObject subPanel = new GameObject("ClockSettingsSubPanel", typeof(RectTransform), typeof(Image));
        subPanel.transform.SetParent(settingsTransform, false);
        subPanelInstance = subPanel;

        LayoutElement clockLayout = subPanel.AddComponent<LayoutElement>();
        clockLayout.ignoreLayout = true;

        RectTransform subRt = subPanel.GetComponent<RectTransform>();
        // Anchor to the right side of the settings panel
        subRt.anchorMin = new Vector2(0.52f, 0.12f);
        subRt.anchorMax = new Vector2(0.96f, 0.88f);
        subRt.pivot = new Vector2(0.5f, 0.5f);
        subRt.offsetMin = Vector2.zero;
        subRt.offsetMax = Vector2.zero;

        Image subImg = subPanel.GetComponent<Image>();
        subImg.color = new Color(0.08f, 0.08f, 0.12f, 0.85f); // Beautiful distinct backdrop

        Outline outline = subPanel.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.05f);

        // 2. Title Header
        GameObject headerGo = new GameObject("ClockTitle", typeof(RectTransform), typeof(TextMeshProUGUI));
        headerGo.transform.SetParent(subPanel.transform, false);
        RectTransform headerRt = headerGo.GetComponent<RectTransform>();
        headerRt.anchorMin = new Vector2(0, 1);
        headerRt.anchorMax = new Vector2(1, 1);
        headerRt.pivot = new Vector2(0.5f, 1);
        headerRt.sizeDelta = new Vector2(0, 30);
        headerRt.anchoredPosition = new Vector2(0, -15);

        TextMeshProUGUI headerTxt = headerGo.GetComponent<TextMeshProUGUI>();
        headerTxt.font = font;
        headerTxt.text = "CÀI ĐẶT ĐỒNG HỒ CỜ VUA";
        headerTxt.fontSize = 14;
        headerTxt.fontStyle = FontStyles.Bold;
        headerTxt.alignment = TextAlignmentOptions.Center;
        headerTxt.color = new Color(0.2f, 0.6f, 0.9f, 1f);

        // Local state copies
        bool tempEnabled = isClockEnabled;
        ClockMode tempMode = clockMode;
        float tempBaseTime = baseTimeSeconds;
        float tempInc = incrementSeconds;

        int selectedPresetIndex = -1;
        float[] presetSeconds = { 60f, 180f, 300f, 600f, 1800f };
        for (int i = 0; i < presetSeconds.Length; i++)
        {
            if (Mathf.Approximately(tempBaseTime, presetSeconds[i]))
                selectedPresetIndex = i;
        }
        if (selectedPresetIndex == -1) selectedPresetIndex = 5;

        // UI element references
        GameObject customTimeContainer = null;
        GameObject incContainer = null;
        TextMeshProUGUI customTimeValText = null;
        TextMeshProUGUI incValText = null;
        Image enableClockBtnImg = null;
        TextMeshProUGUI enableClockBtnText = null;

        Image[] modeBtnImgs = new Image[3];
        TextMeshProUGUI[] modeBtnTexts = new TextMeshProUGUI[3];
        Image[] presetBtnImgs = new Image[6];
        TextMeshProUGUI[] presetBtnTexts = new TextMeshProUGUI[6];

        // --- ROW 1: TOGGLE CLOCK ---
        GameObject row1 = new GameObject("ToggleRow", typeof(RectTransform));
        row1.transform.SetParent(subPanel.transform, false);
        RectTransform r1Rt = row1.GetComponent<RectTransform>();
        r1Rt.anchorMin = new Vector2(0, 1);
        r1Rt.anchorMax = new Vector2(1, 1);
        r1Rt.pivot = new Vector2(0.5f, 1);
        r1Rt.sizeDelta = new Vector2(-30, 40);
        r1Rt.anchoredPosition = new Vector2(0, -50);

        GameObject r1Lbl = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        r1Lbl.transform.SetParent(row1.transform, false);
        r1Lbl.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0.5f);
        r1Lbl.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);
        r1Lbl.GetComponent<RectTransform>().pivot = new Vector2(0, 0.5f);
        r1Lbl.GetComponent<RectTransform>().anchoredPosition = new Vector2(5, 0);
        r1Lbl.GetComponent<TextMeshProUGUI>().font = font;
        r1Lbl.GetComponent<TextMeshProUGUI>().text = "Bật đồng hồ:";
        r1Lbl.GetComponent<TextMeshProUGUI>().fontSize = 12;
        r1Lbl.GetComponent<TextMeshProUGUI>().color = Color.white;

        GameObject toggleBtn = new GameObject("ToggleBtn", typeof(RectTransform), typeof(Image), typeof(Button));
        toggleBtn.transform.SetParent(row1.transform, false);
        RectTransform tRt = toggleBtn.GetComponent<RectTransform>();
        tRt.anchorMin = new Vector2(1, 0.5f);
        tRt.anchorMax = new Vector2(1, 0.5f);
        tRt.pivot = new Vector2(1, 0.5f);
        tRt.sizeDelta = new Vector2(90, 26);
        tRt.anchoredPosition = new Vector2(-5, 0);

        enableClockBtnImg = toggleBtn.GetComponent<Image>();
        enableClockBtnText = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        enableClockBtnText.transform.SetParent(toggleBtn.transform, false);
        enableClockBtnText.GetComponent<RectTransform>().anchorMin = Vector2.zero;
        enableClockBtnText.GetComponent<RectTransform>().anchorMax = Vector2.one;
        enableClockBtnText.GetComponent<RectTransform>().offsetMin = Vector2.zero;
        enableClockBtnText.GetComponent<RectTransform>().offsetMax = Vector2.zero;
        enableClockBtnText.font = font;
        enableClockBtnText.alignment = TextAlignmentOptions.Center;
        enableClockBtnText.fontSize = 11;
        enableClockBtnText.fontStyle = FontStyles.Bold;

        // --- ROW 2: CLOCK MODES ---
        GameObject row2 = new GameObject("ModeRow", typeof(RectTransform));
        row2.transform.SetParent(subPanel.transform, false);
        RectTransform r2Rt = row2.GetComponent<RectTransform>();
        r2Rt.anchorMin = new Vector2(0, 1);
        r2Rt.anchorMax = new Vector2(1, 1);
        r2Rt.pivot = new Vector2(0.5f, 1);
        r2Rt.sizeDelta = new Vector2(-30, 50);
        r2Rt.anchoredPosition = new Vector2(0, -95);

        GameObject r2Lbl = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        r2Lbl.transform.SetParent(row2.transform, false);
        r2Lbl.GetComponent<RectTransform>().anchorMin = new Vector2(0, 1);
        r2Lbl.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
        r2Lbl.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1);
        r2Lbl.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 18);
        r2Lbl.GetComponent<RectTransform>().anchoredPosition = new Vector2(5, 0);
        r2Lbl.GetComponent<TextMeshProUGUI>().font = font;
        r2Lbl.GetComponent<TextMeshProUGUI>().text = "Chế độ chơi:";
        r2Lbl.GetComponent<TextMeshProUGUI>().fontSize = 11;
        r2Lbl.GetComponent<TextMeshProUGUI>().color = new Color(0.7f, 0.7f, 0.7f);

        string[] modeNames = { "Sudden Death", "Fischer Inc", "Simple Delay" };
        for (int i = 0; i < 3; i++)
        {
            GameObject mBtn = new GameObject($"ModeBtn_{i}", typeof(RectTransform), typeof(Image), typeof(Button));
            mBtn.transform.SetParent(row2.transform, false);
            RectTransform mBtnRt = mBtn.GetComponent<RectTransform>();
            mBtnRt.anchorMin = new Vector2(0, 0);
            mBtnRt.anchorMax = new Vector2(0.33f, 0);
            mBtnRt.pivot = new Vector2(0.5f, 0);
            mBtnRt.sizeDelta = new Vector2(-4, 25);
            mBtnRt.anchoredPosition = new Vector2(5 + i * 115f + 50f, 0);

            modeBtnImgs[i] = mBtn.GetComponent<Image>();
            
            TextMeshProUGUI mTxt = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            mTxt.transform.SetParent(mBtn.transform, false);
            mTxt.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            mTxt.GetComponent<RectTransform>().anchorMax = Vector2.one;
            mTxt.font = font;
            mTxt.alignment = TextAlignmentOptions.Center;
            mTxt.fontSize = 9;
            mTxt.fontStyle = FontStyles.Bold;
            mTxt.text = modeNames[i];
            modeBtnTexts[i] = mTxt;
        }

        // --- ROW 3: TIME PRESETS ---
        GameObject presetRow = new GameObject("PresetRow", typeof(RectTransform));
        presetRow.transform.SetParent(subPanel.transform, false);
        RectTransform r3Rt = presetRow.GetComponent<RectTransform>();
        r3Rt.anchorMin = new Vector2(0, 1);
        r3Rt.anchorMax = new Vector2(1, 1);
        r3Rt.pivot = new Vector2(0.5f, 1);
        r3Rt.sizeDelta = new Vector2(-30, 50);
        r3Rt.anchoredPosition = new Vector2(0, -150);

        GameObject r3Lbl = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        r3Lbl.transform.SetParent(presetRow.transform, false);
        r3Lbl.GetComponent<RectTransform>().anchorMin = new Vector2(0, 1);
        r3Lbl.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
        r3Lbl.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1);
        r3Lbl.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 18);
        r3Lbl.GetComponent<RectTransform>().anchoredPosition = new Vector2(5, 0);
        r3Lbl.GetComponent<TextMeshProUGUI>().font = font;
        r3Lbl.GetComponent<TextMeshProUGUI>().text = "Thời gian:";
        r3Lbl.GetComponent<TextMeshProUGUI>().fontSize = 11;
        r3Lbl.GetComponent<TextMeshProUGUI>().color = new Color(0.7f, 0.7f, 0.7f);

        string[] presetLabels = { "1m", "3m", "5m", "10m", "30m", "Tùy chọn" };
        for (int i = 0; i < 6; i++)
        {
            GameObject pBtn = new GameObject($"PresetBtn_{i}", typeof(RectTransform), typeof(Image), typeof(Button));
            pBtn.transform.SetParent(presetRow.transform, false);
            RectTransform pBtnRt = pBtn.GetComponent<RectTransform>();
            pBtnRt.anchorMin = new Vector2(0, 0);
            pBtnRt.anchorMax = new Vector2(0.166f, 0);
            pBtnRt.pivot = new Vector2(0.5f, 0);
            pBtnRt.sizeDelta = new Vector2(-3, 25);
            pBtnRt.anchoredPosition = new Vector2(5 + i * 57f + 25f, 0);

            presetBtnImgs[i] = pBtn.GetComponent<Image>();

            TextMeshProUGUI pTxt = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            pTxt.transform.SetParent(pBtn.transform, false);
            pTxt.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            pTxt.GetComponent<RectTransform>().anchorMax = Vector2.one;
            pTxt.font = font;
            pTxt.alignment = TextAlignmentOptions.Center;
            pTxt.fontSize = 9;
            pTxt.fontStyle = FontStyles.Bold;
            pTxt.text = presetLabels[i];
            presetBtnTexts[i] = pTxt;
        }

        // --- ROW 4: CUSTOM TIME SLIDER ---
        customTimeContainer = new GameObject("CustomTimeContainer", typeof(RectTransform));
        customTimeContainer.transform.SetParent(subPanel.transform, false);
        RectTransform ctRt = customTimeContainer.GetComponent<RectTransform>();
        ctRt.anchorMin = new Vector2(0, 1);
        ctRt.anchorMax = new Vector2(1, 1);
        ctRt.pivot = new Vector2(0.5f, 1);
        ctRt.sizeDelta = new Vector2(-30, 45);
        ctRt.anchoredPosition = new Vector2(0, -205);

        GameObject ctLbl = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        ctLbl.transform.SetParent(customTimeContainer.transform, false);
        ctLbl.GetComponent<RectTransform>().anchorMin = new Vector2(0, 1);
        ctLbl.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 1);
        ctLbl.GetComponent<RectTransform>().pivot = new Vector2(0, 1);
        ctLbl.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 18);
        ctLbl.GetComponent<RectTransform>().anchoredPosition = new Vector2(5, 0);
        ctLbl.GetComponent<TextMeshProUGUI>().font = font;
        ctLbl.GetComponent<TextMeshProUGUI>().text = "Tùy chỉnh phút:";
        ctLbl.GetComponent<TextMeshProUGUI>().fontSize = 11;
        ctLbl.GetComponent<TextMeshProUGUI>().color = new Color(0.6f, 0.6f, 0.6f);

        GameObject ctValGo = new GameObject("Value", typeof(RectTransform), typeof(TextMeshProUGUI));
        ctValGo.transform.SetParent(customTimeContainer.transform, false);
        ctValGo.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 1);
        ctValGo.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
        ctValGo.GetComponent<RectTransform>().pivot = new Vector2(1, 1);
        ctValGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 18);
        ctValGo.GetComponent<RectTransform>().anchoredPosition = new Vector2(-5, 0);
        customTimeValText = ctValGo.GetComponent<TextMeshProUGUI>();
        customTimeValText.font = font;
        customTimeValText.alignment = TextAlignmentOptions.Right;
        customTimeValText.fontSize = 11;
        customTimeValText.fontStyle = FontStyles.Bold;
        customTimeValText.color = Color.white;

        GameObject customSliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        customSliderGo.transform.SetParent(customTimeContainer.transform, false);
        RectTransform csRt = customSliderGo.GetComponent<RectTransform>();
        csRt.anchorMin = new Vector2(0, 0);
        csRt.anchorMax = new Vector2(1, 0);
        csRt.pivot = new Vector2(0.5f, 0);
        csRt.sizeDelta = new Vector2(-10, 15);
        csRt.anchoredPosition = new Vector2(0, 3);

        Slider timeSlider = customSliderGo.GetComponent<Slider>();
        timeSlider.minValue = 1;
        timeSlider.maxValue = 120;
        timeSlider.wholeNumbers = true;
        timeSlider.value = Mathf.Max(1f, Mathf.Round(tempBaseTime / 60f));

        GameObject trackT = new GameObject("Track", typeof(RectTransform), typeof(Image));
        trackT.transform.SetParent(customSliderGo.transform, false);
        trackT.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0.4f);
        trackT.GetComponent<RectTransform>().anchorMax = new Vector2(1, 0.6f);
        trackT.GetComponent<RectTransform>().offsetMin = Vector2.zero;
        trackT.GetComponent<RectTransform>().offsetMax = Vector2.zero;
        trackT.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.3f, 1f);

        GameObject fillAreaT = new GameObject("FillArea", typeof(RectTransform));
        fillAreaT.transform.SetParent(customSliderGo.transform, false);
        fillAreaT.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0);
        fillAreaT.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
        fillAreaT.GetComponent<RectTransform>().offsetMin = new Vector2(4, 0);
        fillAreaT.GetComponent<RectTransform>().offsetMax = new Vector2(-4, 0);

        GameObject fillT = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillT.transform.SetParent(fillAreaT.transform, false);
        fillT.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0.4f);
        fillT.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.6f);
        fillT.GetComponent<RectTransform>().offsetMin = Vector2.zero;
        fillT.GetComponent<RectTransform>().offsetMax = Vector2.zero;
        fillT.GetComponent<Image>().color = new Color(0.2f, 0.6f, 0.85f, 1f);
        timeSlider.fillRect = fillT.GetComponent<RectTransform>();

        GameObject handleAreaT = new GameObject("HandleArea", typeof(RectTransform));
        handleAreaT.transform.SetParent(customSliderGo.transform, false);
        handleAreaT.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0);
        handleAreaT.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
        handleAreaT.GetComponent<RectTransform>().offsetMin = new Vector2(8, 0);
        handleAreaT.GetComponent<RectTransform>().offsetMax = new Vector2(-8, 0);

        GameObject handleT = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleT.transform.SetParent(handleAreaT.transform, false);
        handleT.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0);
        handleT.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 1);
        handleT.GetComponent<RectTransform>().sizeDelta = new Vector2(12, 0);
        handleT.GetComponent<Image>().color = Color.white;
        timeSlider.handleRect = handleT.GetComponent<RectTransform>();

        // --- ROW 5: INCREMENT / DELAY SLIDER ---
        incContainer = new GameObject("IncContainer", typeof(RectTransform));
        incContainer.transform.SetParent(subPanel.transform, false);
        RectTransform incRt = incContainer.GetComponent<RectTransform>();
        incRt.anchorMin = new Vector2(0, 1);
        incRt.anchorMax = new Vector2(1, 1);
        incRt.pivot = new Vector2(0.5f, 1);
        incRt.sizeDelta = new Vector2(-30, 45);
        incRt.anchoredPosition = new Vector2(0, -255);

        GameObject incLbl = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        incLbl.transform.SetParent(incContainer.transform, false);
        incLbl.GetComponent<RectTransform>().anchorMin = new Vector2(0, 1);
        incLbl.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 1);
        incLbl.GetComponent<RectTransform>().pivot = new Vector2(0, 1);
        incLbl.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 18);
        incLbl.GetComponent<RectTransform>().anchoredPosition = new Vector2(5, 0);
        incLbl.GetComponent<TextMeshProUGUI>().font = font;
        incLbl.GetComponent<TextMeshProUGUI>().text = "Tùy chỉnh cộng thêm:";
        incLbl.GetComponent<TextMeshProUGUI>().fontSize = 11;
        incLbl.GetComponent<TextMeshProUGUI>().color = new Color(0.6f, 0.6f, 0.6f);

        GameObject incValGo = new GameObject("Value", typeof(RectTransform), typeof(TextMeshProUGUI));
        incValGo.transform.SetParent(incContainer.transform, false);
        incValGo.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 1);
        incValGo.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
        incValGo.GetComponent<RectTransform>().pivot = new Vector2(1, 1);
        incValGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 18);
        incValGo.GetComponent<RectTransform>().anchoredPosition = new Vector2(-5, 0);
        incValText = incValGo.GetComponent<TextMeshProUGUI>();
        incValText.font = font;
        incValText.alignment = TextAlignmentOptions.Right;
        incValText.fontSize = 11;
        incValText.fontStyle = FontStyles.Bold;
        incValText.color = Color.white;

        GameObject sliderIncGo = new GameObject("SliderInc", typeof(RectTransform), typeof(Slider));
        sliderIncGo.transform.SetParent(incContainer.transform, false);
        RectTransform siRt = sliderIncGo.GetComponent<RectTransform>();
        siRt.anchorMin = new Vector2(0, 0);
        siRt.anchorMax = new Vector2(1, 0);
        siRt.pivot = new Vector2(0.5f, 0);
        siRt.sizeDelta = new Vector2(-10, 15);
        siRt.anchoredPosition = new Vector2(0, 3);

        Slider incSlider = sliderIncGo.GetComponent<Slider>();
        incSlider.minValue = 0;
        incSlider.maxValue = 60;
        incSlider.wholeNumbers = true;
        incSlider.value = tempInc;

        GameObject trackI = new GameObject("Track", typeof(RectTransform), typeof(Image));
        trackI.transform.SetParent(sliderIncGo.transform, false);
        trackI.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0.4f);
        trackI.GetComponent<RectTransform>().anchorMax = new Vector2(1, 0.6f);
        trackI.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.3f, 1f);

        GameObject fillAreaI = new GameObject("FillArea", typeof(RectTransform));
        fillAreaI.transform.SetParent(sliderIncGo.transform, false);
        fillAreaI.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0);
        fillAreaI.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);

        GameObject fillI = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillI.transform.SetParent(fillAreaI.transform, false);
        fillI.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0.4f);
        fillI.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.6f);
        fillI.GetComponent<Image>().color = new Color(0.2f, 0.6f, 0.85f, 1f);
        incSlider.fillRect = fillI.GetComponent<RectTransform>();

        GameObject handleAreaI = new GameObject("HandleArea", typeof(RectTransform));
        handleAreaI.transform.SetParent(sliderIncGo.transform, false);
        handleAreaI.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0);
        handleAreaI.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);

        GameObject handleI = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleI.transform.SetParent(handleAreaI.transform, false);
        handleI.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0);
        handleI.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 1);
        handleI.GetComponent<RectTransform>().sizeDelta = new Vector2(12, 0);
        handleI.GetComponent<Image>().color = Color.white;
        incSlider.handleRect = handleI.GetComponent<RectTransform>();

        // Reactive UI refresh method
        System.Action RefreshSubUI = () =>
        {
            if (tempEnabled)
            {
                enableClockBtnImg.color = new Color(0.2f, 0.6f, 0.35f, 1f);
                enableClockBtnText.text = "ĐANG BẬT";
                enableClockBtnText.color = Color.white;
            }
            else
            {
                enableClockBtnImg.color = new Color(0.35f, 0.35f, 0.4f, 1f);
                enableClockBtnText.text = "ĐANG TẮT";
                enableClockBtnText.color = new Color(0.8f, 0.8f, 0.8f);
            }

            // Dim blocks based on selected modes
            row2.SetActive(tempEnabled);
            presetRow.SetActive(tempEnabled);
            customTimeContainer.SetActive(tempEnabled && selectedPresetIndex == 5);
            incContainer.SetActive(tempEnabled && tempMode != ClockMode.SuddenDeath);

            // Highlights mode buttons
            for (int i = 0; i < 3; i++)
            {
                if (i == (int)tempMode)
                {
                    modeBtnImgs[i].color = new Color(0.2f, 0.55f, 0.8f, 1f);
                    modeBtnTexts[i].color = Color.white;
                }
                else
                {
                    modeBtnImgs[i].color = new Color(0.18f, 0.18f, 0.22f, 0.5f);
                    modeBtnTexts[i].color = new Color(0.6f, 0.6f, 0.6f);
                }
            }

            // Highlights preset buttons
            for (int i = 0; i < 6; i++)
            {
                if (i == selectedPresetIndex)
                {
                    presetBtnImgs[i].color = new Color(0.2f, 0.55f, 0.8f, 1f);
                    presetBtnTexts[i].color = Color.white;
                }
                else
                {
                    presetBtnImgs[i].color = new Color(0.18f, 0.18f, 0.22f, 0.5f);
                    presetBtnTexts[i].color = new Color(0.6f, 0.6f, 0.6f);
                }
            }

            customTimeValText.text = $"{timeSlider.value} Phút";
            incValText.text = $"{incSlider.value} Giây";

            // Save settings reactively on change
            SaveSettings(tempEnabled, tempMode, tempBaseTime, tempInc);
        };

        // Event hooks
        toggleBtn.GetComponent<Button>().onClick.AddListener(() =>
        {
            tempEnabled = !tempEnabled;
            RefreshSubUI();
        });

        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            modeBtnImgs[i].GetComponent<Button>().onClick.AddListener(() =>
            {
                tempMode = (ClockMode)idx;
                RefreshSubUI();
            });
        }

        for (int i = 0; i < 6; i++)
        {
            int idx = i;
            presetBtnImgs[i].GetComponent<Button>().onClick.AddListener(() =>
            {
                selectedPresetIndex = idx;
                if (idx < 5)
                {
                    tempBaseTime = presetSeconds[idx];
                    timeSlider.value = tempBaseTime / 60f;
                }
                RefreshSubUI();
            });
        }

        timeSlider.onValueChanged.AddListener((val) =>
        {
            if (selectedPresetIndex == 5)
            {
                tempBaseTime = val * 60f;
            }
            RefreshSubUI();
        });

        incSlider.onValueChanged.AddListener((val) =>
        {
            tempInc = val;
            RefreshSubUI();
        });

        // First render
        RefreshSubUI();
        Debug.Log("Successfully auto-injected Clock Settings Sub-Panel inside settingsPanel.");
    }
    #endregion

    // Audio clips are played using standard helper methods defined earlier.

    #region Menu Button & Settings Panel Injection Fallbacks
    private IEnumerator InjectSettingsButtonCoroutine()
    {
        // Wait for Canvas and GameMenu to initialize
        yield return new WaitForSeconds(0.2f);

        Canvas canvas = FindObjectOfType<Canvas>();
        GameMenu gameMenu = FindObjectOfType<GameMenu>();

        if (canvas == null || gameMenu == null || gameMenu.titleMenuPanel == null)
        {
            yield break;
        }

        // Check if button already exists
        if (GameObject.Find("Btn_ClockSetup") != null)
        {
            yield break;
        }

        // Find reference button to clone (like Settings button or Logout button)
        Button refButton = null;
        Button[] btns = gameMenu.titleMenuPanel.GetComponentsInChildren<Button>(true);
        foreach (var btn in btns)
        {
            if (btn.gameObject.name.Contains("Settings") || btn.gameObject.name.Contains("Play") || btn.gameObject.name.Contains("Difficulty"))
            {
                refButton = btn;
                break;
            }
        }

        if (refButton == null && btns.Length > 0) refButton = btns[0];
        if (refButton == null) yield break;

        // Clone reference button
        clockSetupButton = Instantiate(refButton.gameObject, gameMenu.titleMenuPanel.transform);
        clockSetupButton.name = "Btn_ClockSetup";
        
        Button setupBtn = clockSetupButton.GetComponent<Button>();
        setupBtn.onClick.RemoveAllListeners();
        setupBtn.onClick.AddListener(OpenSettingsPopup);

        // Change text
        TextMeshProUGUI tmp = clockSetupButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp != null) tmp.text = "Cài Đặt Đồng Hồ";
        
        Text legacyText = clockSetupButton.GetComponentInChildren<Text>(true);
        if (legacyText != null) legacyText.text = "Cài Đặt Đồng Hồ";

        // Layout offset if there's no VerticalLayoutGroup
        if (gameMenu.titleMenuPanel.GetComponent<VerticalLayoutGroup>() == null &&
            gameMenu.titleMenuPanel.GetComponentInChildren<VerticalLayoutGroup>() == null)
        {
            RectTransform refRt = refButton.GetComponent<RectTransform>();
            RectTransform setupRt = clockSetupButton.GetComponent<RectTransform>();
            setupRt.anchoredPosition = refRt.anchoredPosition + new Vector2(0f, -80f);
        }
        else
        {
            // Just push it to be below Settings
            clockSetupButton.transform.SetSiblingIndex(refButton.transform.GetSiblingIndex() + 1);
        }

        Debug.Log("Successfully injected 'Clock Settings' button into Main Menu.");
    }

    public void OpenSettingsPopup()
    {
        if (settingsPanelInstance != null) return;

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        bool tempEnabled = isClockEnabled;
        ClockMode tempMode = clockMode;
        float tempBaseTime = baseTimeSeconds;
        float tempInc = incrementSeconds;

        int selectedPresetIndex = -1;
        float[] presetSeconds = { 60f, 180f, 300f, 600f, 1800f };
        for (int i = 0; i < presetSeconds.Length; i++)
        {
            if (Mathf.Approximately(tempBaseTime, presetSeconds[i]))
                selectedPresetIndex = i;
        }
        if (selectedPresetIndex == -1) selectedPresetIndex = 5; // Custom

        // UI references for interactive updates
        GameObject customTimeContainer = null;
        GameObject incContainer = null;
        TextMeshProUGUI customTimeValText = null;
        TextMeshProUGUI incValText = null;
        Image enableClockBtnImg = null;
        TextMeshProUGUI enableClockBtnText = null;

        Image[] modeBtnImgs = new Image[3];
        TextMeshProUGUI[] modeBtnTexts = new TextMeshProUGUI[3];
        Image[] presetBtnImgs = new Image[6];
        TextMeshProUGUI[] presetBtnTexts = new TextMeshProUGUI[6];

        GameObject modeRow = null;
        GameObject presetRow = null;
        Slider timeSlider = null;
        Slider incSlider = null;
        System.Action RefreshPopupUI = null;
        Button toggleBtn = null;
        Button cancelBtn = null;
        Button saveBtn = null;

        // PREFAB-BASED POPUP
        if (clockSettingsPrefab != null)
        {
            settingsPanelInstance = Instantiate(clockSettingsPrefab, canvas.transform, false);
            Transform root = settingsPanelInstance.transform;

            toggleBtn = FindChildComponent<Button>(root, "DialogPanel/EnableRow/ToggleBtn");
            enableClockBtnImg = FindChildComponent<Image>(root, "DialogPanel/EnableRow/ToggleBtn");
            enableClockBtnText = FindChildComponent<TextMeshProUGUI>(root, "DialogPanel/EnableRow/ToggleBtn/Text");

            modeRow = root.Find("DialogPanel/ModeRow")?.gameObject;
            for (int i = 0; i < 3; i++)
            {
                modeBtnImgs[i] = FindChildComponent<Image>(root, $"DialogPanel/ModeRow/ModeBtn_{i}");
                modeBtnTexts[i] = FindChildComponent<TextMeshProUGUI>(root, $"DialogPanel/ModeRow/ModeBtn_{i}/Text");
            }

            presetRow = root.Find("DialogPanel/PresetRow")?.gameObject;
            for (int i = 0; i < 6; i++)
            {
                presetBtnImgs[i] = FindChildComponent<Image>(root, $"DialogPanel/PresetRow/PresetBtn_{i}");
                presetBtnTexts[i] = FindChildComponent<TextMeshProUGUI>(root, $"DialogPanel/PresetRow/PresetBtn_{i}/Text");
            }

            customTimeContainer = root.Find("DialogPanel/CustomTimeContainer")?.gameObject;
            timeSlider = FindChildComponent<Slider>(root, "DialogPanel/CustomTimeContainer/Slider");
            customTimeValText = FindChildComponent<TextMeshProUGUI>(root, "DialogPanel/CustomTimeContainer/Value");

            incContainer = root.Find("DialogPanel/IncContainer")?.gameObject;
            incSlider = FindChildComponent<Slider>(root, "DialogPanel/IncContainer/SliderInc");
            incValText = FindChildComponent<TextMeshProUGUI>(root, "DialogPanel/IncContainer/Value");

            cancelBtn = FindChildComponent<Button>(root, "DialogPanel/ActionsRow/CancelBtn");
            saveBtn = FindChildComponent<Button>(root, "DialogPanel/ActionsRow/SaveBtn");

            if (timeSlider != null)
            {
                timeSlider.minValue = 1;
                timeSlider.maxValue = 120;
                timeSlider.wholeNumbers = true;
                timeSlider.value = Mathf.Max(1f, Mathf.Round(tempBaseTime / 60f));
            }
            if (incSlider != null)
            {
                incSlider.minValue = 0;
                incSlider.maxValue = 60;
                incSlider.wholeNumbers = true;
                incSlider.value = tempInc;
            }

            RefreshPopupUI = () =>
            {
                if (enableClockBtnImg != null)
                {
                    if (tempEnabled)
                    {
                        enableClockBtnImg.color = new Color(0.2f, 0.6f, 0.35f, 1f);
                        if (enableClockBtnText != null)
                        {
                            enableClockBtnText.text = "ENABLED";
                            enableClockBtnText.color = Color.white;
                        }
                    }
                    else
                    {
                        enableClockBtnImg.color = new Color(0.35f, 0.35f, 0.4f, 1f);
                        if (enableClockBtnText != null)
                        {
                            enableClockBtnText.text = "DISABLED";
                            enableClockBtnText.color = new Color(0.8f, 0.8f, 0.8f);
                        }
                    }
                }

                bool sectionsInteractable = tempEnabled;
                if (modeRow != null) modeRow.SetActive(sectionsInteractable);
                if (presetRow != null) presetRow.SetActive(sectionsInteractable);
                if (customTimeContainer != null) customTimeContainer.SetActive(sectionsInteractable && selectedPresetIndex == 5);
                if (incContainer != null) incContainer.SetActive(sectionsInteractable && tempMode != ClockMode.SuddenDeath);

                for (int i = 0; i < 3; i++)
                {
                    if (modeBtnImgs[i] != null)
                    {
                        if (i == (int)tempMode)
                        {
                            modeBtnImgs[i].color = new Color(0.2f, 0.55f, 0.8f, 1f);
                            if (modeBtnTexts[i] != null) modeBtnTexts[i].color = Color.white;
                        }
                        else
                        {
                            modeBtnImgs[i].color = new Color(0.18f, 0.18f, 0.22f, 1f);
                            if (modeBtnTexts[i] != null) modeBtnTexts[i].color = new Color(0.7f, 0.7f, 0.7f);
                        }
                    }
                }

                for (int i = 0; i < 6; i++)
                {
                    if (presetBtnImgs[i] != null)
                    {
                        if (i == selectedPresetIndex)
                        {
                            presetBtnImgs[i].color = new Color(0.2f, 0.55f, 0.8f, 1f);
                            if (presetBtnTexts[i] != null) presetBtnTexts[i].color = Color.white;
                        }
                        else
                        {
                            presetBtnImgs[i].color = new Color(0.18f, 0.18f, 0.22f, 1f);
                            if (presetBtnTexts[i] != null) presetBtnTexts[i].color = new Color(0.7f, 0.7f, 0.7f);
                        }
                    }
                }

                if (customTimeValText != null && timeSlider != null)
                    customTimeValText.text = $"{timeSlider.value} Min";
                if (incValText != null && incSlider != null)
                    incValText.text = $"{incSlider.value} Sec";
            };

            if (toggleBtn != null)
            {
                toggleBtn.onClick.RemoveAllListeners();
                toggleBtn.onClick.AddListener(() =>
                {
                    tempEnabled = !tempEnabled;
                    RefreshPopupUI();
                });
            }

            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                if (modeBtnImgs[i] != null)
                {
                    Button btn = modeBtnImgs[i].GetComponent<Button>();
                    if (btn != null)
                    {
                        btn.onClick.RemoveAllListeners();
                        btn.onClick.AddListener(() =>
                        {
                            tempMode = (ClockMode)idx;
                            RefreshPopupUI();
                        });
                    }
                }
            }

            for (int i = 0; i < 6; i++)
            {
                int idx = i;
                if (presetBtnImgs[i] != null)
                {
                    Button btn = presetBtnImgs[i].GetComponent<Button>();
                    if (btn != null)
                    {
                        btn.onClick.RemoveAllListeners();
                        btn.onClick.AddListener(() =>
                        {
                            selectedPresetIndex = idx;
                            if (idx < 5 && timeSlider != null)
                            {
                                tempBaseTime = presetSeconds[idx];
                                timeSlider.value = tempBaseTime / 60f;
                            }
                            RefreshPopupUI();
                        });
                    }
                }
            }

            if (timeSlider != null)
            {
                timeSlider.onValueChanged.RemoveAllListeners();
                timeSlider.onValueChanged.AddListener((val) =>
                {
                    if (selectedPresetIndex == 5)
                    {
                        tempBaseTime = val * 60f;
                    }
                    RefreshPopupUI();
                });
            }

            if (incSlider != null)
            {
                incSlider.onValueChanged.RemoveAllListeners();
                incSlider.onValueChanged.AddListener((val) =>
                {
                    tempInc = val;
                    RefreshPopupUI();
                });
            }

            if (cancelBtn != null)
            {
                cancelBtn.onClick.RemoveAllListeners();
                cancelBtn.onClick.AddListener(() =>
                {
                    Destroy(settingsPanelInstance);
                    settingsPanelInstance = null;
                });
            }

            if (saveBtn != null)
            {
                saveBtn.onClick.RemoveAllListeners();
                saveBtn.onClick.AddListener(() =>
                {
                    SaveSettings(tempEnabled, tempMode, tempBaseTime, tempInc);
                    Destroy(settingsPanelInstance);
                    settingsPanelInstance = null;
                });
            }

            RefreshPopupUI();
            return;
        }

        // PROCEDURAL FALLBACK
        TMP_FontAsset font = GetDefaultFont();

        // 1. Overlay Mask
        GameObject overlay = new GameObject("ClockSettingsOverlay", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(canvas.transform, false);
        settingsPanelInstance = overlay;

        RectTransform overlayRt = overlay.GetComponent<RectTransform>();
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = Vector2.zero;
        overlayRt.offsetMax = Vector2.zero;

        Image maskImg = overlay.GetComponent<Image>();
        maskImg.color = new Color(0f, 0f, 0f, 0.7f); // Transparent dark background

        // 2. Dialog Panel
        GameObject panel = new GameObject("DialogPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(overlay.transform, false);
        RectTransform panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(440, 480);
        panelRt.anchoredPosition = Vector2.zero;

        Image panelImg = panel.GetComponent<Image>();
        panelImg.color = new Color(0.12f, 0.12f, 0.16f, 0.98f);
        panel.AddComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 0.1f);

        // 3. Title Text
        GameObject titleGo = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
        titleGo.transform.SetParent(panel.transform, false);
        RectTransform titleRt = titleGo.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0, 1);
        titleRt.anchorMax = new Vector2(1, 1);
        titleRt.pivot = new Vector2(0.5f, 1);
        titleRt.sizeDelta = new Vector2(0, 40);
        titleRt.anchoredPosition = new Vector2(0, -20);

        TextMeshProUGUI titleTxt = titleGo.GetComponent<TextMeshProUGUI>();
        titleTxt.font = font;
        titleTxt.text = "CHESS CLOCK CONFIGURATION";
        titleTxt.fontSize = 20;
        titleTxt.fontStyle = FontStyles.Bold;
        titleTxt.alignment = TextAlignmentOptions.Center;
        titleTxt.color = Color.white;

        // Reuse local state copies and UI references
        tempEnabled = isClockEnabled;
        tempMode = clockMode;
        tempBaseTime = baseTimeSeconds;
        tempInc = incrementSeconds;

        selectedPresetIndex = -1;
        for (int i = 0; i < presetSeconds.Length; i++)
        {
            if (Mathf.Approximately(tempBaseTime, presetSeconds[i]))
                selectedPresetIndex = i;
        }
        if (selectedPresetIndex == -1) selectedPresetIndex = 5; // Custom

        customTimeContainer = null;
        incContainer = null;
        customTimeValText = null;
        incValText = null;
        enableClockBtnImg = null;
        enableClockBtnText = null;

        modeBtnImgs = new Image[3];
        modeBtnTexts = new TextMeshProUGUI[3];
        presetBtnImgs = new Image[6];
        presetBtnTexts = new TextMeshProUGUI[6];

        // --- SECTION 1: ENABLE CLOCK TOGGLE ---
        GameObject enableRow = new GameObject("EnableRow", typeof(RectTransform));
        enableRow.transform.SetParent(panel.transform, false);
        RectTransform enableRowRt = enableRow.GetComponent<RectTransform>();
        enableRowRt.anchorMin = new Vector2(0, 1);
        enableRowRt.anchorMax = new Vector2(1, 1);
        enableRowRt.pivot = new Vector2(0.5f, 1);
        enableRowRt.sizeDelta = new Vector2(-40, 50);
        enableRowRt.anchoredPosition = new Vector2(0, -70);

        GameObject enableLbl = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        enableLbl.transform.SetParent(enableRow.transform, false);
        enableLbl.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0.5f);
        enableLbl.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);
        enableLbl.GetComponent<RectTransform>().pivot = new Vector2(0, 0.5f);
        enableLbl.GetComponent<RectTransform>().anchoredPosition = new Vector2(10, 0);
        
        TextMeshProUGUI eLblTxt = enableLbl.GetComponent<TextMeshProUGUI>();
        eLblTxt.font = font;
        eLblTxt.text = "Use Match Clock:";
        eLblTxt.fontSize = 15;
        eLblTxt.color = Color.white;

        GameObject enableBtnGo = new GameObject("ToggleBtn", typeof(RectTransform), typeof(Image), typeof(Button));
        enableBtnGo.transform.SetParent(enableRow.transform, false);
        RectTransform enableBtnRt = enableBtnGo.GetComponent<RectTransform>();
        enableBtnRt.anchorMin = new Vector2(1, 0.5f);
        enableBtnRt.anchorMax = new Vector2(1, 0.5f);
        enableBtnRt.pivot = new Vector2(1, 0.5f);
        enableBtnRt.sizeDelta = new Vector2(120, 32);
        enableBtnRt.anchoredPosition = new Vector2(-10, 0);

        enableClockBtnImg = enableBtnGo.GetComponent<Image>();
        enableClockBtnText = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        enableClockBtnText.transform.SetParent(enableBtnGo.transform, false);
        enableClockBtnText.GetComponent<RectTransform>().anchorMin = Vector2.zero;
        enableClockBtnText.GetComponent<RectTransform>().anchorMax = Vector2.one;
        enableClockBtnText.GetComponent<RectTransform>().offsetMin = Vector2.zero;
        enableClockBtnText.GetComponent<RectTransform>().offsetMax = Vector2.zero;
        enableClockBtnText.font = font;
        enableClockBtnText.alignment = TextAlignmentOptions.Center;
        enableClockBtnText.fontSize = 13;
        enableClockBtnText.fontStyle = FontStyles.Bold;

        // --- SECTION 2: MODE SELECT ROW ---
        modeRow = new GameObject("ModeRow", typeof(RectTransform));
        modeRow.transform.SetParent(panel.transform, false);
        RectTransform modeRowRt = modeRow.GetComponent<RectTransform>();
        modeRowRt.anchorMin = new Vector2(0, 1);
        modeRowRt.anchorMax = new Vector2(1, 1);
        modeRowRt.pivot = new Vector2(0.5f, 1);
        modeRowRt.sizeDelta = new Vector2(-40, 60);
        modeRowRt.anchoredPosition = new Vector2(0, -130);

        GameObject modeLbl = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        modeLbl.transform.SetParent(modeRow.transform, false);
        modeLbl.GetComponent<RectTransform>().anchorMin = new Vector2(0, 1);
        modeLbl.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
        modeLbl.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1);
        modeLbl.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 20);
        modeLbl.GetComponent<RectTransform>().anchoredPosition = new Vector2(10, 0);
        
        TextMeshProUGUI mLblTxt = modeLbl.GetComponent<TextMeshProUGUI>();
        mLblTxt.font = font;
        mLblTxt.text = "Time Increment Mode:";
        mLblTxt.fontSize = 14;
        mLblTxt.color = new Color(0.8f, 0.8f, 0.8f);

        string[] modeNames = { "Sudden Death", "Fischer Inc", "Simple Delay" };
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            GameObject mBtn = new GameObject($"ModeBtn_{i}", typeof(RectTransform), typeof(Image), typeof(Button));
            mBtn.transform.SetParent(modeRow.transform, false);
            RectTransform mBtnRt = mBtn.GetComponent<RectTransform>();
            mBtnRt.anchorMin = new Vector2(i * 0.3333f, 0);
            mBtnRt.anchorMax = new Vector2((i + 1) * 0.3333f, 0);
            mBtnRt.pivot = new Vector2(0.5f, 0);
            mBtnRt.sizeDelta = new Vector2(-8, 30);
            mBtnRt.anchoredPosition = Vector2.zero;

            modeBtnImgs[i] = mBtn.GetComponent<Image>();
            
            TextMeshProUGUI mTxt = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            mTxt.transform.SetParent(mBtn.transform, false);
            mTxt.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            mTxt.GetComponent<RectTransform>().anchorMax = Vector2.one;
            mTxt.GetComponent<RectTransform>().offsetMin = Vector2.zero;
            mTxt.GetComponent<RectTransform>().offsetMax = Vector2.zero;
            mTxt.font = font;
            mTxt.alignment = TextAlignmentOptions.Center;
            mTxt.fontSize = 11;
            mTxt.fontStyle = FontStyles.Bold;
            mTxt.text = modeNames[i];
            modeBtnTexts[i] = mTxt;
        }

        // --- SECTION 3: TIME PRESETS ROW ---
        presetRow = new GameObject("PresetRow", typeof(RectTransform));
        presetRow.transform.SetParent(panel.transform, false);
        RectTransform presetRowRt = presetRow.GetComponent<RectTransform>();
        presetRowRt.anchorMin = new Vector2(0, 1);
        presetRowRt.anchorMax = new Vector2(1, 1);
        presetRowRt.pivot = new Vector2(0.5f, 1);
        presetRowRt.sizeDelta = new Vector2(-40, 60);
        presetRowRt.anchoredPosition = new Vector2(0, -200);

        GameObject presetLbl = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        presetLbl.transform.SetParent(presetRow.transform, false);
        presetLbl.GetComponent<RectTransform>().anchorMin = new Vector2(0, 1);
        presetLbl.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
        presetLbl.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1);
        presetLbl.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 20);
        presetLbl.GetComponent<RectTransform>().anchoredPosition = new Vector2(10, 0);
        
        TextMeshProUGUI pLblTxt = presetLbl.GetComponent<TextMeshProUGUI>();
        pLblTxt.font = font;
        pLblTxt.text = "Match Duration:";
        pLblTxt.fontSize = 14;
        pLblTxt.color = new Color(0.8f, 0.8f, 0.8f);

        string[] presetLabels = { "1 Min", "3 Min", "5 Min", "10 Min", "30 Min", "Custom" };
        for (int i = 0; i < 6; i++)
        {
            int idx = i;
            GameObject pBtn = new GameObject($"PresetBtn_{i}", typeof(RectTransform), typeof(Image), typeof(Button));
            pBtn.transform.SetParent(presetRow.transform, false);
            RectTransform pBtnRt = pBtn.GetComponent<RectTransform>();
            pBtnRt.anchorMin = new Vector2(i * 0.1666f, 0);
            pBtnRt.anchorMax = new Vector2((i + 1) * 0.1666f, 0);
            pBtnRt.pivot = new Vector2(0.5f, 0);
            pBtnRt.sizeDelta = new Vector2(-6, 30);
            pBtnRt.anchoredPosition = Vector2.zero;

            presetBtnImgs[i] = pBtn.GetComponent<Image>();

            TextMeshProUGUI pTxt = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            pTxt.transform.SetParent(pBtn.transform, false);
            pTxt.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            pTxt.GetComponent<RectTransform>().anchorMax = Vector2.one;
            pTxt.GetComponent<RectTransform>().offsetMin = Vector2.zero;
            pTxt.GetComponent<RectTransform>().offsetMax = Vector2.zero;
            pTxt.font = font;
            pTxt.alignment = TextAlignmentOptions.Center;
            pTxt.fontSize = 10;
            pTxt.fontStyle = FontStyles.Bold;
            pTxt.text = presetLabels[i];
            presetBtnTexts[i] = pTxt;
        }

        // --- SECTION 4: CUSTOM TIME SLIDER PANEL ---
        customTimeContainer = new GameObject("CustomTimeContainer", typeof(RectTransform));
        customTimeContainer.transform.SetParent(panel.transform, false);
        RectTransform customTimeRt = customTimeContainer.GetComponent<RectTransform>();
        customTimeRt.anchorMin = new Vector2(0, 1);
        customTimeRt.anchorMax = new Vector2(1, 1);
        customTimeRt.pivot = new Vector2(0.5f, 1);
        customTimeRt.sizeDelta = new Vector2(-40, 55);
        customTimeRt.anchoredPosition = new Vector2(0, -270);

        GameObject customTimeLbl = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        customTimeLbl.transform.SetParent(customTimeContainer.transform, false);
        customTimeLbl.GetComponent<RectTransform>().anchorMin = new Vector2(0, 1);
        customTimeLbl.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 1);
        customTimeLbl.GetComponent<RectTransform>().pivot = new Vector2(0, 1);
        customTimeLbl.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 20);
        customTimeLbl.GetComponent<RectTransform>().anchoredPosition = new Vector2(10, 0);
        
        TextMeshProUGUI ctLblTxt = customTimeLbl.GetComponent<TextMeshProUGUI>();
        ctLblTxt.font = font;
        ctLblTxt.text = "Custom Time (Minutes):";
        ctLblTxt.fontSize = 13;
        ctLblTxt.color = new Color(0.7f, 0.7f, 0.7f);

        GameObject customTimeValGo = new GameObject("Value", typeof(RectTransform), typeof(TextMeshProUGUI));
        customTimeValGo.transform.SetParent(customTimeContainer.transform, false);
        customTimeValGo.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 1);
        customTimeValGo.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
        customTimeValGo.GetComponent<RectTransform>().pivot = new Vector2(1, 1);
        customTimeValGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 20);
        customTimeValGo.GetComponent<RectTransform>().anchoredPosition = new Vector2(-10, 0);
        customTimeValText = customTimeValGo.GetComponent<TextMeshProUGUI>();
        customTimeValText.font = font;
        customTimeValText.alignment = TextAlignmentOptions.Right;
        customTimeValText.fontSize = 13;
        customTimeValText.fontStyle = FontStyles.Bold;
        customTimeValText.color = Color.white;

        // Custom Slider Component Creation
        GameObject sliderTimeGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        sliderTimeGo.transform.SetParent(customTimeContainer.transform, false);
        RectTransform sliderTimeRt = sliderTimeGo.GetComponent<RectTransform>();
        sliderTimeRt.anchorMin = new Vector2(0, 0);
        sliderTimeRt.anchorMax = new Vector2(1, 0);
        sliderTimeRt.pivot = new Vector2(0.5f, 0);
        sliderTimeRt.sizeDelta = new Vector2(-20, 20);
        sliderTimeRt.anchoredPosition = new Vector2(0, 5);

        timeSlider = sliderTimeGo.GetComponent<Slider>();
        timeSlider.minValue = 1;
        timeSlider.maxValue = 120;
        timeSlider.wholeNumbers = true;
        timeSlider.value = Mathf.Max(1f, Mathf.Round(tempBaseTime / 60f));

        GameObject trackTime = new GameObject("Background", typeof(RectTransform), typeof(Image));
        trackTime.transform.SetParent(sliderTimeGo.transform, false);
        trackTime.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0.4f);
        trackTime.GetComponent<RectTransform>().anchorMax = new Vector2(1, 0.6f);
        trackTime.GetComponent<RectTransform>().offsetMin = Vector2.zero;
        trackTime.GetComponent<RectTransform>().offsetMax = Vector2.zero;
        trackTime.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.3f, 1f);

        GameObject fillAreaTime = new GameObject("Fill Area", typeof(RectTransform));
        fillAreaTime.transform.SetParent(sliderTimeGo.transform, false);
        fillAreaTime.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0);
        fillAreaTime.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
        fillAreaTime.GetComponent<RectTransform>().offsetMin = new Vector2(5, 0);
        fillAreaTime.GetComponent<RectTransform>().offsetMax = new Vector2(-5, 0);

        GameObject fillTime = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillTime.transform.SetParent(fillAreaTime.transform, false);
        fillTime.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0.4f);
        fillTime.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.6f);
        fillTime.GetComponent<RectTransform>().offsetMin = Vector2.zero;
        fillTime.GetComponent<RectTransform>().offsetMax = Vector2.zero;
        fillTime.GetComponent<Image>().color = new Color(0.2f, 0.6f, 0.85f, 1f);
        timeSlider.fillRect = fillTime.GetComponent<RectTransform>();

        GameObject handleAreaTime = new GameObject("Handle Area", typeof(RectTransform));
        handleAreaTime.transform.SetParent(sliderTimeGo.transform, false);
        handleAreaTime.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0);
        handleAreaTime.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
        handleAreaTime.GetComponent<RectTransform>().offsetMin = new Vector2(10, 0);
        handleAreaTime.GetComponent<RectTransform>().offsetMax = new Vector2(-10, 0);

        GameObject handleTime = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleTime.transform.SetParent(handleAreaTime.transform, false);
        handleTime.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0);
        handleTime.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 1);
        handleTime.GetComponent<RectTransform>().sizeDelta = new Vector2(16, 0);
        handleTime.GetComponent<Image>().color = Color.white;
        timeSlider.handleRect = handleTime.GetComponent<RectTransform>();

        // --- SECTION 5: INCREMENT/DELAY ROW ---
        incContainer = new GameObject("IncContainer", typeof(RectTransform));
        incContainer.transform.SetParent(panel.transform, false);
        RectTransform incSettingsRt = incContainer.GetComponent<RectTransform>();
        incSettingsRt.anchorMin = new Vector2(0, 1);
        incSettingsRt.anchorMax = new Vector2(1, 1);
        incSettingsRt.pivot = new Vector2(0.5f, 1);
        incSettingsRt.sizeDelta = new Vector2(-40, 55);
        incSettingsRt.anchoredPosition = new Vector2(0, -330);

        GameObject incLblSettings = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        incLblSettings.transform.SetParent(incContainer.transform, false);
        incLblSettings.GetComponent<RectTransform>().anchorMin = new Vector2(0, 1);
        incLblSettings.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 1);
        incLblSettings.GetComponent<RectTransform>().pivot = new Vector2(0, 1);
        incLblSettings.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 20);
        incLblSettings.GetComponent<RectTransform>().anchoredPosition = new Vector2(10, 0);
        
        TextMeshProUGUI incLblTxt = incLblSettings.GetComponent<TextMeshProUGUI>();
        incLblTxt.font = font;
        incLblTxt.text = "Increment / Delay (Seconds):";
        incLblTxt.fontSize = 13;
        incLblTxt.color = new Color(0.7f, 0.7f, 0.7f);

        GameObject incValGo = new GameObject("Value", typeof(RectTransform), typeof(TextMeshProUGUI));
        incValGo.transform.SetParent(incContainer.transform, false);
        incValGo.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 1);
        incValGo.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
        incValGo.GetComponent<RectTransform>().pivot = new Vector2(1, 1);
        incValGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 20);
        incValGo.GetComponent<RectTransform>().anchoredPosition = new Vector2(-10, 0);
        incValText = incValGo.GetComponent<TextMeshProUGUI>();
        incValText.font = font;
        incValText.alignment = TextAlignmentOptions.Right;
        incValText.fontSize = 13;
        incValText.fontStyle = FontStyles.Bold;
        incValText.color = Color.white;

        // Custom Slider for Increment
        GameObject sliderIncGo = new GameObject("SliderInc", typeof(RectTransform), typeof(Slider));
        sliderIncGo.transform.SetParent(incContainer.transform, false);
        RectTransform sliderIncRt = sliderIncGo.GetComponent<RectTransform>();
        sliderIncRt.anchorMin = new Vector2(0, 0);
        sliderIncRt.anchorMax = new Vector2(1, 0);
        sliderIncRt.pivot = new Vector2(0.5f, 0);
        sliderIncRt.sizeDelta = new Vector2(-20, 20);
        sliderIncRt.anchoredPosition = new Vector2(0, 5);

        incSlider = sliderIncGo.GetComponent<Slider>();
        incSlider.minValue = 0;
        incSlider.maxValue = 60;
        incSlider.wholeNumbers = true;
        incSlider.value = tempInc;

        GameObject trackInc = new GameObject("Background", typeof(RectTransform), typeof(Image));
        trackInc.transform.SetParent(sliderIncGo.transform, false);
        trackInc.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0.4f);
        trackInc.GetComponent<RectTransform>().anchorMax = new Vector2(1, 0.6f);
        trackInc.GetComponent<RectTransform>().offsetMin = Vector2.zero;
        trackInc.GetComponent<RectTransform>().offsetMax = Vector2.zero;
        trackInc.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.3f, 1f);

        GameObject fillAreaInc = new GameObject("Fill Area", typeof(RectTransform));
        fillAreaInc.transform.SetParent(sliderIncGo.transform, false);
        fillAreaInc.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0);
        fillAreaInc.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
        fillAreaInc.GetComponent<RectTransform>().offsetMin = new Vector2(5, 0);
        fillAreaInc.GetComponent<RectTransform>().offsetMax = new Vector2(-5, 0);

        GameObject fillInc = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillInc.transform.SetParent(fillAreaInc.transform, false);
        fillInc.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0.4f);
        fillInc.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.6f);
        fillInc.GetComponent<RectTransform>().offsetMin = Vector2.zero;
        fillInc.GetComponent<RectTransform>().offsetMax = Vector2.zero;
        fillInc.GetComponent<Image>().color = new Color(0.2f, 0.6f, 0.85f, 1f);
        incSlider.fillRect = fillInc.GetComponent<RectTransform>();

        GameObject handleAreaInc = new GameObject("Handle Area", typeof(RectTransform));
        handleAreaInc.transform.SetParent(sliderIncGo.transform, false);
        handleAreaInc.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0);
        handleAreaInc.GetComponent<RectTransform>().anchorMax = new Vector2(1, 1);
        handleAreaInc.GetComponent<RectTransform>().offsetMin = new Vector2(10, 0);
        handleAreaInc.GetComponent<RectTransform>().offsetMax = new Vector2(-10, 0);

        GameObject handleInc = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleInc.transform.SetParent(handleAreaInc.transform, false);
        handleInc.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0);
        handleInc.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 1);
        handleInc.GetComponent<RectTransform>().sizeDelta = new Vector2(16, 0);
        handleInc.GetComponent<Image>().color = Color.white;
        incSlider.handleRect = handleInc.GetComponent<RectTransform>();


        // --- BOTTOM DIALOG ACTIONS BUTTONS ---
        GameObject actionsRow = new GameObject("ActionsRow", typeof(RectTransform));
        actionsRow.transform.SetParent(panel.transform, false);
        RectTransform actionsRowRt = actionsRow.GetComponent<RectTransform>();
        actionsRowRt.anchorMin = new Vector2(0, 0);
        actionsRowRt.anchorMax = new Vector2(1, 0);
        actionsRowRt.pivot = new Vector2(0.5f, 0);
        actionsRowRt.sizeDelta = new Vector2(-40, 45);
        actionsRowRt.anchoredPosition = new Vector2(0, 20);

        // Cancel Button
        GameObject cancelBtnGo = new GameObject("CancelBtn", typeof(RectTransform), typeof(Image), typeof(Button));
        cancelBtnGo.transform.SetParent(actionsRow.transform, false);
        RectTransform cancelBtnRt = cancelBtnGo.GetComponent<RectTransform>();
        cancelBtnRt.anchorMin = new Vector2(0, 0.5f);
        cancelBtnRt.anchorMax = new Vector2(0.48f, 0.5f);
        cancelBtnRt.pivot = new Vector2(0, 0.5f);
        cancelBtnRt.sizeDelta = new Vector2(0, 38);
        cancelBtnRt.anchoredPosition = Vector2.zero;

        cancelBtnGo.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.3f, 1f);
        TextMeshProUGUI cancelTxt = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        cancelTxt.transform.SetParent(cancelBtnGo.transform, false);
        cancelTxt.GetComponent<RectTransform>().anchorMin = Vector2.zero;
        cancelTxt.GetComponent<RectTransform>().anchorMax = Vector2.one;
        cancelTxt.GetComponent<RectTransform>().offsetMin = Vector2.zero;
        cancelTxt.GetComponent<RectTransform>().offsetMax = Vector2.zero;
        cancelTxt.font = font;
        cancelTxt.alignment = TextAlignmentOptions.Center;
        cancelTxt.fontSize = 13;
        cancelTxt.fontStyle = FontStyles.Bold;
        cancelTxt.text = "CANCEL";
        cancelTxt.color = Color.white;

        cancelBtnGo.GetComponent<Button>().onClick.AddListener(() =>
        {
            Destroy(settingsPanelInstance);
            settingsPanelInstance = null;
        });

        // Save Button
        GameObject saveBtnGo = new GameObject("SaveBtn", typeof(RectTransform), typeof(Image), typeof(Button));
        saveBtnGo.transform.SetParent(actionsRow.transform, false);
        RectTransform saveBtnRt = saveBtnGo.GetComponent<RectTransform>();
        saveBtnRt.anchorMin = new Vector2(0.52f, 0.5f);
        saveBtnRt.anchorMax = new Vector2(1f, 0.5f);
        saveBtnRt.pivot = new Vector2(1, 0.5f);
        saveBtnRt.sizeDelta = new Vector2(0, 38);
        saveBtnRt.anchoredPosition = Vector2.zero;

        saveBtnGo.GetComponent<Image>().color = new Color(0.2f, 0.6f, 0.35f, 1f);
        TextMeshProUGUI saveTxt = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        saveTxt.transform.SetParent(saveBtnGo.transform, false);
        saveTxt.GetComponent<RectTransform>().anchorMin = Vector2.zero;
        saveTxt.GetComponent<RectTransform>().anchorMax = Vector2.one;
        saveTxt.GetComponent<RectTransform>().offsetMin = Vector2.zero;
        saveTxt.GetComponent<RectTransform>().offsetMax = Vector2.zero;
        saveTxt.font = font;
        saveTxt.alignment = TextAlignmentOptions.Center;
        saveTxt.fontSize = 13;
        saveTxt.fontStyle = FontStyles.Bold;
        saveTxt.text = "SAVE SETTINGS";
        saveTxt.color = Color.white;

        // Reactive refresh handler for dynamic UI state updates
        RefreshPopupUI = () =>
        {
            // 1. Enable Button update
            if (tempEnabled)
            {
                enableClockBtnImg.color = new Color(0.2f, 0.6f, 0.35f, 1f);
                enableClockBtnText.text = "ENABLED";
                enableClockBtnText.color = Color.white;
            }
            else
            {
                enableClockBtnImg.color = new Color(0.35f, 0.35f, 0.4f, 1f);
                enableClockBtnText.text = "DISABLED";
                enableClockBtnText.color = new Color(0.8f, 0.8f, 0.8f);
            }

            // Dim everything else if disabled
            bool sectionsInteractable = tempEnabled;
            modeRow.SetActive(sectionsInteractable);
            presetRow.SetActive(sectionsInteractable);
            customTimeContainer.SetActive(sectionsInteractable && selectedPresetIndex == 5);
            incContainer.SetActive(sectionsInteractable && tempMode != ClockMode.SuddenDeath);

            // 2. Mode buttons highlighting
            for (int i = 0; i < 3; i++)
            {
                if (i == (int)tempMode)
                {
                    modeBtnImgs[i].color = new Color(0.2f, 0.55f, 0.8f, 1f);
                    modeBtnTexts[i].color = Color.white;
                }
                else
                {
                    modeBtnImgs[i].color = new Color(0.18f, 0.18f, 0.22f, 1f);
                    modeBtnTexts[i].color = new Color(0.7f, 0.7f, 0.7f);
                }
            }

            // 3. Preset buttons highlighting
            for (int i = 0; i < 6; i++)
            {
                if (i == selectedPresetIndex)
                {
                    presetBtnImgs[i].color = new Color(0.2f, 0.55f, 0.8f, 1f);
                    presetBtnTexts[i].color = Color.white;
                }
                else
                {
                    presetBtnImgs[i].color = new Color(0.18f, 0.18f, 0.22f, 1f);
                    presetBtnTexts[i].color = new Color(0.7f, 0.7f, 0.7f);
                }
            }

            // Labels updates
            customTimeValText.text = $"{timeSlider.value} Min";
            incValText.text = $"{incSlider.value} Sec";
        };

        // Click handlers binding
        enableBtnGo.GetComponent<Button>().onClick.AddListener(() =>
        {
            tempEnabled = !tempEnabled;
            RefreshPopupUI();
        });

        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            modeBtnImgs[i].GetComponent<Button>().onClick.AddListener(() =>
            {
                tempMode = (ClockMode)idx;
                RefreshPopupUI();
            });
        }

        for (int i = 0; i < 6; i++)
        {
            int idx = i;
            presetBtnImgs[i].GetComponent<Button>().onClick.AddListener(() =>
            {
                selectedPresetIndex = idx;
                if (idx < 5)
                {
                    tempBaseTime = presetSeconds[idx];
                    timeSlider.value = tempBaseTime / 60f;
                }
                RefreshPopupUI();
            });
        }

        timeSlider.onValueChanged.AddListener((val) =>
        {
            if (selectedPresetIndex == 5)
            {
                tempBaseTime = val * 60f;
            }
            RefreshPopupUI();
        });

        incSlider.onValueChanged.AddListener((val) =>
        {
            tempInc = val;
            RefreshPopupUI();
        });

        saveBtnGo.GetComponent<Button>().onClick.AddListener(() =>
        {
            SaveSettings(tempEnabled, tempMode, tempBaseTime, tempInc);
            Destroy(settingsPanelInstance);
            settingsPanelInstance = null;
        });

        // Initialize display state
        RefreshPopupUI();
    }
    #endregion
}
