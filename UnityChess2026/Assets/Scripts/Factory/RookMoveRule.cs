public class RookMoveRule : IMoveRule
{
    public bool IsValidMove(ChessPiece piece, int targetRow, int targetCol, ChessPiece[,] board)
    {
        int row = piece.currentRow;
        int col = piece.currentCol;

        if (targetRow < 0 || targetRow >= 8 || targetCol < 0 || targetCol >= 8)
            return false;

        if (row != targetRow && col != targetCol) return false;

        if (row == targetRow)
        {
            int step = (targetCol > col) ? 1 : -1;
            for (int c = col + step; c != targetCol; c += step)
                if (board[row, c] != null) return false;
        }
        else
        {
            int step = (targetRow > row) ? 1 : -1;
            for (int r = row + step; r != targetRow; r += step)
                if (board[r, col] != null) return false;
        }

        // Không được đi vào ô có đồng minh
        ChessPiece targetPiece = board[targetRow, targetCol];
        return targetPiece == null || targetPiece.color != piece.color;
    }
}
