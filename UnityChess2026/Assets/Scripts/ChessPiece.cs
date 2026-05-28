using UnityEngine;
using UnityEngine.EventSystems;

public enum PieceType { Pawn, Rook, Knight, Bishop, Queen, King }
public enum PieceColor { White, Black }

[RequireComponent(typeof(Collider))]
public class ChessPiece : MonoBehaviour
{
    public PieceType type;
    public PieceColor color;
    public int currentRow;
    public int currentCol;

    private Renderer pieceRenderer;
    private Collider pieceCollider;

    public Material normalMaterial;
    public Material highlightMaterial;
    public Material attackHighlightMaterial;

    private Vector3 originalPosition;
    private Vector3 targetPosition;
    private float liftHeight = 0.3f;
    private float moveSpeed = 5f;

    public bool hasMoved = false;

    private void Awake()
    {
        pieceRenderer = GetComponent<Renderer>();
        pieceCollider = GetComponent<Collider>();
    }

    private void Start()
    {
        // Nếu SkinManager tồn tại, lấy màu skin hiện tại đè lên màu mặc định
        if (SkinManager.Instance != null)
        {
            Material skinMat = SkinManager.Instance.GetCurrentPieceMaterial(color);
            if (skinMat != null) 
            {
                normalMaterial = skinMat;
            }
        }
        
        if (pieceRenderer != null && normalMaterial != null)
        {
            pieceRenderer.material = normalMaterial;
        }

        originalPosition = transform.position;
        targetPosition = originalPosition;
    }

    private void Update()
    {
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * moveSpeed);
    }

    public void Highlight(bool isSelected, bool isUnderAttack = false)
    {
        if (pieceRenderer != null)
        {
            if (isUnderAttack && attackHighlightMaterial != null)
            {
                pieceRenderer.material = attackHighlightMaterial;
            }
            else if (isSelected && highlightMaterial != null)
            {
                pieceRenderer.material = highlightMaterial;
            }
            else
            {
                pieceRenderer.material = normalMaterial;
            }
        }

        targetPosition = isSelected
            ? new Vector3(originalPosition.x, originalPosition.y + liftHeight, originalPosition.z)
            : originalPosition;
    }

    public void ResetHighlight()
    {
        if (pieceRenderer != null && normalMaterial != null)
        {
            pieceRenderer.material = normalMaterial;
        }

        targetPosition = originalPosition;
    }

    public void SetColliderEnabled(bool enabled)
    {
        if (pieceCollider != null)
            pieceCollider.enabled = enabled;
    }

    private void OnMouseEnter()
    {
        if (EventSystem.current!= null && EventSystem.current.IsPointerOverGameObject()) return;

        if (GameManager.Instance.GetValidMoves(this).Count > 0 && color == GameManager.Instance.currentTurn)
            CursorManager.Instance.SetHand();
    }

    private void OnMouseExit()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        CursorManager.Instance.SetDefault();
    }

    public void DisableCollider()
    {
        if (pieceCollider != null)
            pieceCollider.enabled = false;
    }

    public void UpdateOriginalPosition()
    {
        originalPosition = transform.position;
        targetPosition = originalPosition;
    }
}