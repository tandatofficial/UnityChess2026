using UnityEngine;
using UnityEngine.EventSystems;

public class PieceSelector : MonoBehaviour
{
    private ChessPiece piece;

    void Start()
    {
        piece = GetComponent<ChessPiece>();
    }

    private void OnMouseDown()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (GameManager.Instance == null) return;

        GameManager.Instance.SelectPiece(piece);
    }
}