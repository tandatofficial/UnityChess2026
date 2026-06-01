using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public static class GameMenuUiSafety
{
    public static void PreparePanel(GameObject activePanel)
    {
        EnsureEventSystemExists();
        BringPanelToFront(activePanel);
        DisableFullScreenBlockersExcept(activePanel);
    }

    public static void RestoreFullScreenBlockers()
    {
        Canvas canvas = Object.FindObjectOfType<Canvas>();
        if (canvas == null) return;

        Image[] images = canvas.GetComponentsInChildren<Image>(true);
        foreach (Image img in images)
        {
            if (img == null) continue;

            RectTransform rt = img.rectTransform;
            if (rt == null) continue;

            if (rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one)
                img.raycastTarget = true;
        }
    }

    private static void BringPanelToFront(GameObject panel)
    {
        if (panel == null) return;
        panel.transform.SetAsLastSibling();
    }

    private static void DisableFullScreenBlockersExcept(GameObject activePanel)
    {
        Canvas canvas = activePanel != null ? activePanel.GetComponentInParent<Canvas>() : null;
        if (canvas == null)
            canvas = Object.FindObjectOfType<Canvas>();
        if (canvas == null) return;

        Image[] images = canvas.GetComponentsInChildren<Image>(true);
        foreach (Image img in images)
        {
            if (img == null) continue;

            RectTransform rt = img.rectTransform;
            if (rt == null) continue;

            bool fullScreenAnchors = rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one;
            if (!fullScreenAnchors) continue;

            bool isUnderActive = activePanel != null && img.transform.IsChildOf(activePanel.transform);
            img.raycastTarget = isUnderActive;
        }
    }

    private static void EnsureEventSystemExists()
    {
        if (EventSystem.current != null) return;

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();

#if ENABLE_INPUT_SYSTEM
        eventSystemObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        eventSystemObject.AddComponent<StandaloneInputModule>();
#endif
    }
}