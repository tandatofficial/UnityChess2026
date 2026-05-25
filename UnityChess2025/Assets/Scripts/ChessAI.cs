using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ChessAI : MonoBehaviour
{
    public void MakeMove(PieceColor aiColor)
    {
        StartCoroutine(ThinkAndMove(aiColor));
    }

    private IEnumerator ThinkAndMove(PieceColor aiColor)
    {
        yield return new WaitForSeconds(1.5f);

        int difficulty = PlayerPrefs.GetInt("Difficulty", 2); 
        Move bestMove = null;

        if (difficulty == 1) 
        {
            bestMove = GetRandomMove(aiColor); // Dễ
        }
        else if (difficulty == 2)
        {
            bestMove = GetGreedyMove(aiColor); // Thường
        }
        else 
        {
            bestMove = GetHardMove(aiColor);   // Khó
        }

        if (bestMove != null)
        {
            GameManager.Instance.selectedPiece = bestMove.piece;
            GameManager.Instance.TryMoveTo(bestMove.targetRow, bestMove.targetCol);
        }
        else
        {
            Debug.Log("AI đã hết nước đi!");
        }
    }

    private class Move
    {
        public ChessPiece piece;
        public int targetRow;
        public int targetCol;
    }

    // ĐÃ SỬA LỖI Ở ĐÂY: Đảm bảo có đủ <Move>
    private List<Move> GetAllPossibleMoves(PieceColor color)
    {
        List<Move> moves = new List<Move>();
        foreach (ChessPiece piece in GameManager.Instance.pieceBoard)
        {
            if (piece != null && piece.color == color)
            {
                List<Vector2Int> validMoves = GameManager.Instance.GetValidMoves(piece);
                foreach (Vector2Int pos in validMoves)
                {
                    moves.Add(new Move { piece = piece, targetRow = pos.x, targetCol = pos.y });
                }
            }
        }
        return moves;
    }

    private Move GetRandomMove(PieceColor color)
    {
        List<Move> allMoves = GetAllPossibleMoves(color);
        if (allMoves.Count > 0)
            return allMoves[Random.Range(0, allMoves.Count)];
        return null;
    }

    private Move GetGreedyMove(PieceColor color)
    {
        List<Move> allMoves = GetAllPossibleMoves(color);
        Move bestMove = null;
        int bestScore = -1;

        foreach (Move move in allMoves)
        {
            int score = 0;
            ChessPiece target = GameManager.Instance.pieceBoard[move.targetRow, move.targetCol];
            
            if (target != null)
            {
                score = GetPieceValue(target.type);
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
            }
        }
        
        if (bestScore == 0 && allMoves.Count > 0)
        {
            return allMoves[Random.Range(0, allMoves.Count)];
        }

        return bestMove;
    }

    private Move GetHardMove(PieceColor color)
    {
        List<Move> allMoves = GetAllPossibleMoves(color);
        Move bestMove = null;
        int bestScore = -int.MaxValue; // Điểm khởi đầu cực thấp

        foreach (Move move in allMoves)
        {
            int score = 0;
            ChessPiece target = GameManager.Instance.pieceBoard[move.targetRow, move.targetCol];
            
            // 1. Tiêu chí Tấn Công: Nếu ô đó có quân địch, cộng điểm bằng giá trị quân địch
            if (target != null)
            {
                // Nhân 10 để ưu tiên việc ăn quân lên hàng đầu
                score += GetPieceValue(target.type) * 10; 
            }

            // 2. Tiêu chí Phòng Thủ: Ô đích có đang bị quân của người chơi nhắm tới không?
            bool isDangerous = GameManager.Instance.IsSquareUnderAttack(move.targetRow, move.targetCol, color);
            if (isDangerous)
            {
                // Nếu đi vào ô nguy hiểm, trừ điểm tương ứng với giá trị quân của AI
                // (Ví dụ: Lấy Hậu đi vào ô bị Tốt chiếu thì trừ rất nặng, AI sẽ không đi)
                score -= GetPieceValue(move.piece.type) * 10; 
            }

            // 3. Tiêu chí Phát Triển: Khuyến khích tiến về phía trước (Bàn cờ Đen ở trên đi xuống hàng 0)
            if (color == PieceColor.Black)
            {
                // Càng tiến sát hàng 0 (phía Trắng) càng được cộng điểm nhẹ
                score += (7 - move.targetRow); 
            }

            // 4. Thêm một chút ngẫu nhiên nhỏ (0-5 điểm) để máy tính không đi 100 ván giống hệt nhau
            score += Random.Range(0, 5);

            // So sánh và chọn nước đi điểm cao nhất
            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
            }
        }

        // Nếu tất cả các nước đều quá tệ (bestMove vẫn null), chọn đi bừa 1 nước hợp lệ
        if (bestMove == null && allMoves.Count > 0)
        {
            return allMoves[Random.Range(0, allMoves.Count)];
        }

        return bestMove;
    }

    private int GetPieceValue(PieceType type)
    {
        switch (type)
        {
            case PieceType.Pawn: return 10;
            case PieceType.Knight: return 30;
            case PieceType.Bishop: return 30;
            case PieceType.Rook: return 50;
            case PieceType.Queen: return 90;
            case PieceType.King: return 900;
            default: return 0;
        }
    }
}