using UnityEngine;
using UnityEngine.EventSystems;

public class Square : MonoBehaviour
{
    public int row;
    public int col;

    private Renderer squareRenderer;

    private Material whiteMaterial;
    private Material blackMaterial;
    private Material highlightMaterial;

    private bool isWhite;

    // --- Quản lý trạng thái màu sắc ---
    private bool isHighlighted = false;
    private bool isHinted = false;
    private Material hintMaterial;

    private void Start()
    {
        squareRenderer = GetComponent<Renderer>();
    }

    public void SetMaterials(Material whiteMat, Material blackMat, Material highlightMat)
    {
        whiteMaterial = whiteMat;
        blackMaterial = blackMat;
        highlightMaterial = highlightMat;
    }

    public void ApplyInitialMaterial(bool white)
    {
        isWhite = white;
        squareRenderer = GetComponent<Renderer>();
        RefreshMaterial();
    }

    public void SetHighlight(bool on)
    {
        isHighlighted = on;
        RefreshMaterial();
    }

    public void SetHint(bool on, Material hMat = null)
    {
        isHinted = on;
        if (hMat != null) hintMaterial = hMat;
        RefreshMaterial();
    }

    private void RefreshMaterial()
    {
        if (squareRenderer == null) return;

        if (isHighlighted && highlightMaterial != null)
            squareRenderer.material = highlightMaterial;
        else if (isHinted && hintMaterial != null)
            squareRenderer.material = hintMaterial; // Hiển thị màu Hint
        else
            squareRenderer.material = isWhite ? whiteMaterial : blackMaterial;
    }

    private void OnMouseDown()
    {
        // Guard against missing EventSystem
        if (EventSystem.current != null)
        {
            if (EventSystem.current.IsPointerOverGameObject()) return;
        }

        // Guard against missing GameManager
        if (GameManager.Instance == null)
        {
            Debug.LogWarning("GameManager.Instance is null when clicking square.");
            return;
        }

        GameManager.Instance.TryMoveTo(row, col);
    }

    private void OnMouseEnter()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (GameManager.Instance != null && GameManager.Instance.selectedPiece != null && CursorManager.Instance != null)
            CursorManager.Instance.SetHand();
    }

    private void OnMouseExit()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (CursorManager.Instance != null)
            CursorManager.Instance.SetDefault();
    }
}