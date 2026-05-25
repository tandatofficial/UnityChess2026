using UnityEngine;
    using UnityEngine.SceneManagement;

    public class PauseMenuManager : MonoBehaviour
    {
        [Header("Kéo PauseMenuPanel vào đây")]
        public GameObject pauseMenuPanel;

        private bool isPaused = false;

        void Update()
        {
            // Bấm phím ESC trên bàn phím để bật/tắt menu
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (isPaused) ResumeGame();
                else PauseGame();
            }
        }

        // --- HÀM BẬT MENU (TẠM DỪNG GAME) ---
        public void PauseGame()
        {
            pauseMenuPanel.SetActive(true);
            Time.timeScale = 0f; // Đóng băng thời gian (Camera zoom/xoay và AI sẽ bị dừng lại)
            isPaused = true;
        }

        // --- HÀM TẮT MENU (TIẾP TỤC CHƠI) ---
        public void ResumeGame()
        {
            pauseMenuPanel.SetActive(false);
            Time.timeScale = 1f; // Rã đông thời gian, game chạy lại bình thường
            isPaused = false;
        }

        // --- HÀM VỀ MENU CHÍNH (RESET BÀN CỜ) ---
        public void RestartToMenu()
        {
            Time.timeScale = 1f; // Bắt buộc rã đông trước khi chuyển Scene, nếu không Scene mới sẽ bị kẹt
            
            // Load lại chính Scene hiện tại để mọi thứ reset về lúc mới mở game
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // --- HÀM THOÁT HẲN GAME ---
        public void QuitGame()
        {
            Debug.Log("Đang thoát game...");
            Application.Quit(); // Lệnh này chỉ hoạt động khi đã Build ra game thật (.exe / .apk)
        }
    }