using UnityEngine;

public class PieceSelector : MonoBehaviour
{
    private ChessPiece piece;

    void Start()
    {
        piece = GetComponent<ChessPiece>();
    }

    private void OnMouseDown()
    {
        GameManager.Instance.SelectPiece(piece);
    }
}
