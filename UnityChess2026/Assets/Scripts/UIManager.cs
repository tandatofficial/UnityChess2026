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
    public GameObject userPanel; // 👈 Thêm dòng này

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);

        gameOverPanel.SetActive(false);
        restartButton.onClick.AddListener(RestartGame);
    }

    public void HideUserPanel() // 👈 Hàm để ẩn panel người dùng
    {
        if (userPanel != null)
        {
            userPanel.SetActive(false);
        }
    }

    public void ShowWinScreen(PieceColor winner)
    {
        gameOverPanel.SetActive(true);
        gameOverText.text = winner == PieceColor.White ? "Trắng Thắng!" : "Đen Thắng!";
    }

    public void ShowDrawScreen(string reason)
    {
        gameOverPanel.SetActive(true);
        gameOverText.text = $"Hòa: {reason}";
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
