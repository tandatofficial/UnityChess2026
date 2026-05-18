using UnityEngine;

public class BishopMoveRule : IMoveRule
{
    public bool IsValidMove(ChessPiece piece, int targetRow, int targetCol, ChessPiece[,] board)
    {
        int row = piece.currentRow;
        int col = piece.currentCol;

        int dr = targetRow - row;
        int dc = targetCol - col;

        if (Mathf.Abs(dr) != Mathf.Abs(dc)) return false;

        int stepR = dr > 0 ? 1 : -1;
        int stepC = dc > 0 ? 1 : -1;

        int r = row + stepR;
        int c = col + stepC;

        while (r != targetRow && c != targetCol)
        {
            if (board[r, c] != null) return false;
            r += stepR;
            c += stepC;
        }

        // Không đi vào ô có đồng minh
        ChessPiece target = board[targetRow, targetCol];
        return target == null || target.color != piece.color;
    }
}

