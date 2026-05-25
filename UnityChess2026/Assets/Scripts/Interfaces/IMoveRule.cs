public interface IMoveRule
{
    bool IsValidMove(ChessPiece piece, int targetRow, int targetCol, ChessPiece[,] board);
}
