using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TMPro;

public class GameMenu : MonoBehaviour
{
    [Header("Title menu panel")]
    public GameObject titleMenuPanel;

    [Header("Logo image")]
    public GameObject logoImage;

    [Header("Difficulty menu panel")]
    public GameObject menuPanel;

    [Header("Difficulty back button layer")]
    public GameObject backButtonLayer;

    [Header("Settings panel")]
    public GameObject settingsPanel;

    [Header("Pause menu")]
    public PauseMenuManager pauseMenuManager;
    public GameObject pauseMenuPanel;

    [Header("Undo/Hint UI elements")]
    public GameObject[] easyModeOnlyUI;

    [Header("Pause button")]
    public GameObject pauseButton;

    [Header("Logout button")]
    public GameObject logoutButton;

    [Header("Login scene")]
    public string loginSceneName = "HomeUI";

    private void Start()
    {
        AutoPopulateEasyModeUI();

        if (logoutButton == null)
        {
            // Auto-search for manually created logout button in the scene
            GameObject foundBtn = GameObject.Find("Btn_Logout");
            if (foundBtn == null) foundBtn = GameObject.Find("Logout");
            if (foundBtn == null) foundBtn = GameObject.Find("LogoutButton");

            if (foundBtn != null)
            {
                logoutButton = foundBtn;
            }
        }

        if (logoutButton != null)
        {
            Button btnComp = logoutButton.GetComponent<Button>();
            if (btnComp != null)
            {
                btnComp.onClick.RemoveAllListeners();
                btnComp.onClick.AddListener(Logout);
            }
        }
        else
        {
            Debug.LogWarning("Logout button not found in scene and not assigned in Inspector.");
        }

        bool isOnline = SocketManager.Instance != null && SocketManager.Instance.isOnlineMode;

        if (titleMenuPanel != null)
        {
            titleMenuPanel.SetActive(!isOnline);
        }

        if (menuPanel != null)
        {
            menuPanel.SetActive(false);
        }

        if (logoImage != null)
        {
            logoImage.SetActive(!isOnline);
            if (!isOnline) BringLogoToFront();
        }

        if (backButtonLayer != null)
        {
            backButtonLayer.SetActive(false);
        }

        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
        }

        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(false);
        }

        if (pauseButton != null)
        {
            pauseButton.SetActive(isOnline);
        }

        if (logoutButton != null)
        {
            logoutButton.SetActive(true);
        }

        if (pauseMenuManager != null)
        {
            pauseMenuManager.SetPauseInputEnabled(isOnline);
        }

        // Hide easy mode UI elements (hints/undo) if online
        if (isOnline && easyModeOnlyUI != null)
        {
            foreach (GameObject uiElement in easyModeOnlyUI)
            {
                if (uiElement != null) uiElement.SetActive(false);
            }
        }

        GameMenuUiSafety.PreparePanel(titleMenuPanel);
        BringLogoToFront();
        PlaceLogoutButtonWithMenu();

        Time.timeScale = 1f;

        // Auto restart directly into gameplay if flagged
        if (PlayerPrefs.GetInt("AutoStartGame", 0) == 1)
        {
            PlayerPrefs.DeleteKey("AutoStartGame");
            PlayerPrefs.Save();
            int savedDifficulty = PlayerPrefs.GetInt("Difficulty", 1);
            SelectDifficultyAndStart(savedDifficulty);
        }

        // Auto open online lobby if flagged (multiplayer restart)
        if (PlayerPrefs.GetInt("AutoOpenOnlineLobby", 0) == 1)
        {
            PlayerPrefs.DeleteKey("AutoOpenOnlineLobby");
            PlayerPrefs.Save();
            PlayOnline();
        }

        // Auto rematch in online mode if flagged
        if (PlayerPrefs.GetInt("AutoRematchPending", 0) == 1)
        {
            PlayerPrefs.DeleteKey("AutoRematchPending");
            PlayerPrefs.Save();
            if (SocketManager.Instance != null)
            {
                SocketManager.Instance.SendPlayerReadyForRematch();
            }
        }
    }

    public void ShowDifficultyMenu()
    {
        ClearUiSelection();

        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(false);
        }

        if (pauseButton != null)
        {
            pauseButton.SetActive(false);
        }

        if (pauseMenuManager != null)
        {
            pauseMenuManager.SetPauseInputEnabled(false);
        }

        if (titleMenuPanel != null)
        {
            titleMenuPanel.SetActive(false);
        }

        if (menuPanel != null)
        {
            menuPanel.SetActive(true);
        }

        if (logoImage != null)
        {
            logoImage.SetActive(false);
        }

        if (backButtonLayer != null)
        {
            backButtonLayer.SetActive(true);
        }

        if (logoutButton != null)
        {
            logoutButton.SetActive(true);
        }

        GameMenuUiSafety.PreparePanel(menuPanel);
        BringBackButtonLayerToFront();
        PlaceLogoutButtonWithMenu();
    }

    public void BackToMainMenuFromDifficultyMenu()
    {
        ClearUiSelection();

        if (menuPanel != null) menuPanel.SetActive(false);
        if (logoImage != null)
        {
            logoImage.SetActive(true);
            BringLogoToFront();
        }
        if (backButtonLayer != null) backButtonLayer.SetActive(false);
        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        if (pauseButton != null) pauseButton.SetActive(false);
        if (pauseMenuManager != null) pauseMenuManager.SetPauseInputEnabled(false);
        if (titleMenuPanel != null) titleMenuPanel.SetActive(true);
        if (logoutButton != null) logoutButton.SetActive(true);

        GameMenuUiSafety.PreparePanel(titleMenuPanel);
        BringLogoToFront();
        PlaceLogoutButtonWithMenu();
    }

    public void OpenSettings()
    {
        ClearUiSelection();

        if (titleMenuPanel != null)
        {
            titleMenuPanel.SetActive(false);
        }

        if (menuPanel != null)
        {
            menuPanel.SetActive(false);
        }

        if (backButtonLayer != null)
        {
            backButtonLayer.SetActive(false);
        }

        if (logoImage != null)
        {
            logoImage.SetActive(false);
        }

        if (settingsPanel != null)
        {
            settingsPanel.SetActive(true);
        }
    }

    public void CloseSettings()
    {
        ClearUiSelection();

        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
        }

        if (logoImage != null)
        {
            logoImage.SetActive(true);
            BringLogoToFront();
        }

        if (titleMenuPanel != null)
        {
            titleMenuPanel.SetActive(true);
        }
    }

    public void OpenClockSettings()
    {
        if (ChessClock.Instance != null)
        {
            ChessClock.Instance.OpenSettingsPopup();
        }
    }

    public void QuitGameFromTitle()
    {
        Debug.Log("Quit game from title menu...");
        Application.Quit();
    }

    public void Logout()
    {
        AccountManager.Instance.Logout();

        PlayerPrefs.DeleteKey("loggedInUser");
        PlayerPrefs.Save();

        SceneManager.LoadScene(loginSceneName);
    }

    public void SelectDifficultyAndStart(int difficultyLevel)
    {
        PlayerPrefs.SetInt("Difficulty", difficultyLevel);
        PlayerPrefs.Save();

        ClearUiSelection();

        if (menuPanel != null) menuPanel.SetActive(false);
        if (titleMenuPanel != null) titleMenuPanel.SetActive(false);
        if (logoImage != null) logoImage.SetActive(false);
        if (backButtonLayer != null) backButtonLayer.SetActive(false);
        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        if (pauseButton != null) pauseButton.SetActive(true);
        if (pauseMenuManager != null) pauseMenuManager.SetPauseInputEnabled(true);
        if (logoutButton != null) logoutButton.SetActive(false);

        bool isEasyMode = difficultyLevel == 1;
        foreach (GameObject uiElement in easyModeOnlyUI)
        {
            if (uiElement != null)
            {
                uiElement.SetActive(isEasyMode);
            }
        }

        // Set game mode to AI (offline)
        if (GameManager.Instance != null)
        {
            GameManager.Instance.currentGameMode = GameMode.AI;
            GameManager.Instance.UpdatePieceColliders();
        }

        GameMenuUiSafety.RestoreFullScreenBlockers();
    }

    /// <summary>
    /// Called when "Play Online" is clicked. Opens the Online Lobby UI.
    /// </summary>
    public void PlayOnline()
    {
        ClearUiSelection();

        // Hide all menus
        if (titleMenuPanel != null) titleMenuPanel.SetActive(false);
        if (menuPanel != null) menuPanel.SetActive(false);
        if (logoImage != null) logoImage.SetActive(false);
        if (backButtonLayer != null) backButtonLayer.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (logoutButton != null) logoutButton.SetActive(false);

        // Ensure SocketManager exists
        if (SocketManager.Instance == null)
        {
            GameObject smGo = new GameObject("SocketManager");
            smGo.AddComponent<SocketManager>();
        }

        // Ensure OnlineLobbyUI exists
        if (OnlineLobbyUI.Instance == null)
        {
            GameObject lobbyGo = new GameObject("OnlineLobbyUI");
            lobbyGo.AddComponent<OnlineLobbyUI>();
        }

        OnlineLobbyUI.Instance.ShowLobby();
    }

    private void PlaceLogoutButtonWithMenu()
    {
        // Preserve manually configured parent hierarchy and layout in Unity Editor.
    }

    private void BringBackButtonLayerToFront()
    {
        if (backButtonLayer == null)
        {
            return;
        }

        if (!backButtonLayer.activeSelf)
        {
            backButtonLayer.SetActive(true);
        }

        backButtonLayer.transform.SetAsLastSibling();
    }

    private void BringLogoToFront()
    {
        if (logoImage == null)
        {
            return;
        }

        Transform logoRoot = logoImage.transform.parent != null ? logoImage.transform.parent : logoImage.transform;

        if (logoRoot != null && !logoRoot.gameObject.activeSelf)
        {
            logoRoot.gameObject.SetActive(true);
        }

        if (!logoImage.activeSelf)
        {
            logoImage.SetActive(true);
        }

        if (logoRoot != null)
        {
            logoRoot.SetAsLastSibling();
        }
    }

    // Dynamic creation removed.

    private void AutoPopulateEasyModeUI()
    {
        if (easyModeOnlyUI == null || easyModeOnlyUI.Length == 0)
        {
            var foundUI = new System.Collections.Generic.List<GameObject>();
            Transform hintT = FindChildDeep(transform, "Btn_Hint");
            if (hintT == null) hintT = FindChildDeep(transform, "Hint");
            if (hintT == null) hintT = FindChildDeep(transform, "HintButton");
            if (hintT != null) foundUI.Add(hintT.gameObject);

            Transform undoT = FindChildDeep(transform, "Btn_Undo");
            if (undoT == null) undoT = FindChildDeep(transform, "Undo");
            if (undoT == null) undoT = FindChildDeep(transform, "UndoButton");
            if (undoT != null) foundUI.Add(undoT.gameObject);

            if (foundUI.Count > 0)
            {
                easyModeOnlyUI = foundUI.ToArray();
                Debug.Log($"[GameMenu] Auto-populated easyModeOnlyUI with {easyModeOnlyUI.Length} elements.");
            }
        }
    }

    private Transform FindChildDeep(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            Transform found = FindChildDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static void ClearUiSelection()
    {
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
