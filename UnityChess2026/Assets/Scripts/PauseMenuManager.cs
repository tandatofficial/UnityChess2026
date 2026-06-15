using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class PauseMenuManager : MonoBehaviour
{
    [Header("Pause menu panel")]
    public GameObject pauseMenuPanel;

    [Header("Return-to-main-menu confirmation panel")]
    public GameObject returnToMainMenuConfirmPanel;

    private bool pauseInputEnabled = true;
    private bool isPaused = false;
    private bool isReturnConfirmVisible = false;

    private enum ConfirmAction
    {
        None,
        ReturnToMainMenu,
        QuitGame
    }
    private ConfirmAction currentConfirmAction = ConfirmAction.None;

    private void Start()
    {
        EnsureConfirmPanelOverlay();

        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(false);
        }

        if (returnToMainMenuConfirmPanel != null)
        {
            returnToMainMenuConfirmPanel.SetActive(false);
        }

        Time.timeScale = 1f;
        isPaused = false;
        isReturnConfirmVisible = false;
    }

    private void EnsureConfirmPanelOverlay()
    {
        if (returnToMainMenuConfirmPanel == null)
        {
            return;
        }

        // Reparent to Canvas level if needed so it overlays everything
        if (pauseMenuPanel != null && returnToMainMenuConfirmPanel.transform.parent == pauseMenuPanel.transform)
        {
            returnToMainMenuConfirmPanel.transform.SetParent(pauseMenuPanel.transform.parent, false);
        }

        returnToMainMenuConfirmPanel.transform.SetAsLastSibling();

        // 1. Setup fullscreen dark overlay background
        var rectTransform = returnToMainMenuConfirmPanel.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.localScale = Vector3.one;
        }

        var overlayImage = returnToMainMenuConfirmPanel.GetComponent<Image>();
        if (overlayImage != null)
        {
            overlayImage.color = new Color(0f, 0f, 0f, 0.75f); // Clean, dark, semi-transparent overlay
        }

        // 2. Locate or Create Dialog Card GameObject
        Transform cardTransform = returnToMainMenuConfirmPanel.transform.Find("DialogCard");
        GameObject cardGo;
        if (cardTransform == null)
        {
            cardGo = new GameObject("DialogCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            cardGo.transform.SetParent(returnToMainMenuConfirmPanel.transform, false);
            cardTransform = cardGo.transform;
        }
        else
        {
            cardGo = cardTransform.gameObject;
        }

        // Style the Dialog Card
        var cardRect = cardGo.GetComponent<RectTransform>();
        if (cardRect != null)
        {
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(380f, 170f); // Sleek and compact card size
            cardRect.anchoredPosition = Vector2.zero;
            cardRect.localScale = Vector3.one;
        }

        var cardImage = cardGo.GetComponent<Image>();
        if (cardImage != null)
        {
            // Modern glassmorphism look (smooth dark grey card)
            cardImage.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);
            cardImage.type = Image.Type.Simple;
        }

        // 3. Locate and Reparent buttons to Card
        var yesButton = FindConfirmButton("Yes");
        var noButton = FindConfirmButton("No");

        if (yesButton != null && yesButton.transform.parent == returnToMainMenuConfirmPanel.transform)
        {
            yesButton.transform.SetParent(cardTransform, false);
        }
        if (noButton != null && noButton.transform.parent == returnToMainMenuConfirmPanel.transform)
        {
            noButton.transform.SetParent(cardTransform, false);
        }

        // 4. Locate or Create Title Text inside Card
        Transform titleTransform = cardTransform.Find("ConfirmTitleText");
        GameObject titleGo;
        if (titleTransform == null)
        {
            titleGo = new GameObject("ConfirmTitleText", typeof(RectTransform), typeof(CanvasRenderer));
            titleGo.transform.SetParent(cardTransform, false);
            titleTransform = titleGo.transform;
        }
        else
        {
            titleGo = titleTransform.gameObject;
        }

        var titleText = titleGo.GetComponent<TextMeshProUGUI>();
        if (titleText == null)
        {
            titleText = titleGo.AddComponent<TextMeshProUGUI>();
        }

        // Copy font from buttons to match styling
        if (yesButton != null)
        {
            var btnText = yesButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (btnText != null && btnText.font != null)
            {
                titleText.font = btnText.font;
            }
        }

        var titleRect = titleGo.GetComponent<RectTransform>();
        if (titleRect != null)
        {
            titleRect.anchorMin = new Vector2(0f, 0.5f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.sizeDelta = new Vector2(-40f, -20f); // Balanced padding inside card
            titleRect.anchoredPosition = new Vector2(0f, 5f);
        }

        titleText.alignment = TextAlignmentOptions.Center;
        titleText.enableAutoSizing = true;
        titleText.fontSizeMin = 16f;
        titleText.fontSizeMax = 20f;
        titleText.color = Color.white;
        titleText.text = "Are you sure you want to proceed?";

        // 5. Position buttons inside the card
        if (yesButton != null)
        {
            ConfigureConfirmButton(yesButton, new Vector2(-90f, 35f)); // Clean side-by-side positioning
        }
        if (noButton != null)
        {
            ConfigureConfirmButton(noButton, new Vector2(90f, 35f));
        }
    }

    private Button FindConfirmButton(string buttonName)
    {
        if (returnToMainMenuConfirmPanel == null)
        {
            return null;
        }

        var namedTransform = returnToMainMenuConfirmPanel.transform.Find(buttonName);
        if (namedTransform != null)
        {
            return namedTransform.GetComponent<Button>();
        }

        // Check inside DialogCard
        Transform cardTransform = returnToMainMenuConfirmPanel.transform.Find("DialogCard");
        if (cardTransform != null)
        {
            var cardNamedTransform = cardTransform.Find(buttonName);
            if (cardNamedTransform != null)
            {
                return cardNamedTransform.GetComponent<Button>();
            }
        }

        var buttons = returnToMainMenuConfirmPanel.GetComponentsInChildren<Button>(true);
        foreach (var button in buttons)
        {
            if (button != null && button.gameObject.name == buttonName)
            {
                return button;
            }
        }

        return null;
    }

    private static void ConfigureConfirmButton(Button button, Vector2 anchoredPosition)
    {
        if (button == null)
        {
            return;
        }

        var rectTransform = button.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.anchorMin = new Vector2(0.5f, 0f);
            rectTransform.anchorMax = new Vector2(0.5f, 0f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.sizeDelta = new Vector2(140f, 40f); // Sleek button dimensions
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.localScale = Vector3.one;
        }

        var image = button.GetComponent<Image>();
        if (image != null)
        {
            image.preserveAspect = true;
            // Differentiate YES/NO colors for better visual hierarchy
            if (button.gameObject.name == "Yes")
            {
                image.color = new Color(0.82f, 0.3f, 0.3f, 1f); // Accent Coral Red
            }
            else
            {
                image.color = new Color(0.25f, 0.25f, 0.25f, 1f); // Neutral Gray
            }
        }

        var text = button.GetComponentInChildren<TMP_Text>(true);
        if (text != null)
        {
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = 16f;
            text.fontSizeMax = 20f;
            text.color = Color.white;
            text.fontStyle = FontStyles.Bold;
        }
    }

    public void SetPauseInputEnabled(bool enabled)
    {
        pauseInputEnabled = enabled;

        if (!pauseInputEnabled && isPaused)
        {
            ResumeGame();
        }

        if (!pauseInputEnabled && isReturnConfirmVisible)
        {
            CancelReturnToMainMenu();
        }
    }

    private void Update()
    {
        if (!pauseInputEnabled)
        {
            return;
        }

        if (isReturnConfirmVisible)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelReturnToMainMenu();
            }

            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isPaused)
            {
                ResumeGame();
            }
            else
            {
                PauseGame();
            }
        }
    }

    public void PauseGame()
    {
        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(true);
        }

        if (returnToMainMenuConfirmPanel != null)
        {
            returnToMainMenuConfirmPanel.SetActive(false);
        }

        Time.timeScale = 0f;
        isPaused = true;
        isReturnConfirmVisible = false;
    }

    public void ResumeGame()
    {
        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(false);
        }

        if (returnToMainMenuConfirmPanel != null)
        {
            returnToMainMenuConfirmPanel.SetActive(false);
        }

        Time.timeScale = 1f;
        isPaused = false;
        isReturnConfirmVisible = false;
    }

    private void ShowConfirmationDialog(string message)
    {
        // Hide standard pause panel elements to avoid visual overlap
        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(false);
        }

        if (returnToMainMenuConfirmPanel != null)
        {
            EnsureConfirmPanelOverlay();

            // Find dynamically created title text and update its message
            Transform cardTransform = returnToMainMenuConfirmPanel.transform.Find("DialogCard");
            if (cardTransform != null)
            {
                Transform titleTransform = cardTransform.Find("ConfirmTitleText");
                if (titleTransform != null)
                {
                    var titleText = titleTransform.GetComponent<TextMeshProUGUI>();
                    if (titleText != null)
                    {
                        titleText.text = message;
                    }
                }
            }

            returnToMainMenuConfirmPanel.SetActive(true);
        }

        Time.timeScale = 0f;
        isPaused = true;
        isReturnConfirmVisible = true;
        ClearUiSelection();
    }

    public void RequestReturnToMainMenu()
    {
        currentConfirmAction = ConfirmAction.ReturnToMainMenu;
        ShowConfirmationDialog("Are you sure you want to return to the Main Menu?");
    }

    public void QuitGame()
    {
        currentConfirmAction = ConfirmAction.QuitGame;
        ShowConfirmationDialog("Are you sure you want to quit the game?");
    }

    public void CancelReturnToMainMenu()
    {
        if (returnToMainMenuConfirmPanel != null)
        {
            returnToMainMenuConfirmPanel.SetActive(false);
        }

        // Restore main pause panel
        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(true);
        }

        isReturnConfirmVisible = false;
        currentConfirmAction = ConfirmAction.None;
        ClearUiSelection();
    }

    public void ConfirmReturnToMainMenu()
    {
        Time.timeScale = 1f;

        // If in an online game, cleanly disconnect before switching scenes
        if (GameManager.Instance != null && GameManager.Instance.currentGameMode == GameMode.Online)
        {
            if (SocketManager.Instance != null)
            {
                SocketManager.Instance.Disconnect();
            }
        }

        if (currentConfirmAction == ConfirmAction.QuitGame)
        {
            Debug.Log("Quitting game...");
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
        else
        {
            // Default: reload current scene to restart / return to main menu
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    private static void ClearUiSelection()
    {
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
