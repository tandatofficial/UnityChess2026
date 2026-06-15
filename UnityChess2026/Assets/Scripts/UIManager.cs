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

    private void Start()
    {
        if (SocketManager.Instance != null)
        {
            SocketManager.Instance.OnOpponentRequestedRematch += HandleOpponentRequestedRematch;
            SocketManager.Instance.OnRematchAgreed += HandleRematchAgreed;
        }
    }

    private void OnDestroy()
    {
        if (SocketManager.Instance != null)
        {
            SocketManager.Instance.OnOpponentRequestedRematch -= HandleOpponentRequestedRematch;
            SocketManager.Instance.OnRematchAgreed -= HandleRematchAgreed;
        }
    }

    private void HandleOpponentRequestedRematch()
    {
        if (gameOverText != null)
        {
            string msg = "\n(Đối thủ muốn đấu lại!)";
            if (!gameOverText.text.Contains(msg))
            {
                gameOverText.text += msg;
            }
        }
    }

    private void HandleRematchAgreed()
    {
        Time.timeScale = 1f;
        PlayerPrefs.SetInt("AutoRematchPending", 1);
        PlayerPrefs.DeleteKey("AutoOpenOnlineLobby");
        PlayerPrefs.DeleteKey("AutoStartGame");
        PlayerPrefs.Save();

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
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

        bool isOnline = GameManager.Instance != null && GameManager.Instance.currentGameMode == GameMode.Online;

        if (isOnline && SocketManager.Instance != null && SocketManager.Instance.IsConnected)
        {
            SocketManager.Instance.RequestRematch();

            if (restartButton != null)
            {
                restartButton.interactable = false;
                var btnText = restartButton.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null)
                {
                    btnText.text = "Đang chờ đối thủ...";
                }
                else
                {
                    var legacyText = restartButton.GetComponentInChildren<Text>();
                    if (legacyText != null) legacyText.text = "Đang chờ đối thủ...";
                }
            }
            return;
        }

        if (SocketManager.Instance != null && SocketManager.Instance.IsConnected)
        {
            SocketManager.Instance.Disconnect();
        }

        if (isOnline)
        {
            PlayerPrefs.SetInt("AutoOpenOnlineLobby", 1);
            PlayerPrefs.DeleteKey("AutoStartGame");
        }
        else
        {
            PlayerPrefs.SetInt("AutoStartGame", 1);
            PlayerPrefs.DeleteKey("AutoOpenOnlineLobby");
        }
        PlayerPrefs.Save();

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
