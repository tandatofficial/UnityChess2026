using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CaptureManager : MonoBehaviour
{
    public static CaptureManager Instance;

    public Transform whiteCapturedArea;
    public Transform blackCapturedArea;

    private float capturedOffsetZ = 1f;
    private float moveDuration = 0.5f;

    private List<ChessPiece> whiteCapturedPieces = new List<ChessPiece>();
    private List<ChessPiece> blackCapturedPieces = new List<ChessPiece>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void CapturePiece(ChessPiece piece)
    {
        piece.gameObject.SetActive(true); // Đảm bảo quân cờ không bị disable

        // ❌ Tắt collider để không tương tác nữa
        var collider = piece.GetComponent<Collider>();
        if (collider != null) collider.enabled = false;

        if (piece.color == PieceColor.White)
        {
            whiteCapturedPieces.Add(piece);
            MoveToCapturedAreaSmooth(piece, whiteCapturedArea, whiteCapturedPieces.Count);
        }
        else
        {
            blackCapturedPieces.Add(piece);
            MoveToCapturedAreaSmooth(piece, blackCapturedArea, blackCapturedPieces.Count);
        }

        piece.Highlight(false);
    }


    private void MoveToCapturedAreaSmooth(ChessPiece piece, Transform area, int count)
    {
        int index = count - 1;

        // Tính toán offset đối xứng: 0, +1, -1, +2, -2, ...
        int offsetIndex = (index % 2 == 0) ? -(index / 2) : (index + 1) / 2;
        float offsetZ = offsetIndex * capturedOffsetZ;

        Vector3 targetPosition = area.position + new Vector3(0, 0, offsetZ);

        // Bắt đầu coroutine di chuyển mượt
        StartCoroutine(SmoothMove(piece, targetPosition, moveDuration));
    }

    private IEnumerator SmoothMove(ChessPiece piece, Vector3 targetPos, float duration)
    {
        Vector3 startPos = piece.transform.position;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            piece.transform.position = Vector3.Lerp(startPos, targetPos, t);
            yield return null;
        }

        piece.transform.position = targetPos;
        piece.UpdateOriginalPosition();
    }
}
