using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HintManager : MonoBehaviour
{
    public static HintManager Instance;
    public Material hintMaterial;
    private List<Square> hintedSquares = new List<Square>();
    
    // MỚI THÊM: Bộ nhớ tạm để lưu Hint của lượt hiện tại
    private System.Tuple<ChessPiece, Vector2Int> cachedHint = null;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void GiveHint()
    {
        if (GameManager.Instance.IsMoving()) return;

        ClearHint();

        PieceColor myColor = GameManager.Instance.currentTurn;

        // --- BƯỚC KIỂM TRA BỘ NHỚ TẠM ---
        // Nếu đã có Hint trong bộ nhớ, kiểm tra xem nó còn dùng được cho lượt này không
        if (cachedHint != null && cachedHint.Item1 != null)
        {
            // Điều kiện dùng lại Hint cũ:
            // 1. Vẫn là lượt của màu đó
            // 2. Quân cờ gợi ý vẫn đang đứng yên ở vị trí cũ trên bàn cờ
            // 3. Nước đi gợi ý đó vẫn là một nước đi hợp lệ
            if (cachedHint.Item1.color == myColor &&
                GameManager.Instance.pieceBoard[cachedHint.Item1.currentRow, cachedHint.Item1.currentCol] == cachedHint.Item1 &&
                GameManager.Instance.GetValidMoves(cachedHint.Item1).Contains(cachedHint.Item2))
            {
                ShowHintOnSquare(cachedHint.Item1.currentRow, cachedHint.Item1.currentCol);
                ShowHintOnSquare(cachedHint.Item2.x, cachedHint.Item2.y);
                StartCoroutine(AutoClearHint());
                return; 
            }
        }

        // --- NẾU HINT CŨ ĐÃ HẾT HẠN (qua lượt hoặc bị ăn), TÍNH HINT MỚI ---
        List<System.Tuple<ChessPiece, Vector2Int>> allValidMoves = new();

        foreach (ChessPiece piece in GameManager.Instance.pieceBoard)
        {
            if (piece != null && piece.color == myColor)
            {
                List<Vector2Int> moves = GameManager.Instance.GetValidMoves(piece);
                foreach (Vector2Int move in moves)
                {
                    allValidMoves.Add(new System.Tuple<ChessPiece, Vector2Int>(piece, move));
                }
            }
        }

        if (allValidMoves.Count == 0) return;

        System.Tuple<ChessPiece, Vector2Int> bestMove = null;
        int highestScore = int.MinValue;

        foreach (var move in allValidMoves)
        {
            int score = EvaluateMove(move.Item1, move.Item2, myColor);
            
            // Xáo trộn nhẹ để đa dạng nước đi cho các ván KHÁC NHAU
            if (score > highestScore || (score == highestScore && Random.value > 0.5f))
            {
                highestScore = score;
                bestMove = move;
            }
        }

        // --- LƯU LẠI VÀO BỘ NHỚ TẠM ---
        if (bestMove != null)
        {
            cachedHint = bestMove; // Lưu lại để lần sau bấm nút đỡ phải tính
            
            ShowHintOnSquare(bestMove.Item1.currentRow, bestMove.Item1.currentCol);
            ShowHintOnSquare(bestMove.Item2.x, bestMove.Item2.y);
            StartCoroutine(AutoClearHint());
        }
    }

    // THUẬT TOÁN CHẤM ĐIỂM
    private int EvaluateMove(ChessPiece piece, Vector2Int targetMove, PieceColor myColor)
    {
        int score = 0;
        int pieceValue = GetPieceValue(piece.type);

        ChessPiece targetPiece = GameManager.Instance.pieceBoard[targetMove.x, targetMove.y];
        if (targetPiece != null)
        {
            score += GetPieceValue(targetPiece.type) * 10; 
        }

        bool isTargetUnsafe = GameManager.Instance.IsSquareUnderAttack(targetMove.x, targetMove.y, myColor);
        if (isTargetUnsafe)
        {
            score -= pieceValue * 15; 
        }
        else
        {
            score += 5; 
        }

        bool isCurrentlyAttacked = GameManager.Instance.IsSquareUnderAttack(piece.currentRow, piece.currentCol, myColor);
        if (isCurrentlyAttacked)
        {
            if (!isTargetUnsafe) score += pieceValue * 12; 
        }

        if (targetMove.x >= 2 && targetMove.x <= 5 && targetMove.y >= 2 && targetMove.y <= 5)
        {
            score += 3;
        }

        if (piece.type == PieceType.Pawn)
        {
            int advanceDir = myColor == PieceColor.White ? 1 : -1;
            
            if ((targetMove.x - piece.currentRow) == advanceDir || (targetMove.x - piece.currentRow) == advanceDir * 2) 
            {
                score += 4; 
            }

            int distanceToPromotion = myColor == PieceColor.White ? 7 - targetMove.x : targetMove.x;
            score += (10 - distanceToPromotion); 
        }

        return score;
    }

    private int GetPieceValue(PieceType type)
    {
        switch (type)
        {
            case PieceType.Queen: return 9;
            case PieceType.Rook: return 5;
            case PieceType.Bishop: return 3;
            case PieceType.Knight: return 3;
            case PieceType.Pawn: return 1;
            case PieceType.King: return 100; 
            default: return 0;
        }
    }

    private void ShowHintOnSquare(int row, int col)
    {
        GameObject squareObj = BoardManager.Instance.squares[row, col];
        Square square = squareObj.GetComponent<Square>();
        if (square != null)
        {
            Renderer squareRenderer = square.GetComponent<Renderer>();
            
            // Nếu bạn đã gán Material màu Hint riêng, nó sẽ ép ô cờ đổi sang màu đó
            if (squareRenderer != null && hintMaterial != null)
            {
                squareRenderer.material = hintMaterial;
            }
            else
            {
                // Nếu quên chưa gán, nó sẽ dùng lại màu mặc định của ô cờ
                square.SetHighlight(true); 
            }
            
            hintedSquares.Add(square);
        }
    }

    public void ClearHint()
    {
        foreach (Square sq in hintedSquares)
        {
            if (sq != null)
            {
                // Ép ô cờ tự tắt trạng thái Highlight để quay về màu Gỗ/Concrete chuẩn theo Skin đang dùng
                sq.SetHighlight(false);
            }
        }
        hintedSquares.Clear();
    }

    private IEnumerator AutoClearHint()
    {
        yield return new WaitForSeconds(2.5f);
        ClearHint();
    }
}