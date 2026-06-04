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
            logoutButton = CreateLogoutButtonDynamically();
        }
        else
        {
            Button btnComp = logoutButton.GetComponent<Button>();
            if (btnComp != null)
            {
                btnComp.onClick.RemoveAllListeners();
                btnComp.onClick.AddListener(Logout);
            }
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

    private GameObject CreateLogoutButtonDynamically()
    {
        if (titleMenuPanel == null) return null;

        // Find a template button inside titleMenuPanel
        Transform templateButtonTransform = titleMenuPanel.transform.Find("Btn_Settings");
        if (templateButtonTransform == null)
            templateButtonTransform = titleMenuPanel.transform.Find("Btn_Play");
        
        if (templateButtonTransform == null)
        {
            Button btn = titleMenuPanel.GetComponentInChildren<Button>(true);
            if (btn != null)
                templateButtonTransform = btn.transform;
        }

        if (templateButtonTransform == null)
        {
            Debug.LogError("Could not find any template button to clone for Logout button!");
            return null;
        }

        // Clone the template
        GameObject logoutBtnGo = Instantiate(templateButtonTransform.gameObject);
        logoutBtnGo.name = "Btn_Logout";

        // Update Text
        TMP_Text tmpText = logoutBtnGo.GetComponentInChildren<TMP_Text>(true);
        if (tmpText != null)
        {
            tmpText.text = "Logout";
        }
        else
        {
            Text legacyText = logoutBtnGo.GetComponentInChildren<Text>(true);
            if (legacyText != null)
            {
                legacyText.text = "Logout";
            }
        }

        // Update button click callback
        Button buttonComp = logoutBtnGo.GetComponent<Button>();
        if (buttonComp != null)
        {
            buttonComp.onClick.RemoveAllListeners();
            buttonComp.onClick.AddListener(Logout);
        }

        // Configure Layout / RectTransform to be at the bottom center of the screen
        RectTransform rectTransform = logoutBtnGo.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            // Set anchors to bottom-center
            rectTransform.anchorMin = new Vector2(0.5f, 0f);
            rectTransform.anchorMax = new Vector2(0.5f, 0f);
            rectTransform.pivot = new Vector2(0.5f, 0f);
            
            // Set size and position (50 units from bottom)
            rectTransform.sizeDelta = new Vector2(200f, 50f);
            rectTransform.anchoredPosition = new Vector2(0f, 350f);
            rectTransform.localScale = Vector3.one;
        }

        return logoutBtnGo;
    }

    private static void ClearUiSelection()
    {
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
