using UnityEngine;

public class KingMoveRule : IMoveRule
{
    public bool IsValidMove(ChessPiece piece, int targetRow, int targetCol, ChessPiece[,] board)
    {
        int dr = Mathf.Abs(targetRow - piece.currentRow);
        int dc = Mathf.Abs(targetCol - piece.currentCol);

        // Di chuyển thường 1 ô
        if (dr <= 1 && dc <= 1)
        {
            ChessPiece target = board[targetRow, targetCol];
            return target == null || target.color != piece.color;
        }

        // ✨ Nhập thành
        if (dr == 0 && dc == 2 && !piece.hasMoved)
        {
            int row = piece.currentRow;
            int dir = targetCol > piece.currentCol ? 1 : -1;
            int rookCol = dir > 0 ? 7 : 0;
            ChessPiece rook = board[row, rookCol];

            if (rook == null || rook.type != PieceType.Rook || rook.hasMoved)
                return false;

            // Kiểm tra không có quân cản giữa vua và xe
            for (int c = piece.currentCol + dir; c != rookCol; c += dir)
                if (board[row, c] != null)
                    return false;

            // 🔍 Kiểm tra các ô vua đi qua không bị chiếu
            GameManager gm = GameManager.Instance;
            if (gm == null) return false;

            for (int i = 0; i <= 2; i++) // kiểm tra vị trí hiện tại, trung gian, đích
            {
                int colToCheck = piece.currentCol + i * dir;
                if (gm.IsSquareUnderAttack(row, colToCheck, piece.color))
                    return false;
            }

            return true;
        }

        return false;
    }
}
