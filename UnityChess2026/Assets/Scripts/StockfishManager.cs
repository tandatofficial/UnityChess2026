using System.Diagnostics;
using System.IO;
using System;
using UnityEngine;
using System.Collections.Generic;

public class StockfishManager : MonoBehaviour
{
    public static StockfishManager Instance;
    
    private Action<string> onMoveCalculated = null;
    private Process stockfishProcess;
    private StreamWriter streamWriter;
    private string bestMoveCache = "";
    private bool isCalculating = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        StartEngine();
    }

    public void StartEngine()
    {
        // Trỏ đường dẫn tới file .exe trong thư mục StreamingAssets
        string path = Path.Combine(Application.streamingAssetsPath, "stockfish.exe");

        stockfishProcess = new Process();
        stockfishProcess.StartInfo.FileName = path;
        stockfishProcess.StartInfo.UseShellExecute = false;
        stockfishProcess.StartInfo.RedirectStandardInput = true;
        stockfishProcess.StartInfo.RedirectStandardOutput = true;
        stockfishProcess.StartInfo.CreateNoWindow = true;

        // Đăng ký sự kiện lắng nghe phản hồi từ Stockfish (chạy luồng riêng)
        stockfishProcess.OutputDataReceived += OnOutputDataReceived;

        stockfishProcess.Start();
        stockfishProcess.BeginOutputReadLine(); // Bắt đầu lắng nghe
        streamWriter = stockfishProcess.StandardInput;

        SendCommand("uci");
        SendCommand("isready");
    }

    public void SendCommand(string command)
    {
        if (streamWriter != null)
        {
            streamWriter.WriteLine(command);
            streamWriter.Flush();
        }
    }

    public void GetBestMove(string moveHistoryStr, int thinkTimeMs = 1000, Action<string> callback = null)
    {
        if (isCalculating) 
        {
            UnityEngine.Debug.LogWarning("⚠️ Bỏ qua lệnh: AI đang bận suy nghĩ rồi!");
            return;
        }

        isCalculating = true;   
        bestMoveCache = "";
        onMoveCalculated = callback; // Lưu lại nhiệm vụ trước khi nghĩ

        // Tự động cấu hình độ khó (sức mạnh) cho Stockfish
        if (callback == null)
        {
            // Lấy độ khó người chơi đã chọn (1: Easy, 2: Medium, 3: Hard)
            int difficulty = PlayerPrefs.GetInt("Difficulty", 2);
            SetDifficulty(difficulty);
        }
        else
        {
            // Khi xem gợi ý (Hint), dùng mức tốt nhất (Hard) để có nước đi chuẩn xác nhất
            SetDifficulty(3);
        }

        UnityEngine.Debug.Log($"🧠 AI bắt đầu suy nghĩ trong {thinkTimeMs}ms...");
        
        SendCommand($"position fen {moveHistoryStr}");
        SendCommand($"go movetime {thinkTimeMs}");
    }

    public void SetDifficulty(int difficulty)
    {
        // difficulty: 1 = Easy, 2 = Medium, 3 = Hard
        int skillLevel = 20;
        int targetElo = 1350;
        bool limitStrength = false;

        switch (difficulty)
        {
            case 1: // Dễ (Easy)
                skillLevel = 0;      // Skill level thấp nhất (0/20) để đi nhiều nước lỗi ngớ ngẩn
                targetElo = 450;     // Giới hạn Elo thấp nhất (800)
                limitStrength = true;
                break;
            case 2: // Thường (Medium)
                skillLevel = 5;      // Skill level trung bình khá (5/20)
                targetElo = 800;    // Elo trung bình (1350)
                limitStrength = true;
                break;
            case 3: // Khó (Hard)
                skillLevel = 20;     // Tối đa sức mạnh (Mặc định)
                limitStrength = false;
                break;
        }

        if (limitStrength)
        {
            SendCommand("setoption name UCI_LimitStrength value true");
            SendCommand($"setoption name UCI_Elo value {targetElo}");
        }
        else
        {
            SendCommand("setoption name UCI_LimitStrength value false");
        }

        SendCommand($"setoption name Skill Level value {skillLevel}");
        UnityEngine.Debug.Log($"[StockfishManager] Thiết lập độ khó: {difficulty} (Skill Level: {skillLevel}, LimitStrength: {limitStrength}, Elo: {targetElo})");
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        // Bắt chuỗi kết quả trả về, tìm dòng bắt đầu bằng chữ "bestmove"
        if (!string.IsNullOrEmpty(e.Data) && e.Data.StartsWith("bestmove"))
        {
            // Cắt chuỗi lấy nước đi (vd: "bestmove e7e5 ponder d2d4" -> lấy "e7e5")
            bestMoveCache = e.Data.Split(' ')[1];
        }
    }

    private void Update()
    {
        if (isCalculating && !string.IsNullOrEmpty(bestMoveCache))
        {
            isCalculating = false;
            string moveToExecute = bestMoveCache;
            bestMoveCache = "";

            // NẾU CÓ NGƯỜI ĐẶT HÀNG GỢI Ý (Nút Hint gọi)
            if (onMoveCalculated != null)
            {
                onMoveCalculated.Invoke(moveToExecute); // Giao hàng (trả chuỗi e2e4 về cho UI)
                onMoveCalculated = null; // Xóa sổ đơn hàng
            }
            else // NẾU KHÔNG AI ĐẶT HÀNG (Đến lượt AI tự đánh)
            {
                ExecuteStockfishMove(moveToExecute);
            }
        }
    }

    private void ExecuteStockfishMove(string move)
    {
        // Giải mã ngôn ngữ UCI (vd: "e7" -> col:4, row:6)
        int fromCol = move[0] - 'a';
        int fromRow = move[1] - '1';
        int toCol = move[2] - 'a';
        int toRow = move[3] - '1';

        UnityEngine.Debug.Log($"🤖 Stockfish chọn: {move} -> ({fromRow},{fromCol}) tới ({toRow},{toCol})");

        // Gọi thẳng hàm Network có sẵn
        string formattedMove = $"{fromRow},{fromCol}->{toRow},{toCol}";
        GameManager.Instance.ApplyMoveFromNetwork(formattedMove);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        
        if (stockfishProcess != null)
        {
            try
            {
                bool hasExited = true;
                try
                {
                    hasExited = stockfishProcess.HasExited;
                }
                catch (System.InvalidOperationException)
                {
                    hasExited = true; 
                }

                if (!hasExited)
                {
                    SendCommand("quit");
                    stockfishProcess.Close();
                }
                stockfishProcess.Dispose();
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[StockfishManager] Error closing engine on destroy: " + ex.Message);
            }
            finally
            {
                stockfishProcess = null;
            }
        }
    }

    private void OnApplicationQuit()
    {
        if (stockfishProcess != null)
        {
            try
            {
                bool hasExited = true;
                try
                {
                    hasExited = stockfishProcess.HasExited;
                }
                catch (System.InvalidOperationException)
                {
                    hasExited = true;
                }

                if (!hasExited)
                {
                    SendCommand("quit");
                    stockfishProcess.Close();
                }
                stockfishProcess.Dispose();
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[StockfishManager] Error closing engine on quit: " + ex.Message);
            }
            finally
            {
                stockfishProcess = null;
            }
        }
    }
}