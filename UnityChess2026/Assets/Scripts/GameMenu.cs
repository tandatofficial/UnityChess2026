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

        if (titleMenuPanel != null)
        {
            titleMenuPanel.SetActive(true);
        }

        if (menuPanel != null)
        {
            menuPanel.SetActive(false);
        }

        if (logoImage != null)
        {
            logoImage.SetActive(true);
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
            pauseButton.SetActive(false);
        }

        if (logoutButton != null)
        {
            logoutButton.SetActive(true);
        }

        if (pauseMenuManager != null)
        {
            pauseMenuManager.SetPauseInputEnabled(false);
        }

        GameMenuUiSafety.PreparePanel(titleMenuPanel);
        PlaceLogoutButtonWithMenu();

        Time.timeScale = 1f;
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
        PlaceLogoutButtonWithMenu();
    }

    public void BackToMainMenuFromDifficultyMenu()
    {
        ClearUiSelection();

        if (menuPanel != null) menuPanel.SetActive(false);
        if (logoImage != null) logoImage.SetActive(true);
        if (backButtonLayer != null) backButtonLayer.SetActive(false);
        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        if (pauseButton != null) pauseButton.SetActive(false);
        if (pauseMenuManager != null) pauseMenuManager.SetPauseInputEnabled(false);
        if (titleMenuPanel != null) titleMenuPanel.SetActive(true);
        if (logoutButton != null) logoutButton.SetActive(true);

        GameMenuUiSafety.PreparePanel(titleMenuPanel);
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
        }

        if (titleMenuPanel != null)
        {
            titleMenuPanel.SetActive(true);
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

        if (GameManager.Instance != null)
        {
            GameManager.Instance.UpdatePieceColliders();
        }

        GameMenuUiSafety.RestoreFullScreenBlockers();
    }

    private void PlaceLogoutButtonWithMenu()
    {
        if (logoutButton == null) return;

        Transform targetParent = null;
        if (titleMenuPanel != null && titleMenuPanel.transform.parent != null)
            targetParent = titleMenuPanel.transform.parent;
        else if (menuPanel != null && menuPanel.transform.parent != null)
            targetParent = menuPanel.transform.parent;

        if (targetParent != null && logoutButton.transform.parent != targetParent)
        {
            logoutButton.transform.SetParent(targetParent, false);
        }

        logoutButton.transform.SetAsLastSibling();
    }

    // Dynamic creation removed.

    private static void ClearUiSelection()
    {
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
