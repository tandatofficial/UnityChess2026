using UnityEngine;

public class PawnMoveRule : IMoveRule
{
    public bool IsValidMove(ChessPiece piece, int targetRow, int targetCol, ChessPiece[,] board)
    {
        int dir = piece.color == PieceColor.White ? 1 : -1;
        int startRow = piece.color == PieceColor.White ? 1 : 6;

        int dr = targetRow - piece.currentRow;
        int dc = targetCol - piece.currentCol;

        // Di chuyển thẳng
        if (dc == 0)
        {
            // 1 bước
            if (dr == dir && board[targetRow, targetCol] == null)
                return true;

            // 2 bước từ vị trí ban đầu
            if (piece.currentRow == startRow && dr == 2 * dir &&
                board[piece.currentRow + dir, targetCol] == null &&
                board[targetRow, targetCol] == null)
                return true;
        }

        // Ăn chéo
        if (Mathf.Abs(dc) == 1 && dr == dir)
        {
            ChessPiece target = board[targetRow, targetCol];
            if (target != null && target.color != piece.color)
                return true;
        }

        return false;
    }
}
