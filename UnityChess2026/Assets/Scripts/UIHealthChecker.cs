using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.SceneManagement;

// Runtime diagnostic helper for UI problems.
// Attach this to any GameObject in the scene (e.g. UIManager) to print a detailed
// report to the Console at Start(). This helps find missing EventSystems, inactive canvases,
// buttons without listeners, objects blocking raycasts, and more.
public class UIHealthChecker : MonoBehaviour
{
    [Header("Diagnostics")]
    public bool runOnStart = true;
    public bool listAllGameObjects = false; // optional deep list

    private void Start()
    {
        if (runOnStart)
            RunReport();
    }

    [ContextMenu("Run UI Health Report")]
    public void RunReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("--- UI Health Report ---");
        sb.AppendLine("Scene: " + SceneManager.GetActiveScene().name);

        // EventSystems
        var eventSystems = GameObject.FindObjectsOfType<EventSystem>();
        sb.AppendLine($"EventSystem count: {eventSystems.Length}");
        for (int i = 0; i < eventSystems.Length; i++)
        {
            var es = eventSystems[i];
            sb.AppendLine($"  [{i}] {es.gameObject.name} active={es.gameObject.activeInHierarchy}");
        }

        // Canvases
        var canvases = GameObject.FindObjectsOfType<Canvas>();
        sb.AppendLine($"Canvas count: {canvases.Length}");
        foreach (var c in canvases)
        {
            var go = c.gameObject;
            var cg = go.GetComponent<CanvasGroup>();
            var cr = go.GetComponent<GraphicRaycaster>();
            sb.AppendLine($"  Canvas: {go.name} active={go.activeInHierarchy} renderMode={c.renderMode} sortingOrder={c.sortingOrder}");
            if (cg != null)
                sb.AppendLine($"    CanvasGroup: interactable={cg.interactable} blocksRaycasts={cg.blocksRaycasts} alpha={cg.alpha}");
            sb.AppendLine($"    GraphicRaycaster present={(cr!=null)}");

            // Check children Images that block raycasts
            var images = go.GetComponentsInChildren<Image>(true);
            int blocking = 0;
            foreach (var img in images)
            {
                if (img.raycastTarget) blocking++;
            }
            sb.AppendLine($"    Images in children: {images.Length}, raycastTarget true count: {blocking}");
        }

        // Buttons
        var buttons = GameObject.FindObjectsOfType<Button>(true);
        sb.AppendLine($"Button count: {buttons.Length}");
        foreach (var b in buttons)
        {
            var go = b.gameObject;
            sb.AppendLine($"  Button: {go.name} active={go.activeInHierarchy} interactable={b.interactable}");
            try
            {
                int pc = b.onClick.GetPersistentEventCount();
                sb.AppendLine($"    onClick persistent listener count: {pc}");
                for (int i = 0; i < pc; i++)
                {
                    var target = b.onClick.GetPersistentTarget(i);
                    var method = b.onClick.GetPersistentMethodName(i);
                    sb.AppendLine($"      [{i}] target={(target!=null?target.ToString():"null")} method={method}");
                }
            }
            catch
            {
                // some Unity versions may throw for GetPersistent* at runtime; ignore
                sb.AppendLine("    (Could not read persistent listeners in this Unity runtime)");
            }
        }

        // TMP_InputField and InputField
        var tmpInputs = GameObject.FindObjectsOfType<TMP_InputField>(true);
        sb.AppendLine($"TMP_InputField count: {tmpInputs.Length}");
        foreach (var t in tmpInputs)
            sb.AppendLine($"  {t.gameObject.name} active={t.gameObject.activeInHierarchy}");

        var uiInputs = GameObject.FindObjectsOfType<InputField>(true);
        sb.AppendLine($"UI InputField count: {uiInputs.Length}");
        foreach (var t in uiInputs)
            sb.AppendLine($"  {t.gameObject.name} active={t.gameObject.activeInHierarchy}");

        // TMP_Text fields
        var tmpTexts = GameObject.FindObjectsOfType<TMP_Text>(true);
        sb.AppendLine($"TMP_Text count: {tmpTexts.Length}");

        // Check for objects that might be covering UI
        var allImages = GameObject.FindObjectsOfType<Image>(true);
        int fullScreenCover = 0;
        var fullScreenNames = new List<string>();
        foreach (var img in allImages)
        {
            var rt = img.rectTransform;
            if (rt != null && rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one && rt.gameObject.activeInHierarchy && img.raycastTarget)
            {
                fullScreenCover++;
                fullScreenNames.Add(img.gameObject.name + " (" + img.gameObject.transform.GetPath() + ")");
            }
        }
        sb.AppendLine($"Full-screen images that block raycasts: {fullScreenCover}");
        if (fullScreenNames.Count > 0)
        {
            sb.AppendLine("Full-screen blocker names:");
            foreach (var n in fullScreenNames) sb.AppendLine("  " + n);
        }

        if (listAllGameObjects)
        {
            sb.AppendLine("All GameObjects in scene:");
            var all = GameObject.FindObjectsOfType<Transform>(true);
            foreach (var t in all)
            {
                sb.AppendLine($"  {t.gameObject.name} active={t.gameObject.activeInHierarchy}");
            }
        }

        sb.AppendLine("--- End UI Health Report ---");

        Debug.Log(sb.ToString());
    }
}

// helper extension to get full transform path
public static class TransformExtensions
{
    public static string GetPath(this Transform current)
    {
        if (current.parent == null) return current.name;
        return current.parent.GetPath() + "/" + current.name;
    }
}
