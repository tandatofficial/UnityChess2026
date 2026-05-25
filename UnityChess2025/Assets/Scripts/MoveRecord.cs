using UnityEngine;

// Class này không cần kế thừa MonoBehaviour vì nó chỉ lưu dữ liệu
public class MoveRecord
{
    // Thông tin về quân cờ di chuyển
    public ChessPiece movedPiece;
    public int fromRow;
    public int fromCol;
    public int toRow;
    public int toCol;
    public bool isFirstMove; // Lưu lại xem đây có phải nước đi đầu tiên của quân này không (dùng cho Pawn và King/Rook khi nhập thành)

    // Thông tin về quân cờ bị ăn (nếu có)
    public ChessPiece capturedPiece;
    
    // (Sau này có thể thêm thông tin về phong cấp, nhập thành nếu cần)

    public MoveRecord(ChessPiece movedPiece, int fromRow, int fromCol, int toRow, int toCol, bool isFirstMove, ChessPiece capturedPiece)
    {
        this.movedPiece = movedPiece;
        this.fromRow = fromRow;
        this.fromCol = fromCol;
        this.toRow = toRow;
        this.toCol = toCol;
        this.isFirstMove = isFirstMove;
        this.capturedPiece = capturedPiece;
    }
}