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
    private bool isAnimatingMovement = false;

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
        if (!isAnimatingMovement)
        {
            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * moveSpeed);
        }
    }

    public void SetTargetPosition(Vector3 newPos)
    {
        originalPosition = newPos;
        targetPosition = newPos;
    }

    public void StartMoveAnimation(Vector3 targetSquarePos, float duration)
    {
        StartCoroutine(MoveToSquareCoroutine(targetSquarePos, duration));
    }

    private System.Collections.IEnumerator MoveToSquareCoroutine(Vector3 targetSquarePos, float duration)
    {
        isAnimatingMovement = true;
        Vector3 startPos = transform.position;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            
            // Smooth transition step
            float tSmooth = t * t * (3f - 2f * t);
            
            // Parabolic path
            Vector3 currentPos = Vector3.Lerp(startPos, targetSquarePos, tSmooth);
            float arcHeight = 0.5f;
            currentPos.y += Mathf.Sin(t * Mathf.PI) * arcHeight;
            
            transform.position = currentPos;
            yield return null;
        }

        transform.position = targetSquarePos;
        originalPosition = targetSquarePos;
        targetPosition = targetSquarePos;
        isAnimatingMovement = false;
    }

    public void DisableComponent()
    {
        this.enabled = false;
    }

    public void EnableComponent()
    {
        this.enabled = true;
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