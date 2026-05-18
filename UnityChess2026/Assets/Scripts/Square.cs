using UnityEngine;

public class Square : MonoBehaviour
{
    public int row;
    public int col;

    private Renderer squareRenderer;

    private Material whiteMaterial;
    private Material blackMaterial;
    private Material highlightMaterial;

    private bool isWhite;

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
        squareRenderer.material = isWhite ? whiteMaterial : blackMaterial;
    }

    public void SetHighlight(bool on)
    {
        if (squareRenderer == null || highlightMaterial == null) return;

        squareRenderer.material = on
            ? highlightMaterial
            : (isWhite ? whiteMaterial : blackMaterial);
    }

    private void OnMouseDown()
    {
        GameManager.Instance.TryMoveTo(row, col);
    }

    private void OnMouseEnter()
    {
        if (GameManager.Instance.selectedPiece != null)
            CursorManager.Instance.SetHand();
    }

    private void OnMouseExit()
    {
        CursorManager.Instance.SetDefault();
    }


}
