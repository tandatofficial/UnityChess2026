
public static class MoveRuleFactory
{
    public static IMoveRule GetRule(PieceType type)
    {
        return type switch
        {
            PieceType.Pawn => new PawnMoveRule(),
            PieceType.Rook => new RookMoveRule(),
            PieceType.Knight => new KnightMoveRule(),
            PieceType.Bishop => new BishopMoveRule(),
            PieceType.Queen => new QueenMoveRule(),
            PieceType.King => new KingMoveRule(),
            _ => null
        };
    }
}
