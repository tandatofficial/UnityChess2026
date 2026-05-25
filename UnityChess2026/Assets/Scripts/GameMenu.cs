using UnityEngine;

public class GameMenu : MonoBehaviour
{
    [Header("Giao diện Menu Đầu Game")]
    public GameObject titleMenuPanel; // Bảng tên game và các nút Play, Quit

    [Header("Giao diện Chọn Độ Khó")]
    public GameObject menuPanel; 

    [Header("Kéo thả các nút Undo/Hint vào đây")]
    public GameObject[] easyModeOnlyUI; 

    [Header("Kéo thả nút Pause (Cài đặt) vào đây")]
    public GameObject pauseButton; 

    void Start()
    {
        // Khi vừa mở game: Bật Menu Chính, Tắt Menu Độ khó và Tắt nút Pause
        if (titleMenuPanel != null) titleMenuPanel.SetActive(true);
        if (menuPanel != null) menuPanel.SetActive(false);
        if (pauseButton != null) pauseButton.SetActive(false);
        
        // Đảm bảo game không bị đóng băng thời gian
        Time.timeScale = 1f; 
    }

    // --- CÁC HÀM CHO MENU CHÍNH ---
    public void ShowDifficultyMenu()
    {
        // Bấm "Bắt đầu chơi" -> Tắt Menu Chính, Bật Menu Chọn độ khó
        if (titleMenuPanel != null) titleMenuPanel.SetActive(false);
        if (menuPanel != null) menuPanel.SetActive(true);
    }

    public void OpenSettings()
    {
        Debug.Log("Mở menu Tùy chỉnh (Sẽ làm ở phần sau)");
        // Sau này code đổi màu cờ, bàn cờ sẽ gọi ở đây
    }

    public void QuitGameFromTitle()
    {
        Debug.Log("Đang thoát game từ Menu Chính...");
        Application.Quit();
    }

    // --- HÀM CHO MENU CHỌN ĐỘ KHÓ (Giữ nguyên) ---
    public void SelectDifficultyAndStart(int difficultyLevel)
    {
        PlayerPrefs.SetInt("Difficulty", difficultyLevel);
        PlayerPrefs.Save();

        if (menuPanel != null) menuPanel.SetActive(false);
        if (pauseButton != null) pauseButton.SetActive(true);

        bool isEasyMode = (difficultyLevel == 1);
        foreach (GameObject uiElement in easyModeOnlyUI)
        {
            if (uiElement != null) uiElement.SetActive(isEasyMode);
        }
        
        GameManager.Instance.UpdatePieceColliders();
    }
}