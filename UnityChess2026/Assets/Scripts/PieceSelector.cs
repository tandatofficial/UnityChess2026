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
        GameManager gm = GameManager.Get();
        if (gm == null) return;

        gm.SelectPiece(piece);
    }
}