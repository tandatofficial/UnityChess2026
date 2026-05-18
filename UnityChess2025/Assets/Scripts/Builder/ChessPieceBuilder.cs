using UnityEngine;

public class ChessPieceBuilder
{
    private GameObject pieceObject;

    public ChessPieceBuilder Create(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent)
    {
        pieceObject = Object.Instantiate(prefab, position, rotation, parent);
        return this;
    }

    public ChessPieceBuilder SetInfo(int row, int col, PieceColor color, PieceType type)
    {
        ChessPiece piece = pieceObject.GetComponent<ChessPiece>();
        piece.currentRow = row;
        piece.currentCol = col;
        piece.color = color;
        piece.type = type;
        return this;
    }

    public ChessPieceBuilder AddSelector()
    {
        if (pieceObject.GetComponent<PieceSelector>() == null)
            pieceObject.AddComponent<PieceSelector>();
        return this;
    }

    public ChessPiece Build()
    {
        return pieceObject.GetComponent<ChessPiece>();
    }
}
