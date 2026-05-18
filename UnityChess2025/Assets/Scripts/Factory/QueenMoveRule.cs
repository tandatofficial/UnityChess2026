public class QueenMoveRule : IMoveRule
{
    private RookMoveRule rookRule = new RookMoveRule();
    private BishopMoveRule bishopRule = new BishopMoveRule();

    public bool IsValidMove(ChessPiece piece, int targetRow, int targetCol, ChessPiece[,] board)
    {
        return rookRule.IsValidMove(piece, targetRow, targetCol, board)
            || bishopRule.IsValidMove(piece, targetRow, targetCol, board);
    }
}
