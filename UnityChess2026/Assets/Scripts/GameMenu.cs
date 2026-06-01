using System;
using UnityEngine;
using UnityEngine.EventSystems;

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

    [Header("Pause menu")]
    public PauseMenuManager pauseMenuManager;
    public GameObject pauseMenuPanel;

    [Header("Undo/Hint UI elements")]
    public GameObject[] easyModeOnlyUI;

    [Header("Pause button")]
    public GameObject pauseButton;

    private void Start()
    {
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
    }

    public void BackToMainMenuFromDifficultyMenu()
    {
        ClearUiSelection();

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
            titleMenuPanel.SetActive(true);
        }
    }

    public void OpenSettings()
    {
        Debug.Log("Open settings menu (placeholder)");
    }

    public void QuitGameFromTitle()
    {
        Debug.Log("Quit game from title menu...");
        Application.Quit();
    }

    public void SelectDifficultyAndStart(int difficultyLevel)
    {
        PlayerPrefs.SetInt("Difficulty", difficultyLevel);
        PlayerPrefs.Save();

        ClearUiSelection();

        if (menuPanel != null)
        {
            menuPanel.SetActive(false);
        }

        if (titleMenuPanel != null)
        {
            titleMenuPanel.SetActive(false);
        }

        if (logoImage != null)
        {
            logoImage.SetActive(false);
        }

        if (backButtonLayer != null)
        {
            backButtonLayer.SetActive(false);
        }

        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(false);
        }

        if (pauseButton != null)
        {
            pauseButton.SetActive(true);
        }

        if (pauseMenuManager != null)
        {
            pauseMenuManager.SetPauseInputEnabled(true);
        }

        bool isEasyMode = difficultyLevel == 1;
        foreach (GameObject uiElement in easyModeOnlyUI)
        {
            if (uiElement != null)
            {
                uiElement.SetActive(isEasyMode);
            }
        }

        GameManager.Instance.UpdatePieceColliders();
    }

    private static void ClearUiSelection()
    {
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

}
