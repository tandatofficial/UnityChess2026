using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public ChessPiece[,] pieceBoard = new ChessPiece[8, 8];
    public ChessPiece selectedPiece;
    public PieceColor currentTurn = PieceColor.White;

    private readonly List<Square> highlightedSquares = new();
    private readonly List<IKingCheckObserver> checkObservers = new();

    private bool isMoving = false;
    private ChessPiece lastCheckedKing = null;
    public bool enableCameraSwitch = true;

    private Stack<MoveRecord> moveHistory = new Stack<MoveRecord>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(Instance.gameObject);
        }

        Instance = this;
        ResetRuntimeState();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void ResetRuntimeState()
    {
        pieceBoard = new ChessPiece[8, 8];
        selectedPiece = null;
        currentTurn = PieceColor.White;
        highlightedSquares.Clear();
        checkObservers.Clear();
        isMoving = false;
        lastCheckedKing = null;
        moveHistory.Clear();
        Time.timeScale = 1f;
    }

    public void UpdatePieceColliders()
    {
        foreach (ChessPiece piece in pieceBoard)
        {
            if (piece == null) continue;

            bool isControllable = !SocketManager.Instance.isSpectator &&
                                  piece.color == SocketManager.Instance.assignedColor &&
                                  currentTurn == SocketManager.Instance.assignedColor;

            piece.SetColliderEnabled(isControllable);
        }
    }

    public void SelectPiece(ChessPiece piece)
    {
        if (piece.color != currentTurn) return;

        if (selectedPiece == piece)
        {
            DeselectPiece();
            return;
        }

        List<Vector2Int> validMoves = GetValidMoves(piece);
        if (validMoves.Count == 0)
        {
            StartCoroutine(new ShakeAnimation(0.25f, 0.15f).Play(piece));
            if (CursorManager.Instance != null)
                CursorManager.Instance.SetDefault();
            return;
        }

        DeselectPiece();
        selectedPiece = piece;
        selectedPiece.Highlight(true);
        if (CursorManager.Instance != null)
            CursorManager.Instance.SetHand();
        HighlightValidMoves(validMoves);
    }

    public void TryMoveTo(int row, int col)
    {
        // Xóa dấu vết Gợi ý ngay khi người chơi vừa đi cờ
        if (HintManager.Instance != null) HintManager.Instance.ClearHint();

        // Không có quân cờ được chọn
        if (selectedPiece == null) return;

        // Không phải lượt của người chơi
        // if (currentTurn != SocketManager.Instance.assignedColor) return;

        // ✅ Lưu thông tin quân cờ trước khi di chuyển
        int fromRow = selectedPiece.currentRow;
        int fromCol = selectedPiece.currentCol;
        PieceType movingType = selectedPiece.type;
        PieceColor movingColor = selectedPiece.color;

        Vector2Int target = new(row, col);

        // Nếu nước đi không hợp lệ
        if (!IsMoveLegal(selectedPiece, target))
        {
            if (CursorManager.Instance != null)
                CursorManager.Instance.SetDefault();
            return;
        }

        isMoving = true;

        // Xử lý bắt quân nếu có
        // --- BẮT ĐẦU PHẦN THÊM MỚI ĐỂ LƯU LỊCH SỬ ---
        ChessPiece targetPiece = pieceBoard[row, col];
        bool wasFirstMove = !selectedPiece.hasMoved; 
        
        // Tạo một bản ghi và cất vào ngăn kéo (Stack)
        float whiteTime = ChessClock.Instance != null ? ChessClock.Instance.whiteTimeRemaining : 0f;
        float blackTime = ChessClock.Instance != null ? ChessClock.Instance.blackTimeRemaining : 0f;
        MoveRecord record = new MoveRecord(selectedPiece, fromRow, fromCol, row, col, wasFirstMove, targetPiece, whiteTime, blackTime);
        moveHistory.Push(record);
        // --- KẾT THÚC PHẦN THÊM MỚI ---

        // Xử lý bắt quân nếu có
        HandleCapture(targetPiece);

        // Di chuyển quân
        MovePiece(selectedPiece, row, col);
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayMoveSound();

        // ✅ Gửi nước đi tới server
        // SocketManager.Instance.SendMove($"{fromRow},{fromCol}->{row},{col}");

        // ✅ Log ra console
        Debug.Log($"♟️ {movingColor} {movingType} moved from {fromRow},{fromCol} to {row},{col}");

        // Xử lý phong hậu nếu cần
        if (HandlePromotionIfNeeded(selectedPiece))
        {
            ClearHighlights();
            return;
        }

        // Hủy chọn, kiểm tra chiếu và tiếp tục game
        DeselectPiece();
        NotifyIfCheck();

        PieceColor opponent = currentTurn == PieceColor.White ? PieceColor.Black : PieceColor.White;

        // Kiểm tra thắng thua/hòa
        if (IsCheckmate(opponent))
        {
            EndGame(currentTurn);
            return;
        }

        if (IsStalemate(opponent))
        {
            EndDraw("Stalemate");
            return;
        }

        if (IsInsufficientMaterial())
        {
            EndDraw("Insufficient Material");
            return;
        }

        // Chuyển lượt + cập nhật camera nếu cần
        StartCoroutine(SwitchTurnWithCamera());
    }

    public void ApplyMoveFromNetwork(string move)
    {
        if (string.IsNullOrEmpty(move)) return;

        string[] parts = move.Split("->");
        if (parts.Length != 2) return;

        string[] from = parts[0].Split(',');
        string[] to = parts[1].Split(',');

        int fromRow = int.Parse(from[0]);
        int fromCol = int.Parse(from[1]);
        int toRow = int.Parse(to[0]);
        int toCol = int.Parse(to[1]);

        ChessPiece piece = pieceBoard[fromRow, fromCol];
        if (piece == null) return;

        selectedPiece = piece;

        ChessPiece targetPiece = pieceBoard[toRow, toCol];
        bool wasFirstMove = !piece.hasMoved;
        float whiteTime = ChessClock.Instance != null ? ChessClock.Instance.whiteTimeRemaining : 0f;
        float blackTime = ChessClock.Instance != null ? ChessClock.Instance.blackTimeRemaining : 0f;
        MoveRecord record = new MoveRecord(piece, fromRow, fromCol, toRow, toCol, wasFirstMove, targetPiece, whiteTime, blackTime);
        moveHistory.Push(record);

        HandleCapture(targetPiece);
        MovePiece(piece, toRow, toCol);
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayMoveSound();

        if (HandlePromotionIfNeeded(piece))
        {
            ClearHighlights();
            return;
        }

        DeselectPiece();
        NotifyIfCheck();

        PieceColor opponent = currentTurn == PieceColor.White ? PieceColor.Black : PieceColor.White;

        if (IsCheckmate(opponent))
        {
            EndGame(currentTurn);
            return;
        }

        if (IsStalemate(opponent))
        {
            EndDraw("Stalemate");
            return;
        }

        if (IsInsufficientMaterial())
        {
            EndDraw("Insufficient Material");
            return;
        }

        StartCoroutine(SwitchTurnWithCamera());
    }

    public void EndGame(PieceColor winner)
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayWinSound();
        isMoving = false;
        Time.timeScale = 0f;

        if (ChessClock.Instance != null)
            ChessClock.Instance.StopClock();

        foreach (ChessPiece piece in pieceBoard)
            if (piece != null) piece.ResetHighlight();

        if (UIManager.Instance != null)
            UIManager.Instance.ShowWinScreen(winner);
    }

    public void EndDraw(string reason)
    {
        Debug.Log("Game ended in a draw! Reason: " + reason);
        isMoving = false;
        Time.timeScale = 0f;

        if (ChessClock.Instance != null)
            ChessClock.Instance.StopClock();

        foreach (ChessPiece piece in pieceBoard)
            if (piece != null) piece.ResetHighlight();

        if (UIManager.Instance != null)
            UIManager.Instance.ShowDrawScreen(reason); 
    }


    public void DeselectPiece()
    {
        if (selectedPiece != null)
        {
            selectedPiece.Highlight(false);
            ClearHighlights();
            selectedPiece = null;
            if (CursorManager.Instance != null)
                CursorManager.Instance.SetDefault();
        }
    }

    public List<Vector2Int> GetValidMoves(ChessPiece piece)
    {
        List<Vector2Int> moves = new();
        var rule = MoveRuleFactory.GetRule(piece.type);
        if (rule == null) return moves;

        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                if (row == piece.currentRow && col == piece.currentCol) continue;

                // Nếu ô đích có chứa quân cờ và quân đó là Vua -> Bỏ qua, không coi là nước đi hợp lệ
                ChessPiece targetPiece = pieceBoard[row, col];
                if (targetPiece != null && targetPiece.type == PieceType.King) continue;

                if (!rule.IsValidMove(piece, row, col, pieceBoard)) continue;

                if (SimulateMoveSafe(piece, row, col))
                    moves.Add(new Vector2Int(row, col));
            }
        }

        return moves;
    }

    private bool SimulateMoveSafe(ChessPiece piece, int targetRow, int targetCol)
    {
        ChessPiece[,] tempBoard = new ChessPiece[8, 8];
        System.Array.Copy(pieceBoard, tempBoard, pieceBoard.Length);

        ChessPiece tempPiece = piece;
        ChessPiece oldTarget = tempBoard[targetRow, targetCol];

        tempBoard[piece.currentRow, piece.currentCol] = null;
        tempBoard[targetRow, targetCol] = tempPiece;

        int oldRow = piece.currentRow;
        int oldCol = piece.currentCol;

        piece.currentRow = targetRow;
        piece.currentCol = targetCol;

        bool isSafe = !IsKingInCheck(piece.color, tempBoard);

        piece.currentRow = oldRow;
        piece.currentCol = oldCol;
        return isSafe;
    }

    private bool IsMoveLegal(ChessPiece piece, Vector2Int target)
    {
        return GetValidMoves(piece).Contains(target);
    }

    private void HandleCapture(ChessPiece target)
    {
        if (target != null && target.color != selectedPiece.color)
        {
            target.DisableCollider();
            if (CaptureManager.Instance != null)
                CaptureManager.Instance.CapturePiece(target);
            pieceBoard[target.currentRow, target.currentCol] = null;
        }
    }

    private void MovePiece(ChessPiece piece, int row, int col)
    {
        int oldRow = piece.currentRow;
        int oldCol = piece.currentCol;

        // Castling
        if (piece.type == PieceType.King && Mathf.Abs(col - oldCol) == 2)
        {
            int rookStartCol = col > oldCol ? 7 : 0;
            int rookTargetCol = col > oldCol ? col - 1 : col + 1;
            ChessPiece rook = pieceBoard[row, rookStartCol];

            if (rook != null)
            {
                pieceBoard[row, rookStartCol] = null;
                pieceBoard[row, rookTargetCol] = rook;
                rook.transform.position = BoardManager.Instance.GetWorldPosition(row, rookTargetCol);
                rook.currentCol = rookTargetCol;
                rook.UpdateOriginalPosition();
                rook.hasMoved = true;
            }
        }

        pieceBoard[oldRow, oldCol] = null;
        piece.transform.position = BoardManager.Instance.GetWorldPosition(row, col);
        piece.currentRow = row;
        piece.currentCol = col;
        pieceBoard[row, col] = piece;
        piece.UpdateOriginalPosition();
        piece.hasMoved = true;
    }

    private bool HandlePromotionIfNeeded(ChessPiece piece)
    {
        if (piece.type == PieceType.Pawn && (piece.currentRow == 0 || piece.currentRow == 7))
        {
            if (PromotionManager.Instance != null)
                PromotionManager.Instance.StartPromotion(piece);
            return true;
        }
        return false;
    }

    public void NotifyIfCheck()
    {
        PieceColor opponent = currentTurn == PieceColor.White ? PieceColor.Black : PieceColor.White;
        ChessPiece currentKing = FindKing(opponent, pieceBoard);
        if (currentKing == null) return;

        bool isInCheck = IsKingInCheck(opponent, pieceBoard);

        if (isInCheck)
        {
            currentKing.Highlight(false, true);
            StartCoroutine(new ShakeAnimation(0.25f, 0.1f).Play(currentKing));
            if (AudioManager.Instance != null)
                AudioManager.Instance.PlayCheckSound();

            foreach (var observer in checkObservers)
                observer.OnKingInCheck(opponent);

            lastCheckedKing = currentKing;
        }
        else
        {
            if (lastCheckedKing != null)
            {
                lastCheckedKing.ResetHighlight();
                lastCheckedKing = null;
            }
        }
    }

    private bool IsKingInCheck(PieceColor color, ChessPiece[,] board)
    {
        ChessPiece king = FindKing(color, board);
        if (king == null) return false;

        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                ChessPiece attacker = board[row, col];
                if (attacker != null && attacker.color != color)
                {
                    var rule = MoveRuleFactory.GetRule(attacker.type);
                    if (rule != null && rule.IsValidMove(attacker, king.currentRow, king.currentCol, board))
                        return true;
                }
            }
        }
        return false;
    }

    private ChessPiece FindKing(PieceColor color, ChessPiece[,] board)
    {
        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                ChessPiece piece = board[row, col];
                if (piece != null && piece.color == color && piece.type == PieceType.King)
                    return piece;
            }
        }
        return null;
    }

    private void HighlightValidMoves(List<Vector2Int> positions)
    {
        ClearHighlights();
        foreach (Vector2Int pos in positions)
        {
            GameObject squareObj = BoardManager.Instance.squares[pos.x, pos.y];
            Square square = squareObj.GetComponent<Square>();
            square.SetHighlight(true);
            highlightedSquares.Add(square);

            ChessPiece target = pieceBoard[pos.x, pos.y];
            if (target != null && target.color != selectedPiece.color)
                target.Highlight(false, true);
        }
    }

    public bool IsSquareUnderAttack(int row, int col, PieceColor defenderColor)
    {
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                ChessPiece attacker = pieceBoard[r, c];
                if (attacker != null && attacker.color != defenderColor)
                {
                    var rule = MoveRuleFactory.GetRule(attacker.type);
                    if (rule != null && rule.IsValidMove(attacker, row, col, pieceBoard))
                        return true;
                }
            }
        }
        return false;
    }

    public bool IsCheckmate(PieceColor color)
    {
        if (!IsKingInCheck(color, pieceBoard)) return false;

        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                ChessPiece piece = pieceBoard[row, col];
                if (piece != null && piece.color == color)
                {
                    var moves = GetValidMoves(piece);
                    if (moves.Count > 0)
                        return false;
                }
            }
        }

        return true;
    }

    public bool IsStalemate(PieceColor color)
    {
        if (IsKingInCheck(color, pieceBoard)) return false;

        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                ChessPiece piece = pieceBoard[row, col];
                if (piece != null && piece.color == color)
                {
                    var moves = GetValidMoves(piece);
                    if (moves.Count > 0)
                        return false;
                }
            }
        }

        return true;
    }

    public bool IsInsufficientMaterial()
    {
        List<ChessPiece> allPieces = new();

        foreach (var piece in pieceBoard)
            if (piece != null) allPieces.Add(piece);

        if (allPieces.Count == 2) return true; // King vs King

        if (allPieces.Count == 3)
        {
            foreach (var piece in allPieces)
            {
                if (piece.type != PieceType.King)
                {
                    if (piece.type == PieceType.Bishop || piece.type == PieceType.Knight)
                        return true;
                }
            }
        }

        return false;
    }

    public void SwitchTurnAfterPromotion()
    {
        StartCoroutine(SwitchTurnWithCamera());
    }

    private IEnumerator SwitchTurnWithCamera()
    {
        isMoving = true;
        currentTurn = currentTurn == PieceColor.White ? PieceColor.Black : PieceColor.White;

        yield return new WaitForSeconds(1f);

        UpdatePieceColliders();
        isMoving = false;

        // --- GỌI SIÊU TRÍ TUỆ STOCKFISH VỚI 3 MỨC ĐỘ KHÓ ---
        if (currentTurn == PieceColor.Black)
        {
            string history = GetBoardFEN();
            if (StockfishManager.Instance != null)
            {
                // Mặc định là 1 giây (Medium)
                int thinkTimeMs = 1000; 

                // Lấy độ khó người chơi đã chọn từ Menu (mặc định là 2 - Medium nếu chưa chọn)
                int difficulty = PlayerPrefs.GetInt("Difficulty", 2); 

                switch (difficulty)
                {
                    case 1: // DỄ (Easy)
                        thinkTimeMs = 50;  // Suy nghĩ cực nhanh (50ms) -> Dễ bị hớ hênh, đi sai.
                        break;
                    case 2: // THƯỜNG (Medium)
                        thinkTimeMs = 1000; // Suy nghĩ 1 giây -> Đánh chắc tay, ngang người chơi giỏi.
                        break;
                    case 3: // KHÓ (Hard)
                        thinkTimeMs = 3000; // Suy nghĩ 3 giây -> Tính toán sâu xa, đánh như Kiện tướng!
                        break;
                }

                Debug.Log($"AI Đang suy nghĩ ở mức: {difficulty} (Thời gian: {thinkTimeMs}ms)");
                StockfishManager.Instance.GetBestMove(history, thinkTimeMs); 
            }
        }
    }

    private void ClearHighlights()
    {
        foreach (Square square in highlightedSquares)
        {
            square.SetHighlight(false);
            ChessPiece piece = pieceBoard[square.row, square.col];
            if (piece != null) piece.ResetHighlight();
        }
        highlightedSquares.Clear();
    }

    public bool IsMoving() => isMoving;

    // HÀM UNDO: Quay lại 1 nước đi
    // --- HÀM UNDO: SỬA ĐỂ LÙI 2 BƯỚC LIÊN TIẾP ---
    public void UndoLastMove()
    {
        // CHỐT CHẶN 1: Nếu cờ đang bay thì không cho Undo để tránh loạn mảng
        if (isMoving) 
        {
            Debug.LogWarning("Không thể Undo: Quân cờ đang di chuyển!");
            return;
        }

        // CHỐT CHẶN 2: Chỉ cho phép Undo khi ĐANG LÀ LƯỢT CỦA NGƯỜI CHƠI (White)
        // Nếu AI (Black) đang nghĩ hoặc đang đi, nút Undo sẽ bị vô hiệu hóa
        if (currentTurn == PieceColor.Black)
        {
            Debug.LogWarning("Không thể Undo: Đang là lượt của máy!");
            return;
        }

        // Kiểm tra xem lịch sử có đủ 2 nước (1 của mình, 1 của máy) để lùi không
        if (moveHistory.Count >= 2)
        {
            isMoving = true; // Khóa bàn cờ trong lúc lùi

            UndoSingleMove(); // Lùi nước của Máy
            UndoSingleMove(); // Lùi nước của Người chơi

            // Chuyển lượt về người chơi hiện tại (nếu đang White, lùi 2 bước vẫn là White)
            // Không đổi currentTurn vì lùi số chẵn lần

            UpdatePieceColliders();
            NotifyIfCheck();

            // Nếu AI lỡ phong hậu mà mình Undo, AI vẫn còn kẹt màn hình phong hậu, nên ta phải đóng nó lại
            if (PromotionManager.Instance != null && PromotionManager.Instance.promotionPanel != null)
            {
                PromotionManager.Instance.promotionPanel.SetActive(false);
            }

            // Xóa Hint nếu đang hiển thị
            if (HintManager.Instance != null) HintManager.Instance.ClearHint();

            isMoving = false; // Mở khóa bàn cờ
            Debug.Log("Đã Undo thành công 2 nước cờ (Máy và Người)!");
        }
        else
        {
            Debug.LogWarning("Không đủ lịch sử để Undo 2 nước!");
        }
    }

    private void UndoSingleMove()
    {
        MoveRecord lastMove = moveHistory.Pop();

        if (ChessClock.Instance != null)
        {
            ChessClock.Instance.whiteTimeRemaining = lastMove.whiteClockTime;
            ChessClock.Instance.blackTimeRemaining = lastMove.blackClockTime;
            ChessClock.Instance.UpdateClockUI();
        }

        // 1. Dời quân cờ vừa đi về lại vị trí cũ
        ChessPiece piece = lastMove.movedPiece;
        pieceBoard[lastMove.toRow, lastMove.toCol] = null; // Xóa ở chỗ mới
        
        piece.transform.position = BoardManager.Instance.GetWorldPosition(lastMove.fromRow, lastMove.fromCol);
        piece.currentRow = lastMove.fromRow;
        piece.currentCol = lastMove.fromCol;
        pieceBoard[lastMove.fromRow, lastMove.fromCol] = piece; // Đặt lại vào chỗ cũ
        
        piece.hasMoved = !lastMove.isFirstMove; // Phục hồi trạng thái hasMoved
        piece.UpdateOriginalPosition();

        // NẾU NƯỚC ĐI CỦA MÁY LÀ PHONG HẬU -> BIẾN HẬU LẠI THÀNH TỐT
        if (piece.type != PieceType.Pawn && (lastMove.fromRow == 1 && lastMove.toRow == 0 || lastMove.fromRow == 6 && lastMove.toRow == 7))
        {
            // Trong game của bạn, đoạn này cần gọi lệnh biến hình lại thành Pawn.
            // Nếu bạn sinh quân mới khi phong hậu, bạn sẽ cần Destroy(piece) và Instantiate lại Pawn ở đây.
            // Tạm thời nếu bạn chỉ đổi model/type:
            piece.type = PieceType.Pawn; 
            // (Thêm code đổi hình dạng về Tốt nếu bạn có)
        }

        // 2. Phục hồi quân cờ bị ăn (nếu có)
        if (lastMove.capturedPiece != null)
        {
            ChessPiece captured = lastMove.capturedPiece;
            captured.transform.position = BoardManager.Instance.GetWorldPosition(lastMove.toRow, lastMove.toCol);
            captured.currentRow = lastMove.toRow;
            captured.currentCol = lastMove.toCol;
            pieceBoard[lastMove.toRow, lastMove.toCol] = captured;
            
            captured.SetColliderEnabled(true);
            captured.gameObject.SetActive(true); 
            
            // Xóa quân bị ăn khỏi danh sách hiển thị trên UI
            if (CaptureManager.Instance != null)
                CaptureManager.Instance.RemoveLastCapturedPiece(captured);
        }
    }

    // Dịch bàn cờ hiện tại sang mã FEN chuẩn quốc tế cho Stockfish đọc
    public string GetBoardFEN()
    {
        string fen = "";
        
        // 1. Quét vị trí quân cờ (từ hàng 8 xuống hàng 1)
        for (int row = 7; row >= 0; row--)
        {
            int emptyCount = 0;
            for (int col = 0; col < 8; col++)
            {
                ChessPiece piece = pieceBoard[row, col];
                if (piece == null)
                {
                    emptyCount++;
                }
                else
                {
                    if (emptyCount > 0)
                    {
                        fen += emptyCount.ToString();
                        emptyCount = 0;
                    }
                    char pChar = GetPieceChar(piece);
                    fen += pChar;
                }
            }
            if (emptyCount > 0) fen += emptyCount.ToString();
            if (row > 0) fen += "/";
        }

        // 2. Lượt đi (w = Trắng, b = Đen)
        fen += (currentTurn == PieceColor.White) ? " w " : " b ";

        // 3. Quyền nhập thành (Để đơn giản cho AI phụ, ta tạm bỏ qua quyền nhập thành, set là "-")
        // Nếu bạn muốn AI nhập thành được, phần này cần code khá dài để track Vua và Xe di chuyển chưa.
        // Tạm thời set "-" để sửa lỗi Vua đi lòng vòng trước đã.
        fen += "- ";

        // 4. Bắt tốt qua đường (En Passant) - Tạm bỏ qua
        fen += "- ";

        // 5. Số nước đi kể từ lần cuối ăn quân hoặc đẩy Tốt (Halfmove clock)
        int halfMoves = moveHistory.Count;
        fen += halfMoves.ToString() + " ";

        // 6. Số vòng lượt đi (Fullmove number - cứ mỗi lượt Trắng + Đen tính là 1 vòng)
        int fullMoves = (moveHistory.Count / 2) + 1;
        fen += fullMoves.ToString();

        return fen;
    }

    // Hàm phụ trợ dịch Quân cờ sang Chữ cái (Trắng = In hoa, Đen = In thường)
    private char GetPieceChar(ChessPiece piece)
    {
        char c = 'p';
        switch (piece.type)
        {
            case PieceType.King: c = 'k'; break;
            case PieceType.Queen: c = 'q'; break;
            case PieceType.Rook: c = 'r'; break;
            case PieceType.Bishop: c = 'b'; break;
            case PieceType.Knight: c = 'n'; break;
            case PieceType.Pawn: c = 'p'; break;
        }
        return (piece.color == PieceColor.White) ? char.ToUpper(c) : c;
    }
}
