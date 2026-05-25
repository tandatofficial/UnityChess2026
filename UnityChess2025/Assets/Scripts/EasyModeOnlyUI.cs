using UnityEngine;

    public class EasyModeOnlyUI : MonoBehaviour
    {
        void Start()
        {
            // Lấy độ khó hiện tại. Mặc định là 2 nếu không tìm thấy.
            int difficulty = PlayerPrefs.GetInt("Difficulty", 2);

            // Nếu không phải chế độ Dễ (1), tự động ẩn chính nó đi
            if (difficulty != 1)
            {
                gameObject.SetActive(false);
            }
        }
    }