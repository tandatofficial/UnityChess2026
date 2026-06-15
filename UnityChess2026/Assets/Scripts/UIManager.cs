using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance;

    [Header("Game Over Panel")]
    public GameObject gameOverPanel;
    public TextMeshProUGUI gameOverText;
    public Button restartButton;

    [Header("User Panel")]
    public GameObject userPanel;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);

        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);

        if (restartButton != null)
        {
            restartButton.onClick.RemoveAllListeners();
            restartButton.onClick.AddListener(RestartGame);
        }
    }

    public void HideUserPanel()
    {
        if (userPanel != null)
        {
            userPanel.SetActive(false);
        }
    }

    public void ShowWinScreen(PieceColor winner)
    {
        if (gameOverPanel != null)
            gameOverPanel.SetActive(true);

        if (gameOverText != null)
            gameOverText.text = winner == PieceColor.White ? "Trắng Thắng!" : "Đen Thắng!";
    }

    public void ShowDrawScreen(string reason)
    {
        if (gameOverPanel != null)
            gameOverPanel.SetActive(true);

        if (gameOverText != null)
            gameOverText.text = $"Hòa: {reason}";
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;

        if (SocketManager.Instance != null && SocketManager.Instance.IsConnected)
        {
            SocketManager.Instance.Disconnect();
        }

        PlayerPrefs.SetInt("AutoStartGame", 1);
        PlayerPrefs.Save();

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
