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

        if (pauseMenuPanel != null && returnToMainMenuConfirmPanel.transform.parent == pauseMenuPanel.transform)
        {
            returnToMainMenuConfirmPanel.transform.SetParent(pauseMenuPanel.transform.parent, false);
        }

        returnToMainMenuConfirmPanel.transform.SetAsLastSibling();

        var rectTransform = returnToMainMenuConfirmPanel.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.localScale = Vector3.one;
        }

        ApplyConfirmPanelLayout();
    }

    private void ApplyConfirmPanelLayout()
    {
        if (returnToMainMenuConfirmPanel == null)
        {
            return;
        }

        var layoutGroups = returnToMainMenuConfirmPanel.GetComponentsInChildren<LayoutGroup>(true);
        foreach (var layoutGroup in layoutGroups)
        {
            layoutGroup.enabled = false;
        }

        var contentFitters = returnToMainMenuConfirmPanel.GetComponentsInChildren<ContentSizeFitter>(true);
        foreach (var fitter in contentFitters)
        {
            fitter.enabled = false;
        }

        var panelRect = returnToMainMenuConfirmPanel.GetComponent<RectTransform>();
        if (panelRect != null)
        {
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(460f, 220f);
            panelRect.anchoredPosition = new Vector2(0f, 0f);
            panelRect.localScale = Vector3.one;
        }

        var titleText = FindConfirmTitle();
        if (titleText != null)
        {
            var titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(380f, 70f);
            titleRect.anchoredPosition = new Vector2(0f, -22f);
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.enableAutoSizing = true;
            titleText.fontSizeMin = 18f;
            titleText.fontSizeMax = 28f;
        }

        var yesButton = FindConfirmButton("Yes");
        var noButton = FindConfirmButton("No");

        ConfigureConfirmButton(yesButton, new Vector2(-110f, -120f));
        ConfigureConfirmButton(noButton, new Vector2(110f, -120f));
    }

    private TMP_Text FindConfirmTitle()
    {
        if (returnToMainMenuConfirmPanel == null)
        {
            return null;
        }

        var texts = returnToMainMenuConfirmPanel.GetComponentsInChildren<TMP_Text>(true);
        foreach (var text in texts)
        {
            if (text != null && text.gameObject.name == "Text (TMP)" && text.text.Contains("Go back to main menu"))
            {
                return text;
            }
        }

        return null;
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
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.sizeDelta = new Vector2(160f, 44f);
            rectTransform.anchoredPosition = anchoredPosition;
        }

        var image = button.GetComponent<Image>();
        if (image != null)
        {
            image.preserveAspect = true;
        }

        var text = button.GetComponentInChildren<TMP_Text>(true);
        if (text != null)
        {
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = 18f;
            text.fontSizeMax = 24f;
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

    public void RequestReturnToMainMenu()
    {
        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(true);
        }

        if (returnToMainMenuConfirmPanel != null)
        {
            returnToMainMenuConfirmPanel.SetActive(true);
        }

        Time.timeScale = 0f;
        isPaused = true;
        isReturnConfirmVisible = true;
        ClearUiSelection();
    }

    public void CancelReturnToMainMenu()
    {
        if (returnToMainMenuConfirmPanel != null)
        {
            returnToMainMenuConfirmPanel.SetActive(false);
        }

        isReturnConfirmVisible = false;
        ClearUiSelection();
    }

    public void ConfirmReturnToMainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void QuitGame()
    {
        Debug.Log("Quitting game...");
        Application.Quit();
    }

    private static void ClearUiSelection()
    {
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
