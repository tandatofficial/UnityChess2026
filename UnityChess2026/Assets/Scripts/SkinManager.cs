using UnityEngine;
using TMPro;

[System.Serializable]
public class BoardTheme
{
    public string themeName; 
    public Material tableMaterial; 
    public Material borderMaterial; 
    public Material whiteSquareMaterial; 
    public Material blackSquareMaterial; 
}

[System.Serializable]
public class PieceTheme
{
    public string themeName;
    public Material whitePieceMaterial; 
    public Material blackPieceMaterial; 
}

public class SkinManager : MonoBehaviour
{
    public static SkinManager Instance;

    [Header("=== GIAO DIỆN TÙY CHỈNH ===")]
    public TMP_Dropdown boardSkinDropdown; 
    public TMP_Dropdown pieceSkinDropdown; 
    public TMP_Dropdown environmentDropdown;

    [Header("=== KẾT NỐI VỚI BÀN CỜ ===")]
    public Renderer tableRenderer;         
    public Transform whiteSquaresParent;   
    public Transform blackSquaresParent;   

    [Header("=== DANH SÁCH CHỦ ĐỀ (THEMES) ===")]
    public BoardTheme[] boardThemes; 
    public PieceTheme[] pieceThemes; 

    [Header("=== TÙY CHỈNH MÔI TRƯỜNG ===")]
    public Material[] skyboxes;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        Invoke("LoadSavedSkins", 0.1f);
    }

    public void LoadSavedSkins()
    {
        int savedBoardSkin = PlayerPrefs.GetInt("BoardSkin", 0);
        int savedPieceSkin = PlayerPrefs.GetInt("PieceSkin", 0); 
        int savedEnvironment = PlayerPrefs.GetInt("Environment", 0); // Đọc Môi trường đã lưu

        // Ép các Dropdown hiển thị đúng chữ
        if (boardSkinDropdown != null) boardSkinDropdown.SetValueWithoutNotify(savedBoardSkin); 
        if (pieceSkinDropdown != null) pieceSkinDropdown.SetValueWithoutNotify(savedPieceSkin); 
        if (environmentDropdown != null) environmentDropdown.SetValueWithoutNotify(savedEnvironment);

        ChangeBoardSkin(savedBoardSkin);
        ChangePieceSkin(savedPieceSkin); 
        ChangeEnvironment(savedEnvironment);
    }

    public void ChangePieceSkin(int index)
    {
        if (index >= 0 && index < pieceThemes.Length)
        {
            PieceTheme theme = pieceThemes[index];
            ChessPiece[] allPieces = FindObjectsOfType<ChessPiece>(true); 

            foreach (ChessPiece piece in allPieces)
            {
                piece.normalMaterial = (piece.color == PieceColor.White) ? theme.whitePieceMaterial : theme.blackPieceMaterial;
                piece.ResetHighlight();
            }

            PlayerPrefs.SetInt("PieceSkin", index);
            PlayerPrefs.Save();
        }
    }

    public Material GetCurrentPieceMaterial(PieceColor color)
    {
        int currentIndex = PlayerPrefs.GetInt("PieceSkin", 0);
        if (currentIndex >= 0 && currentIndex < pieceThemes.Length)
        {
            return color == PieceColor.White ? pieceThemes[currentIndex].whitePieceMaterial : pieceThemes[currentIndex].blackPieceMaterial;
        }
        return null;
    }

    public void ChangeBoardSkin(int index)
    {
        if (index >= 0 && index < boardThemes.Length)
        {
            BoardTheme theme = boardThemes[index];

            if (tableRenderer != null && theme.tableMaterial != null)
                tableRenderer.material = theme.tableMaterial;

            if (whiteSquaresParent != null && theme.borderMaterial != null)
            {
                Transform boardTransform = whiteSquaresParent.parent;
                Transform borderTransform = boardTransform.Find("BoardBorder");
                if (borderTransform != null)
                {
                    Renderer borderRenderer = borderTransform.GetComponent<Renderer>();
                    if (borderRenderer != null) borderRenderer.material = theme.borderMaterial;
                }
            }

            if (BoardManager.Instance != null)
            {
                BoardManager.Instance.whiteMaterial = theme.whiteSquareMaterial;
                BoardManager.Instance.blackMaterial = theme.blackSquareMaterial;
            }

            if (whiteSquaresParent != null && theme.whiteSquareMaterial != null)
            {
                foreach (Transform child in whiteSquaresParent)
                {
                    Square squareScript = child.GetComponent<Square>();
                    if (squareScript != null && BoardManager.Instance != null)
                    {
                        squareScript.SetMaterials(theme.whiteSquareMaterial, theme.blackSquareMaterial, BoardManager.Instance.highlightMaterial);
                        squareScript.ApplyInitialMaterial(true);
                    }
                }
            }

            if (blackSquaresParent != null && theme.blackSquareMaterial != null)
            {
                foreach (Transform child in blackSquaresParent)
                {
                    Square squareScript = child.GetComponent<Square>();
                    if (squareScript != null && BoardManager.Instance != null)
                    {
                        squareScript.SetMaterials(theme.whiteSquareMaterial, theme.blackSquareMaterial, BoardManager.Instance.highlightMaterial);
                        squareScript.ApplyInitialMaterial(false);
                    }
                }
            }

            PlayerPrefs.SetInt("BoardSkin", index);
            PlayerPrefs.Save();
        }
    }

    // --- HÀM ĐỔI MÔI TRƯỜNG ĐÃ CÓ SẴN ---
    public void ChangeEnvironment(int index)
    {
        if (index >= 0 && index < skyboxes.Length)
        {
            RenderSettings.skybox = skyboxes[index];
            DynamicGI.UpdateEnvironment(); // Bắt Unity tính toán lại ánh sáng cho hợp với bầu trời mới
            PlayerPrefs.SetInt("Environment", index);
            PlayerPrefs.Save();
        }
    }
}