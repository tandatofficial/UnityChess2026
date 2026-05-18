using UnityEngine;

public class BoardManager : MonoBehaviour, IBoardManager
{
    public static BoardManager Instance;

    public GameObject squarePrefab;         // Chỉ 1 prefab duy nhất
    public GameObject whiteSquares;         // Cha của ô trắng
    public GameObject blackSquares;         // Cha của ô đen
    public GameObject boardBorder;

    public int boardSize = 8;
    public float squareSize = 1f;

    public Material whiteMaterial;
    public Material blackMaterial;
    public Material highlightMaterial;
    public Material selectedHighlightMaterial;

    public GameObject[,] squares;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        InitializeBoard();
    }

    public void InitializeBoard()
    {
        squares = new GameObject[boardSize, boardSize];
        GenerateBoard();
        GenerateBorder();
    }

    private void GenerateBoard()
    {
        float halfBoard = (boardSize * squareSize) / 2f;

        for (int row = 0; row < boardSize; row++)
        {
            for (int col = 0; col < boardSize; col++)
            {
                float x = (col * squareSize) - halfBoard + squareSize / 2f;
                float z = (row * squareSize) - halfBoard + squareSize / 2f;
                Vector3 position = new Vector3(x, 0, z);

                bool isWhite = (row + col) % 2 == 0;
                Transform parent = isWhite ? whiteSquares.transform : blackSquares.transform;

                GameObject square = Instantiate(squarePrefab, position, Quaternion.identity, parent);
                square.name = $"Square_{row}_{col}";

                Square squareScript = square.GetComponent<Square>() ?? square.AddComponent<Square>();
                squareScript.row = row;
                squareScript.col = col;
                squareScript.SetMaterials(whiteMaterial, blackMaterial, highlightMaterial);
                squareScript.ApplyInitialMaterial(isWhite);

                squares[row, col] = square;
            }
        }
    }

    private void GenerateBorder()
    {
        if (boardBorder == null) return;

        GameObject border = Instantiate(boardBorder, Vector3.zero, Quaternion.identity, transform);
        border.transform.localScale = new Vector3(1, 1, 1);
        border.name = "BoardBorder";
    }

    public Vector3 GetWorldPosition(int row, int col)
    {
        float halfBoard = (boardSize * squareSize) / 2f;
        float x = (col * squareSize) - halfBoard + squareSize / 2f;
        float z = (row * squareSize) - halfBoard + squareSize / 2f;
        return new Vector3(x, 0.5f, z);
    }
}
