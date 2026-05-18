using UnityEngine;

public class PromotionManager : MonoBehaviour
{
    public static PromotionManager Instance;

    private ChessPiece promotingPawn;

    public GameObject promotionPanel;
    public GameObject queenButton, rookButton, bishopButton, knightButton;

    public GameObject whiteQueenPrefab, whiteRookPrefab, whiteBishopPrefab, whiteKnightPrefab;
    public GameObject blackQueenPrefab, blackRookPrefab, blackBishopPrefab, blackKnightPrefab;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        promotionPanel.SetActive(false);

        // Gán sự kiện click cho nút và thêm Debug
        queenButton.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(() =>
        {
            Debug.Log("Button clicked: Queen");
            PromoteTo(PieceType.Queen);
        });

        rookButton.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(() =>
        {
            Debug.Log("Button clicked: Rook");
            PromoteTo(PieceType.Rook);
        });

        bishopButton.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(() =>
        {
            Debug.Log("Button clicked: Bishop");
            PromoteTo(PieceType.Bishop);
        });

        knightButton.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(() =>
        {
            Debug.Log("Button clicked: Knight");
            PromoteTo(PieceType.Knight);
        });
    }



    public void StartPromotion(ChessPiece pawn)
    {
        promotingPawn = pawn;
        promotionPanel.SetActive(true);
    }

    public void PromoteTo(PieceType newType)
    {
        if (promotingPawn == null)
        {
            Debug.LogWarning("No pawn selected for promotion.");
            return;
        }

        int row = promotingPawn.currentRow;
        int col = promotingPawn.currentCol;
        PieceColor color = promotingPawn.color;

        // Xóa quân tốt
        Destroy(promotingPawn.gameObject);
        GameManager.Instance.pieceBoard[row, col] = null;

        GameObject prefab = GetPromotionPrefab(color, newType);
        if (prefab == null)
        {
            Debug.LogError("Promotion prefab not found!");
            return;
        }

        Quaternion rotation = Quaternion.identity;
        if (newType == PieceType.Knight)
        {
            rotation = (color == PieceColor.White) ? Quaternion.Euler(0, 90f, 0) : Quaternion.Euler(0, -90f, 0);
        }

        ChessPiece newPiece = new ChessPieceBuilder()
            .Create(prefab, BoardManager.Instance.GetWorldPosition(row, col), rotation,
                    (color == PieceColor.White ? PieceSpawner.Instance.whitePieces.transform : PieceSpawner.Instance.blackPieces.transform))
            .SetInfo(row, col, color, newType)
            .AddSelector()
            .Build();

        GameManager.Instance.pieceBoard[row, col] = newPiece;

        promotionPanel.SetActive(false);
        promotingPawn = null;

        GameManager.Instance.NotifyIfCheck();

        // ✅ Kiểm tra checkmate sau khi phong
        PieceColor opponent = GameManager.Instance.currentTurn == PieceColor.White ? PieceColor.Black : PieceColor.White;
        if (GameManager.Instance.IsCheckmate(opponent))
        {
            GameManager.Instance.EndGame(GameManager.Instance.currentTurn);
            return;
        }

        GameManager.Instance.SwitchTurnAfterPromotion();
    }



    private GameObject GetPromotionPrefab(PieceColor color, PieceType type)
    {
        return (color, type) switch
        {
            (PieceColor.White, PieceType.Queen) => whiteQueenPrefab,
            (PieceColor.White, PieceType.Rook) => whiteRookPrefab,
            (PieceColor.White, PieceType.Bishop) => whiteBishopPrefab,
            (PieceColor.White, PieceType.Knight) => whiteKnightPrefab,
            (PieceColor.Black, PieceType.Queen) => blackQueenPrefab,
            (PieceColor.Black, PieceType.Rook) => blackRookPrefab,
            (PieceColor.Black, PieceType.Bishop) => blackBishopPrefab,
            (PieceColor.Black, PieceType.Knight) => blackKnightPrefab,
            _ => null,
        };
    }
}
