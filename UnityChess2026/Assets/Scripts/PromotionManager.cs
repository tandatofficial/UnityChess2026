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

        bool isAIBlack = GameManager.Instance != null &&
                         GameManager.Instance.currentGameMode == GameMode.AI &&
                         pawn.color == PieceColor.Black;

        if (isAIBlack)
        {
            // Nếu là quân Đen ở chế độ đấu với AI, tự động phong Hậu
            PromoteTo(PieceType.Queen);
        }
        else
        {
            // Chế độ Online (người chơi cầm Trắng hoặc Đen) hoặc Local 2P: bật menu chọn quân
            if (promotionPanel != null)
                promotionPanel.SetActive(true);
        }
    }

    public void StartPromotionFromNetwork(ChessPiece pawn, PieceType newType)
    {
        PromotePawnDirectly(pawn, newType);
    }

    public ChessPiece PromotePawnDirectly(ChessPiece pawn, PieceType newType)
    {
        if (pawn == null) return null;

        int row = pawn.currentRow;
        int col = pawn.currentCol;
        PieceColor color = pawn.color;

        Destroy(pawn.gameObject);
        GameManager.Instance.pieceBoard[row, col] = null;

        GameObject prefab = GetPromotionPrefab(color, newType);
        if (prefab == null)
        {
            Debug.LogError("Promotion prefab not found!");
            return null;
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

        newPiece.hasMoved = true;
        GameManager.Instance.pieceBoard[row, col] = newPiece;

        if (promotionPanel != null)
            promotionPanel.SetActive(false);
        if (promotingPawn == pawn)
            promotingPawn = null;

        return newPiece;
    }

    public void PromoteTo(PieceType newType)
    {
        if (promotingPawn == null)
        {
            Debug.LogWarning("No pawn selected for promotion.");
            return;
        }

        ChessPiece newPiece = PromotePawnDirectly(promotingPawn, newType);
        if (newPiece == null) return;

        // Gửi nước đi kèm loại quân phong cấp sau khi bàn cờ đã cập nhật quân mới
        if (GameManager.Instance != null && GameManager.Instance.currentGameMode == GameMode.Online)
        {
            if (!string.IsNullOrEmpty(GameManager.Instance.pendingNetworkMove))
            {
                GameManager.Instance.SendNetworkMove($"{GameManager.Instance.pendingNetworkMove}={newType}");
                GameManager.Instance.pendingNetworkMove = null;
            }
        }

        GameManager.Instance.NotifyIfCheck();

        PieceColor current = GameManager.Instance.currentTurn;
        PieceColor opponent = current == PieceColor.White ? PieceColor.Black : PieceColor.White;

        if (GameManager.Instance.IsCheckmate(opponent))
        {
            GameManager.Instance.EndGame(current);
            if (GameManager.Instance.currentGameMode == GameMode.Online && SocketManager.Instance != null)
            {
                string winnerStr = current == PieceColor.White ? "white" : "black";
                SocketManager.Instance.SendGameOver(winnerStr, "checkmate");
            }
            return;
        }

        if (GameManager.Instance.IsStalemate(opponent))
        {
            GameManager.Instance.EndDraw("Stalemate");
            if (GameManager.Instance.currentGameMode == GameMode.Online && SocketManager.Instance != null)
            {
                SocketManager.Instance.SendGameOver("draw", "stalemate");
            }
            return;
        }

        if (GameManager.Instance.IsInsufficientMaterial())
        {
            GameManager.Instance.EndDraw("Insufficient Material");
            if (GameManager.Instance.currentGameMode == GameMode.Online && SocketManager.Instance != null)
            {
                SocketManager.Instance.SendGameOver("draw", "insufficient_material");
            }
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
