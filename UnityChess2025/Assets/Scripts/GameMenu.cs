using UnityEngine;

    public class GameMenu : MonoBehaviour
    {
        [Header("Kéo thả object chứa bảng menu vào đây")]
        public GameObject menuPanel; 

        [Header("Kéo thả các nút Undo/Hint vào đây")]
        public GameObject[] easyModeOnlyUI; // Danh sách các UI chỉ hiện ở mode Dễ

        public void SelectDifficultyAndStart(int difficultyLevel)
        {
            PlayerPrefs.SetInt("Difficulty", difficultyLevel);
            PlayerPrefs.Save();

            // Ẩn bảng Menu
            if (menuPanel != null)
            {
                menuPanel.SetActive(false);
            }

            // Bật/tắt các nút Undo/Hint ngay lúc này
            bool isEasyMode = (difficultyLevel == 1);
            foreach (GameObject uiElement in easyModeOnlyUI)
            {
                if (uiElement != null)
                {
                    uiElement.SetActive(isEasyMode);
                }
            }
            
            // Đảm bảo bàn cờ được update trạng thái click
            GameManager.Instance.UpdatePieceColliders();
        }
    }