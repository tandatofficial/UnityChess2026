using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HintManager : MonoBehaviour
{
    public static HintManager Instance;
    public Material hintMaterial;
    private List<Square> hintedSquares = new List<Square>();
    
    // BỘ NHỚ TẠM MỚI: Lưu lịch sử bàn cờ và nước cờ gợi ý
    private string cachedHistory = "";
    private string cachedMove = "";

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // Hàm này đã được nối sẵn với nút Hint của bạn
    public void GiveHint()
    {
        // Chặn không cho bấm Hint khi đang đi cờ hoặc đang là lượt của máy (Black)
        if (GameManager.Instance.IsMoving() || GameManager.Instance.currentTurn == PieceColor.Black) 
        {
            Debug.LogWarning("Chưa tới lượt bạn hoặc cờ đang bay, không thể xem gợi ý!");
            return;
        }

        ClearHint();
        
        // Lấy lịch sử ván cờ hiện tại
        string currentHistory = GameManager.Instance.GetBoardFEN();
        Debug.Log($"🔍 [HINT DEBUG] Chuỗi gửi cho Stockfish: '{currentHistory}'");

        // --- BƯỚC KIỂM TRA BỘ NHỚ TẠM ---
        // Nếu lịch sử không đổi (chưa ai đi thêm nước nào), dùng luôn Hint đã tính
        if (currentHistory == cachedHistory && !string.IsNullOrEmpty(cachedMove))
        {
            Debug.Log("💡 Lấy gợi ý từ bộ nhớ tạm...");
            ShowHintOnBoard(cachedMove);
            return;
        }

        Debug.Log("💡 Đang nhờ siêu trí tuệ Stockfish tính toán...");
        
        // --- KÍCH HOẠT STOCKFISH ---
        if (StockfishManager.Instance != null)
        {
            // Cho máy suy nghĩ 3 giây (3000ms). Trả kết quả về qua callback.
            StockfishManager.Instance.GetBestMove(currentHistory, 3000, (suggestedMove) => 
            {
                // Lưu lại kết quả vào bộ nhớ tạm
                cachedHistory = currentHistory;
                cachedMove = suggestedMove;
                
                ShowHintOnBoard(suggestedMove);
            });
        }
        else
        {
            Debug.LogError("🚨 Không tìm thấy StockfishManager trong cảnh!");
        }
    }

    // Hàm nhận mã UCI (vd: e2e4) và vẽ lên bàn cờ
    private void ShowHintOnBoard(string move)
    {
        if (move == "(none)" || string.IsNullOrEmpty(move)) return;

        // Dịch mã UCI sang tọa độ mảng
        int fromCol = move[0] - 'a';
        int fromRow = move[1] - '1';
        int toCol = move[2] - 'a';
        int toRow = move[3] - '1';

        ChessPiece pieceToMove = GameManager.Instance.pieceBoard[fromRow, fromCol];
        if (pieceToMove != null)
        {
            Debug.Log($"💡 Gợi ý: Hãy cầm quân {pieceToMove.type} đi tới ô ({toRow},{toCol})");
        }

        // Bật sáng 2 ô: Ô quân cờ đang đứng và ô đích cần đến
        ShowHintOnSquare(fromRow, fromCol);
        ShowHintOnSquare(toRow, toCol);

        // Tự tắt sau 2.5 giây
        StartCoroutine(AutoClearHint());
    }

    private void ShowHintOnSquare(int row, int col)
    {
        GameObject squareObj = BoardManager.Instance.squares[row, col];
        Square square = squareObj.GetComponent<Square>();
        if (square != null)
        {
            Renderer squareRenderer = square.GetComponent<Renderer>();
            
            if (squareRenderer != null && hintMaterial != null)
            {
                squareRenderer.material = hintMaterial;
            }
            else
            {
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