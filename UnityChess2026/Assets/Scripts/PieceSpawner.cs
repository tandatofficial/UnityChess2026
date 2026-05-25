using UnityEngine;

public class PieceSpawner : MonoBehaviour
{
    public static PieceSpawner Instance;

    public GameObject whitePawnPrefab;
    public GameObject whiteRookPrefab;
    public GameObject whiteKnightPrefab;
    public GameObject whiteBishopPrefab;
    public GameObject whiteQueenPrefab;
    public GameObject whiteKingPrefab;

    public GameObject blackPawnPrefab;
    public GameObject blackRookPrefab;
    public GameObject blackKnightPrefab;
    public GameObject blackBishopPrefab;
    public GameObject blackQueenPrefab;
    public GameObject blackKingPrefab;

    public GameObject blackPieces;
    public GameObject whitePieces;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        SpawnAllPieces();
    }

    public void SpawnAllPieces()
    {
        for (int col = 0; col < 8; col++)
        {
            SpawnPiece(whitePawnPrefab, 1, col, PieceColor.White, PieceType.Pawn);
            SpawnPiece(blackPawnPrefab, 6, col, PieceColor.Black, PieceType.Pawn);
        }

        // Rooks
        SpawnPiece(whiteRookPrefab, 0, 0, PieceColor.White, PieceType.Rook);
        SpawnPiece(whiteRookPrefab, 0, 7, PieceColor.White, PieceType.Rook);
        SpawnPiece(blackRookPrefab, 7, 0, PieceColor.Black, PieceType.Rook);
        SpawnPiece(blackRookPrefab, 7, 7, PieceColor.Black, PieceType.Rook);

        // Knights
        SpawnPiece(whiteKnightPrefab, 0, 1, PieceColor.White, PieceType.Knight);
        SpawnPiece(whiteKnightPrefab, 0, 6, PieceColor.White, PieceType.Knight);
        SpawnPiece(blackKnightPrefab, 7, 1, PieceColor.Black, PieceType.Knight);
        SpawnPiece(blackKnightPrefab, 7, 6, PieceColor.Black, PieceType.Knight);

        // Bishops
        SpawnPiece(whiteBishopPrefab, 0, 2, PieceColor.White, PieceType.Bishop);
        SpawnPiece(whiteBishopPrefab, 0, 5, PieceColor.White, PieceType.Bishop);
        SpawnPiece(blackBishopPrefab, 7, 2, PieceColor.Black, PieceType.Bishop);
        SpawnPiece(blackBishopPrefab, 7, 5, PieceColor.Black, PieceType.Bishop);

        // Queens
        SpawnPiece(whiteQueenPrefab, 0, 3, PieceColor.White, PieceType.Queen);
        SpawnPiece(blackQueenPrefab, 7, 3, PieceColor.Black, PieceType.Queen);

        // Kings
        SpawnPiece(whiteKingPrefab, 0, 4, PieceColor.White, PieceType.King);
        SpawnPiece(blackKingPrefab, 7, 4, PieceColor.Black, PieceType.King);
    }

    private void SpawnPiece(GameObject prefab, int row, int col, PieceColor color, PieceType type)
    {
        Vector3 position = BoardManager.Instance.GetWorldPosition(row, col);

        Quaternion rotation = Quaternion.identity;

        if (type == PieceType.Knight || type == PieceType.King)
        {
            rotation = (color == PieceColor.White)
                ? Quaternion.Euler(0, 90f, 0)
                : Quaternion.Euler(0, -90f, 0);
        }

        Transform parent = (color == PieceColor.White) ? whitePieces.transform : blackPieces.transform;

        ChessPiece piece = new ChessPieceBuilder()
            .Create(prefab, position, rotation, parent)
            .SetInfo(row, col, color, type)
            .AddSelector()
            .Build();

        GameManager.Instance.pieceBoard[row, col] = piece;
    }

}
