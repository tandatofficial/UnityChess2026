//using UnityEngine;

//public class PieceFactory : MonoBehaviour
//{
//    public static PieceFactory Instance;

//    public GameObject whiteQueenPrefab, whiteRookPrefab, whiteBishopPrefab, whiteKnightPrefab;
//    public GameObject blackQueenPrefab, blackRookPrefab, blackBishopPrefab, blackKnightPrefab;

//    public GameObject whitePiecesParent;
//    public GameObject blackPiecesParent;

//    private void Awake()
//    {
//        if (Instance == null) Instance = this;
//        else Destroy(gameObject);
//    }

//    public GameObject CreatePiece(PieceType type, PieceColor color, int row, int col)
//    {
//        GameObject prefab = GetPrefab(type, color);
//        Vector3 pos = BoardManager.Instance.GetWorldPosition(row, col);
//        Quaternion rotation = Quaternion.identity;
//        Transform parent = color == PieceColor.White ? whitePiecesParent.transform : blackPiecesParent.transform;

//        GameObject newPiece = Instantiate(prefab, pos, rotation, parent);
//        ChessPiece cp = newPiece.GetComponent<ChessPiece>();
//        cp.type = type;
//        cp.color = color;
//        cp.currentRow = row;
//        cp.currentCol = col;
//        cp.UpdateOriginalPosition();

//        if (newPiece.GetComponent<PieceSelector>() == null)
//            newPiece.AddComponent<PieceSelector>();

//        return newPiece;
//    }

//    public void ReplacePieceModel(ChessPiece piece, PieceType newType)
//    {
//        int row = piece.currentRow;
//        int col = piece.currentCol;
//        PieceColor color = piece.color;

//        Destroy(piece.gameObject); // Xoá quân cũ
//        CreatePiece(newType, color, row, col); // Sinh quân mới
//    }

//    private GameObject GetPrefab(PieceType type, PieceColor color)
//    {
//        return (color, type) switch
//        {
//            (PieceColor.White, PieceType.Queen) => whiteQueenPrefab,
//            (PieceColor.White, PieceType.Rook) => whiteRookPrefab,
//            (PieceColor.White, PieceType.Bishop) => whiteBishopPrefab,
//            (PieceColor.White, PieceType.Knight) => whiteKnightPrefab,
//            (PieceColor.Black, PieceType.Queen) => blackQueenPrefab,
//            (PieceColor.Black, PieceType.Rook) => blackRookPrefab,
//            (PieceColor.Black, PieceType.Bishop) => blackBishopPrefab,
//            (PieceColor.Black, PieceType.Knight) => blackKnightPrefab,
//            _ => null
//        };
//    }
//}
